using Shared.Entities;

namespace ApiGraphQL.Domain.Abstractions;

public interface IApiKeyRepository
{
    void Add(ApiKey apiKey);

    Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task RevokeAsync(Guid id, CancellationToken cancellationToken);
}
