using ApiGraphQL.Domain.Abstractions;
using Shared.Data;

namespace ApiGraphQL.Infrastructure;

public class EfUnitOfWork : IUnitOfWork
{
    private readonly WriteDbContext _dbContext;

    public EfUnitOfWork(WriteDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
