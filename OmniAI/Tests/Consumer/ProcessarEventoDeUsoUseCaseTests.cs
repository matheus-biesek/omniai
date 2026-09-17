using System.Text.Json;
using Consumer.Application.ProcessarEventoDeUso;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Entities;
using Shared.Messaging;

namespace Tests.Consumer;

public class ProcessarEventoDeUsoUseCaseTests
{
    private readonly FakeProjectLookup _projects = new();
    private readonly FakeUsageRecordRepository _records = new();
    private readonly FakeUsageEventLogRepository _logs = new();
    private readonly FakeNotifier _notifier = new();
    private readonly ProcessarEventoDeUsoUseCase _useCase;
    private readonly Guid _projectId = Guid.NewGuid();

    public ProcessarEventoDeUsoUseCaseTests()
    {
        _projects.Projects["checkout-service"] = _projectId;
        _useCase = new ProcessarEventoDeUsoUseCase(
            _projects,
            _records,
            _logs,
            _notifier,
            Options.Create(new RetryOptions { MaxAttempts = 5, BaseDelaySeconds = 10 }),
            NullLogger<ProcessarEventoDeUsoUseCase>.Instance);
    }

    // Serializa exatamente como o Webhook (RedisUsageEventPublisher) faz.
    private static string PayloadDoWebhook(string project = "checkout-service", decimal? cost = 0.00045m) =>
        JsonSerializer.Serialize(new UsageEventMessage
        {
            Project = project,
            Provider = "openai",
            Model = "gpt-4o-mini",
            PromptTokens = 1000,
            CompletionTokens = 500,
            TotalTokens = 1500,
            CostUsd = cost,
            LatencyMs = 812,
            Status = "success",
            Timestamp = new DateTime(2026, 9, 16, 10, 30, 0, DateTimeKind.Utc),
        });

    private static UsageEventLog Log(string payload, int attemptCount = 0) => new()
    {
        Id = Guid.NewGuid(),
        RedisEntryId = "1726500000000-0",
        Payload = payload,
        Status = attemptCount == 0 ? UsageEventLogStatus.Pendente : UsageEventLogStatus.FalhaTransiente,
        AttemptCount = attemptCount,
        ReceivedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task EventoValido_PersisteRecordComTodosOsCampos_MarcaProcessado_ENotifica()
    {
        var log = Log(PayloadDoWebhook());

        await _useCase.ExecutarAsync(log, CancellationToken.None);

        var record = Assert.Single(_records.Records);
        Assert.Equal(_projectId, record.ProjectId);
        Assert.Equal(log.Id, record.SourceEventLogId);
        Assert.Equal("openai", record.Provider);
        Assert.Equal("gpt-4o-mini", record.Model);
        Assert.Equal(1000, record.PromptTokens);
        Assert.Equal(500, record.CompletionTokens);
        Assert.Equal(1500, record.TotalTokens);
        Assert.Equal(0.00045m, record.CostUsd);
        Assert.Equal(812, record.LatencyMs);
        Assert.Equal("success", record.Status);
        Assert.Equal(new DateTime(2026, 9, 16, 10, 30, 0, DateTimeKind.Utc), record.OccurredAt);
        Assert.Equal(DateTimeKind.Utc, record.OccurredAt.Kind);

        Assert.Equal(new[] { log.Id }, _logs.Processed);
        Assert.Empty(_logs.Transient);
        Assert.Empty(_logs.Permanent);

        var notified = Assert.Single(_notifier.Notified);
        Assert.Equal("checkout-service", notified.Project);
        Assert.Same(record, notified.Record);
    }

    [Fact]
    public async Task CustoNulo_PersisteNulo_NaoZero()
    {
        await _useCase.ExecutarAsync(Log(PayloadDoWebhook(cost: null)), CancellationToken.None);

        Assert.Null(Assert.Single(_records.Records).CostUsd);
    }

    [Fact]
    public async Task RecordJaExiste_SoMarcaProcessado_SemDuplicar_NemNotificar()
    {
        var log = Log(PayloadDoWebhook());
        _records.Records.Add(new UsageRecord { SourceEventLogId = log.Id });

        await _useCase.ExecutarAsync(log, CancellationToken.None);

        Assert.Single(_records.Records);
        Assert.Equal(new[] { log.Id }, _logs.Processed);
        Assert.Empty(_notifier.Notified);
    }

    [Fact]
    public async Task ProjetoInexistente_FalhaPermanenteNaPrimeiraTentativa()
    {
        var log = Log(PayloadDoWebhook(project: "projeto-apagado"));

        await _useCase.ExecutarAsync(log, CancellationToken.None);

        var permanent = Assert.Single(_logs.Permanent);
        Assert.Equal(log.Id, permanent.Id);
        Assert.Contains("projeto-apagado", permanent.Error);
        Assert.Empty(_logs.Transient);
        Assert.Empty(_records.Records);
    }

    [Fact]
    public async Task PayloadJsonNull_FalhaPermanente()
    {
        var log = Log("null");

        await _useCase.ExecutarAsync(log, CancellationToken.None);

        Assert.Single(_logs.Permanent);
        Assert.Empty(_logs.Transient);
    }

    // DOCS/05-modulo-consumer.md: "Payload corrompido" -> Permanente, "na primeira tentativa, nao
    // entra na fila de retry".
    [Theory]
    [InlineData("{isto nao e json")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"Project\":\"checkout-service\"}")]
    public async Task PayloadCorrompido_FalhaPermanenteNaPrimeiraTentativa(string payload)
    {
        var log = Log(payload);

        await _useCase.ExecutarAsync(log, CancellationToken.None);

        Assert.Single(_logs.Permanent);
        Assert.Empty(_logs.Transient);
    }

