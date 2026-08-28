using System.Security.Claims;
using ApiGraphQL.Application.Login;
using CreateApiKeyUseCase = ApiGraphQL.Application.CreateApiKey.CreateApiKeyUseCase;
using CreateProjectUseCase = ApiGraphQL.Application.CreateProject.CreateProjectUseCase;
using RevokeApiKeyUseCase = ApiGraphQL.Application.RevokeApiKey.RevokeApiKeyUseCase;

namespace ApiGraphQL.Types;

public class Mutation
{
    public LoginPayload Login(string username, string password, LoginUseCase useCase)
    {
        var token = useCase.Executar(username, password);
        return new LoginPayload(token);
    }

    public async Task<CreateProjectPayload> CreateProject(
        string name,
        CreateProjectUseCase useCase,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        claimsPrincipal.RequireAuthenticated();

        var result = await useCase.ExecutarAsync(name, cancellationToken);
        return new CreateProjectPayload(result.ProjectId, result.ProjectName, result.ApiKey);
    }

    public async Task<CreateApiKeyPayload> CreateApiKey(
        Guid projectId,
        CreateApiKeyUseCase useCase,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        claimsPrincipal.RequireAuthenticated();

        var result = await useCase.ExecutarAsync(projectId, cancellationToken);
        return new CreateApiKeyPayload(result.ApiKeyId, result.ApiKey);
    }

    public async Task<bool> RevokeApiKey(
        Guid apiKeyId,
        RevokeApiKeyUseCase useCase,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        claimsPrincipal.RequireAuthenticated();

        await useCase.ExecutarAsync(apiKeyId, cancellationToken);
        return true;
    }
}
