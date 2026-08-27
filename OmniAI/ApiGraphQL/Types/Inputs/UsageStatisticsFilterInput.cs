namespace ApiGraphQL.Types.Inputs;

public sealed record UsageStatisticsFilterInput(
    string? Project,
    string? Provider,
    DateTime? From,
    DateTime? To);