    [Fact]
    public async Task ErroTransiente_PrimeiraTentativa_AgendaRetryEm10s()
    {
        _records.ThrowOnAdd = new TimeoutException("postgres fora");
        var log = Log(PayloadDoWebhook());
        var antes = DateTime.UtcNow;

        await _useCase.ExecutarAsync(log, CancellationToken.None);

        var t = Assert.Single(_logs.Transient);
        Assert.Equal(1, t.Attempt);
        Assert.Equal("postgres fora", t.Error);
        Assert.InRange(t.NextRetryAt, antes.AddSeconds(10), DateTime.UtcNow.AddSeconds(10));
        Assert.Empty(_logs.Permanent);
        Assert.Empty(_logs.Processed);
        Assert.Empty(_notifier.Notified);
    }

    [Theory]
    [InlineData(1, 2, 20)]
    [InlineData(2, 3, 40)]
    [InlineData(3, 4, 80)]
    public async Task ErroTransiente_BackoffExponencial(int attemptCountAtual, int proximaTentativa, int delayEsperadoSegundos)
    {
        _records.ThrowOnAdd = new TimeoutException("x");
        var antes = DateTime.UtcNow;

        await _useCase.ExecutarAsync(Log(PayloadDoWebhook(), attemptCountAtual), CancellationToken.None);

        var t = Assert.Single(_logs.Transient);
        Assert.Equal(proximaTentativa, t.Attempt);
        Assert.InRange(t.NextRetryAt, antes.AddSeconds(delayEsperadoSegundos), DateTime.UtcNow.AddSeconds(delayEsperadoSegundos));
    }

    [Fact]
    public async Task ErroTransiente_NaQuintaTentativa_ViraFalhaPermanente()
    {
        _records.ThrowOnAdd = new TimeoutException("x");

        await _useCase.ExecutarAsync(Log(PayloadDoWebhook(), attemptCount: 4), CancellationToken.None);

        Assert.Single(_logs.Permanent);
        Assert.Empty(_logs.Transient);
    }

    [Fact]
    public async Task FalhaAoNotificar_NaoEhFalhaDeProcessamento()
    {
        _notifier.ThrowOnNotify = new InvalidOperationException("hub fora");
        var log = Log(PayloadDoWebhook());

        await _useCase.ExecutarAsync(log, CancellationToken.None);

        Assert.Single(_records.Records);
        Assert.Equal(new[] { log.Id }, _logs.Processed);
        Assert.Empty(_logs.Transient);
        Assert.Empty(_logs.Permanent);
    }

    [Fact]
    public async Task FalhaAoMarcarProcessado_DepoisReprocessamentoNaoDuplica()
    {
        var log = Log(PayloadDoWebhook());
        _logs.ThrowOnMarkProcessed = new TimeoutException("caiu antes de marcar");

        await _useCase.ExecutarAsync(log, CancellationToken.None);

        Assert.Single(_records.Records);
        Assert.Single(_logs.Transient);

        // proximo ciclo: banco voltou
        _logs.ThrowOnMarkProcessed = null;
        await _useCase.ExecutarAsync(log, CancellationToken.None);

        Assert.Single(_records.Records);
        Assert.Equal(new[] { log.Id }, _logs.Processed);
    }
}
