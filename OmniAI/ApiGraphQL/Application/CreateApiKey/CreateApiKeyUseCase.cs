using ApiGraphQL.Domain.Abstractions;
using ApiGraphQL.Domain.Exceptions;
using Microsoft.Extensions.Options;
using Shared.Entities;
using Shared.Security;

namespace ApiGraphQL.Application.CreateApiKey;

public class CreateApiKeyUseCase
{
    private readonly IProjectRepository _projectRepository;
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ApiKeyHashingOptions _hashingOptions;

    public CreateApiKeyUseCase(
        IProjectRepository projectRepository,
        IApiKeyRepository apiKeyRepository,
        IUnitOfWork unitOfWork,
        IOptions<ApiKeyHashingOptions> hashingOptions)
    {
        _projectRepository = projectRepository;
        _apiKeyRepository = apiKeyRepository;
        _unitOfWork = unitOfWork;
        _hashingOptions = hashingOptions.Value;
    }

    public async Task<CreateApiKeyResult> ExecutarAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new ProjectNotFoundException(projectId);

        var rawApiKey = ApiKeyGenerator.Generate();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            KeyHash = ApiKeyHasher.Hash(rawApiKey, _hashingOptions.PepperSecret),
            CreatedAt = DateTime.UtcNow,
        };

        _apiKeyRepository.Add(apiKey);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateApiKeyResult(apiKey.Id, rawApiKey);
    }
}
