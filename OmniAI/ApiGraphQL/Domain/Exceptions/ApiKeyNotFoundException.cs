namespace ApiGraphQL.Domain.Exceptions;

public class ApiKeyNotFoundException : Exception
{
    public ApiKeyNotFoundException(Guid apiKeyId)
        : base($"API Key '{apiKeyId}' não encontrada.")
    {
    }
}
