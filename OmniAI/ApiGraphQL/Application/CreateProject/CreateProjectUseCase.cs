using ApiGraphQL.Domain.Abstractions;
using ApiGraphQL.Domain.Exceptions;
using Microsoft.Extensions.Options;
using Shared.Entities;
using Shared.Security;

namespace ApiGraphQL.Application.CreateProject;

public class CreateProjectUseCase
{
    private readonly IProjectRepository _projectRepository;
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ApiKeyHashingOptions _hashingOptions;

    public CreateProjectUseCase(
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

    public async Task<CreateProjectResult> ExecutarAsync(string name, CancellationToken cancellationToken)
    {
        if (await _projectRepository.ExistsByNameAsync(name, cancellationToken))
        {
            throw new ProjectNameAlreadyExistsException(name);
        }

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTime.UtcNow,
        };

        var rawApiKey = ApiKeyGenerator.Generate();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            KeyHash = ApiKeyHasher.Hash(rawApiKey, _hashingOptions.PepperSecret),
            CreatedAt = DateTime.UtcNow,
        };

        _projectRepository.Add(project);
        _apiKeyRepository.Add(apiKey);

        // Project e a primeira ApiKey sao gravados numa unica transacao - um projeto sem chave
        // nenhuma nao serve pra nada, entao os dois precisam ser criados juntos ou nenhum dos dois.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateProjectResult(project.Id, project.Name, rawApiKey);
    }
}
