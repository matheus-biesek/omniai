using Microsoft.Extensions.Options;
using Shared.Entities;
using Shared.Messaging;
using Shared.Security;
using Webhook.Application.ReceberMetrica;
using Webhook.Domain.Abstractions;

namespace Tests.Webhook;

public class ReceberMetricaUseCaseTests
{
    private const string Pepper = "pepper-de-teste";

    private sealed class FakeApiKeyRepository : IApiKeyRepository
    {
        public Dictionary<string, ApiKey> ActiveByHash { get; } = new();
        public List<string> HashesConsultados { get; } = new();

        public Task<ApiKey?> FindActiveByHashAsync(string keyHash, CancellationToken cancellationToken)
        {
            HashesConsultados.Add(keyHash);
            return Task.FromResult(ActiveByHash.GetValueOrDefault(keyHash));
        }
    }

    private sealed class FakePublisher : IUsageEventPublisher
    {
        public PublishOutcome Outcome { get; set; } = PublishOutcome.Published;
        public List<UsageEventMessage> Publicadas { get; } = new();

        public Task<PublishOutcome> PublishAsync(UsageEventMessage message, CancellationToken cancellationToken)
        {
            if (Outcome == PublishOutcome.Published)
            {
                Publicadas.Add(message);
            }

            return Task.FromResult(Outcome);
        }
    }

    private readonly FakeApiKeyRepository _repository = new();
    private readonly FakePublisher _publisher = new();
    private readonly ReceberMetricaUseCase _useCase;

    public ReceberMetricaUseCaseTests()
    {
        _useCase = new ReceberMetricaUseCase(
            _repository,
            _publisher,
            Options.Create(new ApiKeyHashingOptions { PepperSecret = Pepper }));
    }

    private void CadastrarChave(string rawKey, string projectName)
    {
        var project = new Project { Id = Guid.NewGuid(), Name = projectName };
        _repository.ActiveByHash[ApiKeyHasher.Hash(rawKey, Pepper)] = new ApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Project = project,
        };
    }

    private static ReceberMetricaCommand Comando(string rawKey, decimal? cost = 0.5m) => new(
        ApiKeyRaw: rawKey,
        Provider: "openai",
        Model: "gpt-4o",
        PromptTokens: 10,
        CompletionTokens: 20,
        TotalTokens: 30,
        CostUsd: cost,
        LatencyMs: 123,
        Status: "success",
        Timestamp: new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public async Task ChaveInexistente_RetornaApiKeyInvalida_ENaoPublica()
    {
        var result = await _useCase.ExecutarAsync(Comando("ombk_nao-existe"), CancellationToken.None);

        Assert.Equal(ReceberMetricaStatus.ApiKeyInvalida, result.Status);
        Assert.Empty(_publisher.Publicadas);
    }

    [Fact]
    public async Task ConsultaORepositorioPeloHashComPepper_NuncaPelaChaveCrua()
    {
        await _useCase.ExecutarAsync(Comando("ombk_xyz"), CancellationToken.None);

        var consultado = Assert.Single(_repository.HashesConsultados);
        Assert.Equal(ApiKeyHasher.Hash("ombk_xyz", Pepper), consultado);
        Assert.NotEqual("ombk_xyz", consultado);
    }

    [Fact]
    public async Task ChaveValida_PublicaMensagemComTodosOsCampos_ERetornaAceito()
    {
        CadastrarChave("ombk_valida", "checkout-service");

        var result = await _useCase.ExecutarAsync(Comando("ombk_valida"), CancellationToken.None);

        Assert.Equal(ReceberMetricaStatus.Aceito, result.Status);
        var msg = Assert.Single(_publisher.Publicadas);
        Assert.Equal("checkout-service", msg.Project);
        Assert.Equal("openai", msg.Provider);
        Assert.Equal("gpt-4o", msg.Model);
        Assert.Equal(10, msg.PromptTokens);
        Assert.Equal(20, msg.CompletionTokens);
        Assert.Equal(30, msg.TotalTokens);
        Assert.Equal(0.5m, msg.CostUsd);
        Assert.Equal(123, msg.LatencyMs);
        Assert.Equal("success", msg.Status);
        Assert.Equal(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), msg.Timestamp);
    }

    [Fact]
    public async Task ProjetoDaMensagemVemDaApiKey_NaoDoPayload()
    {
        // O comando nem carrega "project" - a identidade do projeto e sempre a da chave autenticada,
        // entao um SDK nao consegue reportar consumo em nome de outro projeto.
        CadastrarChave("ombk_a", "projeto-a");

        await _useCase.ExecutarAsync(Comando("ombk_a"), CancellationToken.None);

        Assert.Equal("projeto-a", Assert.Single(_publisher.Publicadas).Project);
    }

    [Fact]
    public async Task CustoNulo_ChegaNuloNaMensagem()
    {
        CadastrarChave("ombk_valida", "p");

        await _useCase.ExecutarAsync(Comando("ombk_valida", cost: null), CancellationToken.None);

        Assert.Null(Assert.Single(_publisher.Publicadas).CostUsd);
    }

    [Fact]
    public async Task FilaCheia_RetornaFilaCheia()
    {
        CadastrarChave("ombk_valida", "p");
        _publisher.Outcome = PublishOutcome.QueueFull;

        var result = await _useCase.ExecutarAsync(Comando("ombk_valida"), CancellationToken.None);

        Assert.Equal(ReceberMetricaStatus.FilaCheia, result.Status);
    }
}
