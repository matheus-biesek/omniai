using ApiGraphQL.Domain.Abstractions;
using ApiGraphQL.Domain.Exceptions;

namespace ApiGraphQL.Application.RevokeApiKey;

public class RevokeApiKeyUseCase
{
    private readonly IApiKeyRepository _apiKeyRepository;

    public RevokeApiKeyUseCase(IApiKeyRepository apiKeyRepository)
    {
        _apiKeyRepository = apiKeyRepository;
    }

    public async Task ExecutarAsync(Guid apiKeyId, CancellationToken cancellationToken)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(apiKeyId, cancellationToken)
            ?? throw new ApiKeyNotFoundException(apiKeyId);

        if (apiKey.RevokedAt is not null)
        {
            return;
        }

        await _apiKeyRepository.RevokeAsync(apiKeyId, cancellationToken);
    }
}
