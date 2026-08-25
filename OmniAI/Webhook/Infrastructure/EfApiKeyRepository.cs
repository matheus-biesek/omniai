using Microsoft.EntityFrameworkCore;
using Shared.Data;
using Shared.Entities;
using Webhook.Domain.Abstractions;

namespace Webhook.Infrastructure;

public class EfApiKeyRepository : IApiKeyRepository
{
    private readonly WriteDbContext _dbContext;

    public EfApiKeyRepository(WriteDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiKey?> FindActiveByHashAsync(string keyHash, CancellationToken cancellationToken)
    {
        return await _dbContext.ApiKeys
            .Include(k => k.Project)
            .Where(k => k.KeyHash == keyHash && k.RevokedAt == null)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
