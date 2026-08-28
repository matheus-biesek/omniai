namespace ApiGraphQL.Types;

public sealed record CreateProjectPayload(Guid ProjectId, string ProjectName, string ApiKey);
