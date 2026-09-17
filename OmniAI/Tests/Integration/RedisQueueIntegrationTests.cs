using System.Text.Json;
using Consumer.Infrastructure;
using Microsoft.Extensions.Options;
using Shared.Messaging;
using Webhook.Domain.Abstractions;
using Webhook.Infrastructure;

namespace Tests.Integration;

[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class RedisQueueIntegrationTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;
    private readonly string _stream = $"test:usage-events:{Guid.NewGuid():N}";

    public RedisQueueIntegrationTests(DatabaseFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _fixture.Redis.GetDatabase().KeyDeleteAsync(_stream);

    private RedisUsageEventPublisher Publisher(int maxQueueSize) => new(
        _fixture.Redis,
        Options.Create(new QueueBackpressureOptions { StreamKey = _stream, MaxQueueSize = maxQueueSize }));

    private RedisUsageEventReader Reader(string consumerName = "consumer-test") => new(
        _fixture.Redis,
        Options.Create(new QueueOptions { StreamKey = _stream, ConsumerGroup = "group-test", ConsumerName = consumerName, BatchSize = 50 }));

    private static UsageEventMessage Message(int tokens) => new()
    {
        Project = "p",
        Provider = "openai",
        Model = "gpt-4o",
        PromptTokens = tokens,
        CompletionTokens = 0,
        TotalTokens = tokens,
        CostUsd = null,
        LatencyMs = 1,
        Status = "success",
        Timestamp = DateTime.UtcNow,
    };

    [Fact]
    public async Task Backpressure_AceitaAteOLimite_EDepoisRecusa()
    {
        var publisher = Publisher(maxQueueSize: 3);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(PublishOutcome.Published, await publisher.PublishAsync(Message(i), CancellationToken.None));
        }

        Assert.Equal(PublishOutcome.QueueFull, await publisher.PublishAsync(Message(99), CancellationToken.None));
        Assert.Equal(3, await _fixture.Redis.GetDatabase().StreamLengthAsync(_stream));
    }

    [Fact]
    public async Task PublicadoPeloWebhook_ELidoPeloConsumer_ComMesmoPayload()
    {
        var reader = Reader();
        await reader.EnsureConsumerGroupAsync(CancellationToken.None);
        await reader.EnsureConsumerGroupAsync(CancellationToken.None); // idempotente (BUSYGROUP)

        var original = Message(42);
        await Publisher(100).PublishAsync(original, CancellationToken.None);

        var lidos = await reader.ReadNewAsync(CancellationToken.None);

        var entry = Assert.Single(lidos);
        var roundtrip = JsonSerializer.Deserialize<UsageEventMessage>(entry.Payload)!;
        Assert.Equal(42, roundtrip.TotalTokens);
        Assert.Equal(original.Timestamp, roundtrip.Timestamp);
        Assert.Null(roundtrip.CostUsd);
    }

    [Fact]
    public async Task MensagemNaoConfirmada_VoltaEmReadOwnPending_EConfirmadaSome()
    {
        var reader = Reader();
        await reader.EnsureConsumerGroupAsync(CancellationToken.None);
        await Publisher(100).PublishAsync(Message(1), CancellationToken.None);
        await Publisher(100).PublishAsync(Message(2), CancellationToken.None);

        var lidos = await reader.ReadNewAsync(CancellationToken.None);
        Assert.Equal(2, lidos.Count);
        Assert.Empty(await reader.ReadNewAsync(CancellationToken.None));

        await reader.AcknowledgeAsync(lidos[0].EntryId, CancellationToken.None);

        // "Reinicio" do processo: mesma identidade de consumer relê o que ficou pendente.
        var pendentes = await Reader().ReadOwnPendingAsync(CancellationToken.None);
        var pendente = Assert.Single(pendentes);
        Assert.Equal(lidos[1].EntryId, pendente.EntryId);
        Assert.Equal(lidos[1].Payload, pendente.Payload);

        await reader.AcknowledgeAsync(pendente.EntryId, CancellationToken.None);
        Assert.Empty(await reader.ReadOwnPendingAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FilaVoltaATerEspaco_DepoisQueOConsumerConfirmaAsMensagens()
    {
        // Backpressure deve medir o que ainda nao foi processado - nao o total historico da stream.
        var publisher = Publisher(maxQueueSize: 3);
        var reader = Reader();
        await reader.EnsureConsumerGroupAsync(CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(PublishOutcome.Published, await publisher.PublishAsync(Message(i), CancellationToken.None));
        }

        Assert.Equal(PublishOutcome.QueueFull, await publisher.PublishAsync(Message(99), CancellationToken.None));

        foreach (var entry in await reader.ReadNewAsync(CancellationToken.None))
        {
            await reader.AcknowledgeAsync(entry.EntryId, CancellationToken.None);
        }

        Assert.Equal(PublishOutcome.Published, await publisher.PublishAsync(Message(100), CancellationToken.None));
    }

    [Fact]
    public async Task Acknowledge_RemoveAEntradaDaStream()
    {
        var reader = Reader();
        await reader.EnsureConsumerGroupAsync(CancellationToken.None);
        await Publisher(100).PublishAsync(Message(1), CancellationToken.None);

        var entry = Assert.Single(await reader.ReadNewAsync(CancellationToken.None));
        await reader.AcknowledgeAsync(entry.EntryId, CancellationToken.None);

        var db = _fixture.Redis.GetDatabase();
        Assert.Equal(0, await db.StreamLengthAsync(_stream));
        Assert.Equal(0, (await db.StreamPendingAsync(_stream, "group-test")).PendingMessageCount);
    }

    [Fact]
    public async Task RemoveAcknowledged_ApagaSoAsJaConfirmadas_PreservaPendentesENaoLidas()
    {
        var db = _fixture.Redis.GetDatabase();
        var reader = Reader();
        await reader.EnsureConsumerGroupAsync(CancellationToken.None);
        var publisher = Publisher(100);

        await publisher.PublishAsync(Message(1), CancellationToken.None);
        await publisher.PublishAsync(Message(2), CancellationToken.None);
        await publisher.PublishAsync(Message(3), CancellationToken.None);
        var entregues = await reader.ReadNewAsync(CancellationToken.None);

        // Confirmacao "a moda antiga" (so XACK), como faziam as versoes anteriores do Consumer.
        await db.StreamAcknowledgeAsync(_stream, "group-test", entregues[0].EntryId);
        await db.StreamAcknowledgeAsync(_stream, "group-test", entregues[1].EntryId);
        await publisher.PublishAsync(Message(4), CancellationToken.None); // ainda nao lida
        Assert.Equal(4, await db.StreamLengthAsync(_stream));

        await reader.RemoveAcknowledgedAsync(CancellationToken.None);

        Assert.Equal(2, await db.StreamLengthAsync(_stream));
        Assert.Equal(entregues[2].EntryId, Assert.Single(await reader.ReadOwnPendingAsync(CancellationToken.None)).EntryId);
        Assert.Equal(4, JsonSerializer.Deserialize<UsageEventMessage>(Assert.Single(await reader.ReadNewAsync(CancellationToken.None)).Payload)!.TotalTokens);
    }

    [Fact]
    public async Task RemoveAcknowledged_SemPendentes_ApagaAteAUltimaEntregue()
    {
        var db = _fixture.Redis.GetDatabase();
        var reader = Reader();
        await reader.EnsureConsumerGroupAsync(CancellationToken.None);
        var publisher = Publisher(100);

        await publisher.PublishAsync(Message(1), CancellationToken.None);
        await publisher.PublishAsync(Message(2), CancellationToken.None);
        foreach (var e in await reader.ReadNewAsync(CancellationToken.None))
        {
            await db.StreamAcknowledgeAsync(_stream, "group-test", e.EntryId);
        }

        await publisher.PublishAsync(Message(3), CancellationToken.None);

        await reader.RemoveAcknowledgedAsync(CancellationToken.None);

        Assert.Equal(1, await db.StreamLengthAsync(_stream));
        Assert.Single(await reader.ReadNewAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RemoveAcknowledged_StreamVazia_NaoFazNada()
    {
        var reader = Reader();
        await reader.EnsureConsumerGroupAsync(CancellationToken.None);

        await reader.RemoveAcknowledgedAsync(CancellationToken.None);

        Assert.Equal(0, await _fixture.Redis.GetDatabase().StreamLengthAsync(_stream));
    }

    [Fact]
    public async Task MensagensPublicadasAntesDoConsumerGroupExistir_NaoSaoPerdidas()
    {
        // Primeiro boot: o Webhook pode aceitar eventos antes do Consumer criar o grupo.
        await Publisher(100).PublishAsync(Message(1), CancellationToken.None);

        var reader = Reader();
        await reader.EnsureConsumerGroupAsync(CancellationToken.None);

        Assert.Single(await reader.ReadNewAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConsumerGroupCriadoAntesDaStreamExistir_NaoPerdeMensagensPublicadasDepois()
    {
        var reader = Reader();
        await reader.EnsureConsumerGroupAsync(CancellationToken.None);

        await Publisher(100).PublishAsync(Message(7), CancellationToken.None);

        Assert.Single(await reader.ReadNewAsync(CancellationToken.None));
    }
}
