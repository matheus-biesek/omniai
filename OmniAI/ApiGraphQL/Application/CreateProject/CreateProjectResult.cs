namespace ApiGraphQL.Application.CreateProject;

public sealed record CreateProjectResult(Guid ProjectId, string ProjectName, string ApiKey);
