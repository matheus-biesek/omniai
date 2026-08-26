using Consumer.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Entities;

namespace Consumer.Infrastructure;

public class EfUsageRecordRepository : IUsageRecordRepository
{
    private readonly WriteDbContext _dbContext;

    public EfUsageRecordRepository(WriteDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsForEventLogAsync(Guid sourceEventLogId, CancellationToken cancellationToken)
    {
        return await _dbContext.UsageRecords.AnyAsync(r => r.SourceEventLogId == sourceEventLogId, cancellationToken);
    }

    public async Task<UsageRecord> AddAsync(UsageRecord record, CancellationToken cancellationToken)
    {
        _dbContext.UsageRecords.Add(record);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // O contexto e compartilhado com outros repositorios no mesmo escopo (ex: o log do
            // evento). Sem isso, a entidade que falhou continua rastreada e envenena o proximo
            // SaveChanges de quem reutilizar este DbContext.
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        return record;
    }
}
