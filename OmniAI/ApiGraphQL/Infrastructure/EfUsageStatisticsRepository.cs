using ApiGraphQL.Domain;
using ApiGraphQL.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Entities;

namespace ApiGraphQL.Infrastructure;

public class EfUsageStatisticsRepository : IUsageStatisticsRepository
{
    private readonly ReadDbContext _dbContext;

    public EfUsageStatisticsRepository(ReadDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UsageStatisticsResult> GetAsync(UsageStatisticsFilter filter, CancellationToken cancellationToken)
    {
        var query = ApplyFilter(_dbContext.UsageRecords.AsNoTracking(), filter);

        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                CostUsd = g.Sum(r => r.CostUsd ?? 0),
                Tokens = g.Sum(r => (long)r.TotalTokens),
                Requests = g.LongCount(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var byProvider = await query
            .GroupBy(r => r.Provider)
            .Select(g => new UsageByProvider(g.Key, g.Sum(r => r.CostUsd ?? 0), g.Sum(r => (long)r.TotalTokens), g.LongCount()))
            .ToListAsync(cancellationToken);

        var byProject = await query
            .GroupBy(r => r.Project.Name)
            .Select(g => new UsageByProject(g.Key, g.Sum(r => r.CostUsd ?? 0), g.Sum(r => (long)r.TotalTokens), g.LongCount()))
            .ToListAsync(cancellationToken);

        return new UsageStatisticsResult(
            totals?.CostUsd ?? 0,
            totals?.Tokens ?? 0,
            totals?.Requests ?? 0,
            byProvider,
            byProject);
    }

    private static IQueryable<UsageRecord> ApplyFilter(IQueryable<UsageRecord> query, UsageStatisticsFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Project))
        {
            query = query.Where(r => r.Project.Name == filter.Project);
        }

        if (!string.IsNullOrWhiteSpace(filter.Provider))
        {
            query = query.Where(r => r.Provider == filter.Provider);
        }

        if (filter.From.HasValue)
        {
            query = query.Where(r => r.OccurredAt >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            query = query.Where(r => r.OccurredAt <= filter.To.Value);
        }

        return query;
    }
}
