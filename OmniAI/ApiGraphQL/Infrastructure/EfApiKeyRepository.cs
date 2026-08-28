using ApiGraphQL.Domain.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Entities;

namespace ApiGraphQL.Infrastructure;

public class EfApiKeyRepository : IApiKeyRepository
{
    private readonly WriteDbContext _dbContext;

    public EfApiKeyRepository(WriteDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(ApiKey apiKey)
    {
        _dbContext.ApiKeys.Add(apiKey);
    }

    public Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
    }

    public Task RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        // ExecuteUpdateAsync grava direto no banco, sem passar pelo change tracker - evita a
        // armadilha de contexto compartilhado (ver 05-modulo-consumer.md) mesmo aqui, onde nao
        // ha risco imediato, para manter o mesmo padrao em todo lugar que atualiza status.
        return _dbContext.ApiKeys
            .Where(k => k.Id == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(k => k.RevokedAt, DateTime.UtcNow),
                cancellationToken);
    }
}
