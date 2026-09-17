using System.Security.Claims;
using ApiGraphQL.Application.Login;
using CreateApiKeyUseCase = ApiGraphQL.Application.CreateApiKey.CreateApiKeyUseCase;
using CreateProjectUseCase = ApiGraphQL.Application.CreateProject.CreateProjectUseCase;
using RevokeApiKeyUseCase = ApiGraphQL.Application.RevokeApiKey.RevokeApiKeyUseCase;

namespace ApiGraphQL.Types;

public class Mutation
{
    public LoginPayload Login(string username, string password, LoginUseCase useCase, IHttpContextAccessor httpContextAccessor)
    {
        // Atras de proxy reverso todos os clientes chegam com o IP do proxy - mesma limitacao do rate
        // limit por IP do Webhook (ver 09-seguranca.md).
        var clientKey = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var token = useCase.Executar(username, password, clientKey);
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
