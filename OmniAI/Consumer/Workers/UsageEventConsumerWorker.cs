using Consumer.Application.ProcessarEventoDeUso;
using Consumer.Application.RegistrarEvento;
using Consumer.Domain.Abstractions;
using Microsoft.Extensions.Options;

namespace Consumer.Workers;

public class UsageEventConsumerWorker : BackgroundService
{
    private readonly IUsageEventReader _reader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerOptions _options;
    private readonly ILogger<UsageEventConsumerWorker> _logger;

    public UsageEventConsumerWorker(
        IUsageEventReader reader,
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> options,
        ILogger<UsageEventConsumerWorker> logger)
    {
        _reader = reader;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _reader.EnsureConsumerGroupAsync(stoppingToken);

        try
        {
            // Entradas ja confirmadas que ficaram na stream (ex: de versoes que so faziam XACK, sem
            // XDEL) ainda contam no XLEN do backpressure do Webhook. Limpeza e so manutencao: se
            // falhar, o worker segue normalmente.
            await _reader.RemoveAcknowledgedAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Nao foi possivel remover da stream as entradas ja confirmadas.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RegistrarNovosEventosAsync(stoppingToken);
                await ProcessarEventosPendentesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Rede de seguranca de nivel mais alto: falha de infraestrutura (Redis ou Postgres
                // temporariamente inalcancavel - ex: conexao caiu apos o SO suspender/hibernar) nao
                // pode derrubar o worker inteiro. Loga e tenta de novo no proximo ciclo.
                _logger.LogError(ex, "Falha inesperada no ciclo do worker, tentando novamente no proximo ciclo.");
            }

            await Task.Delay(_options.PollIntervalMs, stoppingToken);
        }
    }

    private async Task RegistrarNovosEventosAsync(CancellationToken cancellationToken)
    {
        var pendentesDoProcesso = await _reader.ReadOwnPendingAsync(cancellationToken);
        var novos = await _reader.ReadNewAsync(cancellationToken);

        var entradas = pendentesDoProcesso.Concat(novos).ToList();
        if (entradas.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<RegistrarEventoUseCase>();

        foreach (var entrada in entradas)
        {
            try
            {
                var registradoAgora = await useCase.ExecutarAsync(entrada.EntryId, entrada.Payload, cancellationToken);
                if (!registradoAgora)
                {
                    _logger.LogInformation("Evento {EntryId} ja estava registrado (confirmacao anterior falhou), so confirmando.", entrada.EntryId);
                }

                await _reader.AcknowledgeAsync(entrada.EntryId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao registrar evento {EntryId}, permanece pendente no Redis.", entrada.EntryId);
            }
        }
    }

    private async Task ProcessarEventosPendentesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var eventLogRepository = scope.ServiceProvider.GetRequiredService<IUsageEventLogRepository>();
        var useCase = scope.ServiceProvider.GetRequiredService<ProcessarEventoDeUsoUseCase>();

        var pendentes = await eventLogRepository.GetDueForProcessingAsync(maxBatchSize: 50, cancellationToken);

        foreach (var log in pendentes)
        {
            try
            {
                await useCase.ExecutarAsync(log, cancellationToken);
            }
            catch (Exception ex)
            {
                // Rede de seguranca: o Use Case ja trata falha transiente/permanente internamente.
                // Se algo ainda assim escapar (bug, exception ao tentar marcar a falha), isso nao
                // pode derrubar o worker inteiro - um evento problematico nao pode parar todos os outros.
                _logger.LogCritical(ex, "Falha inesperada processando evento {EventLogId}, worker continua.", log.Id);
            }
        }
    }
}
