using Shared.Entities;

namespace Webhook.Domain.Abstractions;

public interface IApiKeyRepository
{
    Task<ApiKey?> FindActiveByHashAsync(string keyHash, CancellationToken cancellationToken);
}
