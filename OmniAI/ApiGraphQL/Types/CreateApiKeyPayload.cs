namespace ApiGraphQL.Types;

public sealed record CreateApiKeyPayload(Guid ApiKeyId, string ApiKey);
