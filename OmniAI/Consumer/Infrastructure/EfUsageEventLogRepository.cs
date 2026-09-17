using Consumer.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Entities;

namespace Consumer.Infrastructure;

public class EfUsageEventLogRepository : IUsageEventLogRepository
{
    private readonly WriteDbContext _dbContext;

    public EfUsageEventLogRepository(WriteDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> TryAddAsync(UsageEventLog log, CancellationToken cancellationToken)
    {
        // INSERT ... ON CONFLICT direto, em vez de Add + SaveChanges: a checagem de duplicidade e
        // atomica no banco (indice unico em RedisEntryId) e uma entrada repetida nao deixa entidade
        // rastreada no change tracker - o que envenenaria o resto do lote, que compartilha este
        // DbContext no worker.
        var inserted = await _dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO usage_event_logs ("Id", "RedisEntryId", "Payload", "Status", "AttemptCount", "ReceivedAt")
            VALUES ({log.Id}, {log.RedisEntryId}, {log.Payload}, {(int)log.Status}, {log.AttemptCount}, {log.ReceivedAt})
            ON CONFLICT ("RedisEntryId") DO NOTHING
            """,
            cancellationToken);

        return inserted == 1;
    }

    public async Task<IReadOnlyList<UsageEventLog>> GetDueForProcessingAsync(int maxBatchSize, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        return await _dbContext.UsageEventLogs
            .Where(e => e.Status == UsageEventLogStatus.Pendente
                || (e.Status == UsageEventLogStatus.FalhaTransiente && e.NextRetryAt <= now))
            .OrderBy(e => e.ReceivedAt)
            .Take(maxBatchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken)
    {
        // ExecuteUpdateAsync grava direto no banco, sem passar pelo change tracker do EF Core -
        // este DbContext e compartilhado com outros repositorios no mesmo escopo (ex: UsageRecord),
        // e uma entidade rastreada aqui ficaria sujeita a interferencia de falhas alheias.
        await _dbContext.UsageEventLogs
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(e => e.Status, UsageEventLogStatus.Processado)
                    .SetProperty(e => e.ProcessedAt, DateTime.UtcNow),
                cancellationToken);
    }

    public async Task MarkTransientFailureAsync(Guid id, int attemptCount, string error, DateTime nextRetryAt, CancellationToken cancellationToken)
    {
        await _dbContext.UsageEventLogs
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(e => e.Status, UsageEventLogStatus.FalhaTransiente)
                    .SetProperty(e => e.AttemptCount, attemptCount)
                    .SetProperty(e => e.LastError, error)
                    .SetProperty(e => e.NextRetryAt, nextRetryAt),
                cancellationToken);
    }

    public async Task MarkPermanentFailureAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        await _dbContext.UsageEventLogs
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(e => e.Status, UsageEventLogStatus.FalhaPermanente)
                    .SetProperty(e => e.LastError, error)
                    .SetProperty(e => e.NextRetryAt, (DateTime?)null),
                cancellationToken);
    }
}
