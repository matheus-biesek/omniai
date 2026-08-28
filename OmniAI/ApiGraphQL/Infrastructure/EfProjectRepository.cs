using ApiGraphQL.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Entities;

namespace ApiGraphQL.Infrastructure;

public class EfProjectRepository : IProjectRepository
{
    private readonly WriteDbContext _dbContext;

    public EfProjectRepository(WriteDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsByNameAsync(string name, CancellationToken cancellationToken)
    {
        return _dbContext.Projects.AnyAsync(p => p.Name == name, cancellationToken);
    }

    public void Add(Project project)
    {
        _dbContext.Projects.Add(project);
    }

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }
}
