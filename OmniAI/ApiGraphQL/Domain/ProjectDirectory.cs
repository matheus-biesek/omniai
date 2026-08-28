namespace ApiGraphQL.Domain;

public sealed record ApiKeySummary(Guid Id, DateTime CreatedAt, DateTime? RevokedAt);

public sealed record ProjectSummary(Guid Id, string Name, DateTime CreatedAt, IReadOnlyList<ApiKeySummary> ApiKeys);
