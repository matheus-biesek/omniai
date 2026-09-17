using System.Text.Json;
using ApiGraphQL.Domain;
using ApiGraphQL.Infrastructure;
using Consumer.Application.ProcessarEventoDeUso;
using Consumer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Entities;
using Shared.Messaging;
using Shared.Security;
using Tests.Consumer;
using WebhookApiKeyRepository = Webhook.Infrastructure.EfApiKeyRepository;

namespace Tests.Integration;

[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public class PersistenceIntegrationTests : IAsyncLifetime
{
    private readonly DatabaseFixture _db;

    public PersistenceIntegrationTests(DatabaseFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Project> SeedProjectAsync(string name)
    {
        await using var ctx = _db.CreateWriteContext();
        var project = new Project { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTime.UtcNow };
        ctx.Projects.Add(project);
        await ctx.SaveChangesAsync();
        return project;
    }

    private async Task<UsageEventLog> SeedLogAsync(UsageEventLogStatus status, DateTime receivedAt, DateTime? nextRetryAt = null)
    {
        await using var ctx = _db.CreateWriteContext();
        var log = new UsageEventLog
        {
            Id = Guid.NewGuid(),
            RedisEntryId = $"{receivedAt.Ticks}-0",
            Payload = "{}",
            Status = status,
            ReceivedAt = receivedAt,
            NextRetryAt = nextRetryAt,
        };
        ctx.UsageEventLogs.Add(log);
        await ctx.SaveChangesAsync();
        return log;
    }

    private async Task SeedRecordAsync(Guid projectId, string provider, int tokens, decimal? cost, DateTime occurredAt)
    {
        var log = await SeedLogAsync(UsageEventLogStatus.Processado, DateTime.UtcNow);
        await using var ctx = _db.CreateWriteContext();
        ctx.UsageRecords.Add(new UsageRecord
        {
            ProjectId = projectId,
            SourceEventLogId = log.Id,
            Provider = provider,
            Model = "m",
            TotalTokens = tokens,
            CostUsd = cost,
            Status = "success",
            OccurredAt = occurredAt,
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Migrations_CriamTodasAsTabelasEsperadas()
    {
        await using var ctx = _db.CreateWriteContext();

        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await ctx.Projects.CountAsync());
        Assert.Equal(0, await ctx.ApiKeys.CountAsync());
        Assert.Equal(0, await ctx.UsageEventLogs.CountAsync());
        Assert.Equal(0, await ctx.UsageRecords.CountAsync());
    }

    [Fact]
    public async Task Banco_RejeitaNomeDeProjetoDuplicado()
    {
        await SeedProjectAsync("dup");

        await Assert.ThrowsAsync<DbUpdateException>(() => SeedProjectAsync("dup"));
    }

    [Fact]
    public async Task Banco_RejeitaDoisRecordsParaOMesmoEventLog()
    {
        var project = await SeedProjectAsync("p");
        var log = await SeedLogAsync(UsageEventLogStatus.Processado, DateTime.UtcNow);

        await using var ctx = _db.CreateWriteContext();
        ctx.UsageRecords.Add(new UsageRecord { ProjectId = project.Id, SourceEventLogId = log.Id, Provider = "openai", Model = "m", Status = "success", OccurredAt = DateTime.UtcNow });
        ctx.UsageRecords.Add(new UsageRecord { ProjectId = project.Id, SourceEventLogId = log.Id, Provider = "openai", Model = "m", Status = "success", OccurredAt = DateTime.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task WebhookApiKeyRepository_SoEncontraChaveAtiva_ComProjetoCarregado()
    {
        var project = await SeedProjectAsync("checkout");
        await using (var ctx = _db.CreateWriteContext())
        {
            ctx.ApiKeys.Add(new ApiKey { Id = Guid.NewGuid(), ProjectId = project.Id, KeyHash = "ativa", CreatedAt = DateTime.UtcNow });
            ctx.ApiKeys.Add(new ApiKey { Id = Guid.NewGuid(), ProjectId = project.Id, KeyHash = "revogada", CreatedAt = DateTime.UtcNow, RevokedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await using var readCtx = _db.CreateWriteContext();
        var repo = new WebhookApiKeyRepository(readCtx);

        var ativa = await repo.FindActiveByHashAsync("ativa", CancellationToken.None);
        Assert.NotNull(ativa);
        Assert.Equal("checkout", ativa.Project.Name);
        Assert.Null(await repo.FindActiveByHashAsync("revogada", CancellationToken.None));
        Assert.Null(await repo.FindActiveByHashAsync("nao-existe", CancellationToken.None));
    }

    [Fact]
    public async Task ApiGraphQLRevoke_GravaRevokedAt_EWebhookParaDeAceitarNaHora()
    {
        var project = await SeedProjectAsync("p");
        var keyId = Guid.NewGuid();
        await using (var ctx = _db.CreateWriteContext())
        {
            ctx.ApiKeys.Add(new ApiKey { Id = keyId, ProjectId = project.Id, KeyHash = "h", CreatedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.CreateWriteContext())
        {
            await new global::ApiGraphQL.Infrastructure.EfApiKeyRepository(ctx).RevokeAsync(keyId, CancellationToken.None);
        }

        await using var check = _db.CreateWriteContext();
        Assert.NotNull((await check.ApiKeys.SingleAsync(k => k.Id == keyId)).RevokedAt);
        Assert.Null(await new WebhookApiKeyRepository(check).FindActiveByHashAsync("h", CancellationToken.None));
    }

    [Fact]
    public async Task EventLogRepository_GetDue_TrazPendentesETransientesVencidos_EmOrdemDeChegada()
    {
        var now = DateTime.UtcNow;
        var pendenteNovo = await SeedLogAsync(UsageEventLogStatus.Pendente, now.AddMinutes(-1));
        var pendenteAntigo = await SeedLogAsync(UsageEventLogStatus.Pendente, now.AddMinutes(-10));
        var transienteVencido = await SeedLogAsync(UsageEventLogStatus.FalhaTransiente, now.AddMinutes(-5), nextRetryAt: now.AddSeconds(-1));
        await SeedLogAsync(UsageEventLogStatus.FalhaTransiente, now.AddMinutes(-6), nextRetryAt: now.AddMinutes(5));
        await SeedLogAsync(UsageEventLogStatus.Processado, now.AddMinutes(-20));
        await SeedLogAsync(UsageEventLogStatus.FalhaPermanente, now.AddMinutes(-20));

        await using var ctx = _db.CreateWriteContext();
        var due = await new EfUsageEventLogRepository(ctx).GetDueForProcessingAsync(50, CancellationToken.None);

        Assert.Equal(new[] { pendenteAntigo.Id, transienteVencido.Id, pendenteNovo.Id }, due.Select(d => d.Id));
    }

    [Fact]
    public async Task EventLogRepository_GetDue_RespeitaTamanhoDoLote()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedLogAsync(UsageEventLogStatus.Pendente, DateTime.UtcNow.AddSeconds(-i));
        }

        await using var ctx = _db.CreateWriteContext();
        Assert.Equal(3, (await new EfUsageEventLogRepository(ctx).GetDueForProcessingAsync(3, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task EventLogRepository_MarcacoesDeStatus_GravamOsCamposCertos()
    {
        var a = await SeedLogAsync(UsageEventLogStatus.Pendente, DateTime.UtcNow);
        var b = await SeedLogAsync(UsageEventLogStatus.Pendente, DateTime.UtcNow);
        var c = await SeedLogAsync(UsageEventLogStatus.FalhaTransiente, DateTime.UtcNow, DateTime.UtcNow);
        var retry = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var ctx = _db.CreateWriteContext())
        {
            var repo = new EfUsageEventLogRepository(ctx);
            await repo.MarkProcessedAsync(a.Id, CancellationToken.None);
            await repo.MarkTransientFailureAsync(b.Id, 2, "boom", retry, CancellationToken.None);
            await repo.MarkPermanentFailureAsync(c.Id, "fatal", CancellationToken.None);
        }

        await using var check = _db.CreateWriteContext();
        var la = await check.UsageEventLogs.SingleAsync(x => x.Id == a.Id);
        var lb = await check.UsageEventLogs.SingleAsync(x => x.Id == b.Id);
        var lc = await check.UsageEventLogs.SingleAsync(x => x.Id == c.Id);

        Assert.Equal(UsageEventLogStatus.Processado, la.Status);
        Assert.NotNull(la.ProcessedAt);
        Assert.Equal(UsageEventLogStatus.FalhaTransiente, lb.Status);
        Assert.Equal(2, lb.AttemptCount);
        Assert.Equal("boom", lb.LastError);
        Assert.Equal(retry, lb.NextRetryAt);
        Assert.Equal(UsageEventLogStatus.FalhaPermanente, lc.Status);
        Assert.Equal("fatal", lc.LastError);
        Assert.Null(lc.NextRetryAt);
    }

    [Fact]
    public async Task ProcessarEventoDeUso_ComRepositoriosReais_FluxoCompleto_EDbContextCompartilhado()
    {
        await SeedProjectAsync("checkout");
        var ok = await SeedLogAsync(UsageEventLogStatus.Pendente, DateTime.UtcNow);
        var semProjeto = await SeedLogAsync(UsageEventLogStatus.Pendente, DateTime.UtcNow);

        await using (var ctx = _db.CreateWriteContext())
        {
            await ctx.UsageEventLogs.Where(l => l.Id == ok.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.Payload, Payload("checkout")));
            await ctx.UsageEventLogs.Where(l => l.Id == semProjeto.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.Payload, Payload("fantasma")));
        }

        // Um unico DbContext pra todos os repositorios, como no escopo de DI do Consumer.
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

            // Reprocessar o mesmo log (ex: queda antes de marcar) nao duplica.
            await useCase.ExecutarAsync(ok, CancellationToken.None);
        }

        await using var check = _db.CreateWriteContext();
        var record = await check.UsageRecords.SingleAsync();
        Assert.Equal(ok.Id, record.SourceEventLogId);
        Assert.Equal(0.00045m, record.CostUsd);
        Assert.Equal(new DateTime(2026, 9, 16, 10, 30, 0, DateTimeKind.Utc), record.OccurredAt);
        Assert.Equal(UsageEventLogStatus.Processado, (await check.UsageEventLogs.SingleAsync(l => l.Id == ok.Id)).Status);
        Assert.Equal(UsageEventLogStatus.FalhaPermanente, (await check.UsageEventLogs.SingleAsync(l => l.Id == semProjeto.Id)).Status);

        static string Payload(string project) => JsonSerializer.Serialize(new UsageEventMessage
        {
            Project = project,
            Provider = "openai",
            Model = "gpt-4o-mini",
            PromptTokens = 1000,
            CompletionTokens = 500,
            TotalTokens = 1500,
            CostUsd = 0.00045m,
            LatencyMs = 10,
            Status = "success",
            Timestamp = new DateTime(2026, 9, 16, 10, 30, 0, DateTimeKind.Utc),
        });
    }

    [Fact]
    public async Task UsageRecordRepository_FalhaAoGravar_LimpaChangeTracker_EProximoSaveFunciona()
    {
        var log = await SeedLogAsync(UsageEventLogStatus.Pendente, DateTime.UtcNow);

        await using var ctx = _db.CreateWriteContext();
        var records = new EfUsageRecordRepository(ctx);

        // ProjectId inexistente -> violacao de FK
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => records.AddAsync(new UsageRecord
        {
            ProjectId = Guid.NewGuid(), SourceEventLogId = log.Id, Provider = "openai", Model = "m", Status = "success", OccurredAt = DateTime.UtcNow,
        }, CancellationToken.None));

        Assert.Empty(ctx.ChangeTracker.Entries());

        // Mesmo contexto continua utilizavel
        var outro = new UsageEventLog { Id = Guid.NewGuid(), RedisEntryId = "x", Payload = "{}", ReceivedAt = DateTime.UtcNow };
        await new EfUsageEventLogRepository(ctx).AddAsync(outro, CancellationToken.None);
    }

    [Fact]
    public async Task EstatisticasDeUso_AgregaTotaisPorProvedorEPorProjeto_CustoNuloContaComoZero()
    {
        var checkout = await SeedProjectAsync("checkout");
        var search = await SeedProjectAsync("search");
        var t0 = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedRecordAsync(checkout.Id, "openai", 100, 1.25m, t0);
        await SeedRecordAsync(checkout.Id, "openai", 50, null, t0.AddDays(1));
        await SeedRecordAsync(checkout.Id, "anthropic", 10, 0.5m, t0.AddDays(2));
        await SeedRecordAsync(search.Id, "openai", 1, 0.00000001m, t0.AddDays(3));

        await using var ctx = _db.CreateReadContext();
        var repo = new EfUsageStatisticsRepository(ctx);

        var all = await repo.GetAsync(new UsageStatisticsFilter(null, null, null, null), CancellationToken.None);
        Assert.Equal(1.75000001m, all.TotalCostUsd);
        Assert.Equal(161, all.TotalTokens);
        Assert.Equal(4, all.TotalRequests);

        var openai = all.ByProvider.Single(p => p.Provider == "openai");
        Assert.Equal(1.25000001m, openai.CostUsd);
        Assert.Equal(151, openai.Tokens);
        Assert.Equal(3, openai.Requests);
        Assert.Equal(new UsageByProvider("anthropic", 0.5m, 10, 1), all.ByProvider.Single(p => p.Provider == "anthropic"));

        Assert.Equal(new UsageByProject("checkout", 1.75m, 160, 3), all.ByProject.Single(p => p.Project == "checkout"));
        Assert.Equal(new UsageByProject("search", 0.00000001m, 1, 1), all.ByProject.Single(p => p.Project == "search"));

        var byProject = await repo.GetAsync(new UsageStatisticsFilter("checkout", null, null, null), CancellationToken.None);
        Assert.Equal(3, byProject.TotalRequests);
        Assert.Single(byProject.ByProject);

        var byProvider = await repo.GetAsync(new UsageStatisticsFilter(null, "anthropic", null, null), CancellationToken.None);
        Assert.Equal(1, byProvider.TotalRequests);

        var combinado = await repo.GetAsync(new UsageStatisticsFilter("checkout", "openai", null, null), CancellationToken.None);
        Assert.Equal(2, combinado.TotalRequests);
        Assert.Equal(1.25m, combinado.TotalCostUsd);

        var periodo = await repo.GetAsync(new UsageStatisticsFilter(null, null, t0.AddDays(1), t0.AddDays(2)), CancellationToken.None);
        Assert.Equal(2, periodo.TotalRequests); // limites inclusivos

        var vazio = await repo.GetAsync(new UsageStatisticsFilter("nao-existe", null, null, null), CancellationToken.None);
        Assert.Equal(0, vazio.TotalRequests);
        Assert.Equal(0m, vazio.TotalCostUsd);
        Assert.Empty(vazio.ByProject);
        Assert.Empty(vazio.ByProvider);
    }

    [Fact]
    public async Task DiretorioDeProjetos_ListaOrdenadoPorNome_ComMetadadosDasChaves_SemHash()
    {
        var b = await SeedProjectAsync("b-service");
        var a = await SeedProjectAsync("a-service");
        await using (var ctx = _db.CreateWriteContext())
        {
            ctx.ApiKeys.Add(new ApiKey { Id = Guid.NewGuid(), ProjectId = a.Id, KeyHash = ApiKeyHasher.Hash("k1", "p"), CreatedAt = DateTime.UtcNow });
            ctx.ApiKeys.Add(new ApiKey { Id = Guid.NewGuid(), ProjectId = a.Id, KeyHash = ApiKeyHasher.Hash("k2", "p"), CreatedAt = DateTime.UtcNow, RevokedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.CreateReadContext();
        var list = await new EfProjectDirectoryQuery(read).GetAllAsync(CancellationToken.None);

        Assert.Equal(new[] { "a-service", "b-service" }, list.Select(p => p.Name));
        Assert.Equal(2, list[0].ApiKeys.Count);
        Assert.Single(list[0].ApiKeys, k => k.RevokedAt is not null);
        Assert.Empty(list[1].ApiKeys);
    }

    [Fact]
    public async Task Replicacao_DadoGravadoNoPrimaryApareceNaReplica()
    {
        var project = await SeedProjectAsync($"replicado-{Guid.NewGuid():N}");

        await using var replica = _db.CreateReadContext(DatabaseFixture.ReplicaConnectionString);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await replica.Projects.AnyAsync(p => p.Id == project.Id))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("Projeto nao apareceu na replica em 10s.");
    }
}
