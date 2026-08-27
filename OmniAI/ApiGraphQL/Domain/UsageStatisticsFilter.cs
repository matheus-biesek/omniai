namespace ApiGraphQL.Domain;

public sealed record UsageStatisticsFilter(
    string? Project,
    string? Provider,
    DateTime? From,
    DateTime? To);
