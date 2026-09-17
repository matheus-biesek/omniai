using System.Text.Json;
using Consumer.Application.ProcessarEventoDeUso;
using Consumer.Application.RegistrarEvento;
using Consumer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Entities;
using Shared.Messaging;
using Tests.Consumer;

namespace Tests.Integration;

[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class RegistroIdempotenteIntegrationTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db;

    public RegistroIdempotenteIntegrationTests(DatabaseFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Payload() => JsonSerializer.Serialize(new UsageEventMessage
    {
        Project = "checkout",
        Provider = "openai",
        Model = "gpt-4o",
        PromptTokens = 100,
        CompletionTokens = 100,
        TotalTokens = 200,
        CostUsd = 1.5m,
        LatencyMs = 10,
        Status = "success",
        Timestamp = DateTime.UtcNow,
    });

    // Cenario real: o Consumer grava o log, o XACK falha (Redis caiu / processo morreu), e a mesma
    // entrada volta na leitura de pendencias ("0"). Nao pode virar dois logs nem dois UsageRecords.
    [Fact]
    public async Task MesmaEntradaDoRedisRegistradaDuasVezes_GeraUmLogEUmRecordSo()
    {
        await using (var seed = _db.CreateWriteContext())
        {
            seed.Projects.Add(new Project { Id = Guid.NewGuid(), Name = "checkout", CreatedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        var payload = Payload();
        for (var releitura = 0; releitura < 2; releitura++)
        {
            await using var ctx = _db.CreateWriteContext();
            await new RegistrarEventoUseCase(new EfUsageEventLogRepository(ctx))
                .ExecutarAsync("1789610895607-0", payload, CancellationToken.None);
        }

        await using (var ctx = _db.CreateWriteContext())
        {
            var logs = new EfUsageEventLogRepository(ctx);
            var useCase = new ProcessarEventoDeUsoUseCase(
                new EfProjectLookup(ctx),
                new EfUsageRecordRepository(ctx),
                logs,
                new FakeNotifier(),
                Options.Create(new RetryOptions()),
                NullLogger<ProcessarEventoDeUsoUseCase>.Instance);

            foreach (var log in await logs.GetDueForProcessingAsync(50, CancellationToken.None))
            {
                await useCase.ExecutarAsync(log, CancellationToken.None);
            }
        }

        await using var check = _db.CreateWriteContext();
        Assert.Equal(1, await check.UsageEventLogs.CountAsync(l => l.RedisEntryId == "1789610895607-0"));
        Assert.Equal(1, await check.UsageRecords.CountAsync());
        Assert.Equal(1.5m, await check.UsageRecords.SumAsync(r => r.CostUsd));
    }

    [Fact]
    public async Task Banco_RejeitaDoisLogsComOMesmoRedisEntryId()
    {
        await using var ctx = _db.CreateWriteContext();
        ctx.UsageEventLogs.Add(new UsageEventLog { Id = Guid.NewGuid(), RedisEntryId = "1-0", Payload = "{}", ReceivedAt = DateTime.UtcNow });
        ctx.UsageEventLogs.Add(new UsageEventLog { Id = Guid.NewGuid(), RedisEntryId = "1-0", Payload = "{}", ReceivedAt = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
