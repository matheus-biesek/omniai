using ApiGraphQL.Domain;
using ApiGraphQL.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Data;

namespace ApiGraphQL.Infrastructure;

public class EfProjectDirectoryQuery : IProjectDirectoryQuery
{
    private readonly ReadDbContext _dbContext;

    public EfProjectDirectoryQuery(ReadDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<ProjectSummary>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectSummary(
                p.Id,
                p.Name,
                p.CreatedAt,
                p.ApiKeys
                    .Select(k => new ApiKeySummary(k.Id, k.CreatedAt, k.RevokedAt))
                    .ToList()))
            .ToListAsync(cancellationToken);
    }
}
