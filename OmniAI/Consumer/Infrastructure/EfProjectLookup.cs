using Consumer.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Data;

namespace Consumer.Infrastructure;

public class EfProjectLookup : IProjectLookup
{
    private readonly WriteDbContext _dbContext;

    public EfProjectLookup(WriteDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Guid?> FindProjectIdByNameAsync(string projectName, CancellationToken cancellationToken)
    {
        return await _dbContext.Projects
            .Where(p => p.Name == projectName)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
