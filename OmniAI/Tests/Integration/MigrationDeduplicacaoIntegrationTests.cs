using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Data;
using Shared.Entities;

namespace Tests.Integration;

// Banco proprio: este teste precisa parar o schema numa migration antiga, semear o estado que o bug
// de duplicidade deixava e so entao aplicar a migration do indice unico.
[Trait("Category", "Integration")]
public class MigrationDeduplicacaoIntegrationTests
{
    private const string MigrationAnterior = "20260826030436_AddUsageEventLog";

    private static readonly string ConnectionString =
        DatabaseFixture.WriteConnectionString.Replace("Database=omniai_tests", "Database=omniai_tests_migration");

    private static WriteDbContext Context() =>
        new(new DbContextOptionsBuilder<WriteDbContext>().UseNpgsql(ConnectionString).Options);

    [Fact]
    public async Task MigrationDoIndiceUnico_LimpaDuplicatasExistentes_SemPerderEventosUnicos()
    {
        await using (var ctx = Context())
        {
            await ctx.Database.EnsureDeletedAsync();
            await ctx.GetService<IMigrator>().MigrateAsync(MigrationAnterior);
        }

        var project = new Project { Id = Guid.NewGuid(), Name = "checkout", CreatedAt = DateTime.UtcNow };
        var t0 = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        UsageEventLog Log(string entry, int minuto) => new()
        {
            Id = Guid.NewGuid(), RedisEntryId = entry, Payload = "{}", Status = UsageEventLogStatus.Processado, ReceivedAt = t0.AddMinutes(minuto),
        };
        UsageRecord Record(UsageEventLog log) => new()
        {
            ProjectId = project.Id, SourceEventLogId = log.Id, Provider = "openai", Model = "m", TotalTokens = 10, CostUsd = 2m, Status = "success", OccurredAt = t0,
        };

        // Entrada "A": duplicada, as duas copias viraram UsageRecord (custo em dobro) -> fica a mais antiga.
        var a1 = Log("A", 0);
        var a2 = Log("A", 1);
        // Entrada "B": duplicada, so a copia mais nova chegou a virar UsageRecord -> fica a que tem record.
        var b1 = Log("B", 2);
        b1.Status = UsageEventLogStatus.FalhaPermanente;
        var b2 = Log("B", 3);
        // Entrada "C": sem duplicata.
        var c = Log("C", 4);

        await using (var ctx = Context())
        {
            ctx.Projects.Add(project);
            ctx.UsageEventLogs.AddRange(a1, a2, b1, b2, c);
            ctx.UsageRecords.AddRange(Record(a1), Record(a2), Record(b2), Record(c));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = Context())
        {
            await ctx.Database.MigrateAsync();
        }

        await using (var check = Context())
        {
            var logs = await check.UsageEventLogs.OrderBy(l => l.ReceivedAt).Select(l => l.Id).ToListAsync();
            Assert.Equal(new[] { a1.Id, b2.Id, c.Id }, logs);

            var records = await check.UsageRecords.Select(r => r.SourceEventLogId).ToListAsync();
            Assert.Equal(new HashSet<Guid> { a1.Id, b2.Id, c.Id }, records.ToHashSet());
            Assert.Equal(6m, await check.UsageRecords.SumAsync(r => r.CostUsd));

            check.UsageEventLogs.Add(Log("C", 9));
            await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
        }

        await using (var cleanup = Context())
        {
            await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
