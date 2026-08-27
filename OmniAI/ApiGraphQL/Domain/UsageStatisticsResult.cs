namespace ApiGraphQL.Domain;

public sealed record UsageByProvider(string Provider, decimal CostUsd, long Tokens, long Requests);

public sealed record UsageByProject(string Project, decimal CostUsd, long Tokens, long Requests);

public sealed record UsageStatisticsResult(
    decimal TotalCostUsd,
    long TotalTokens,
    long TotalRequests,
    IReadOnlyList<UsageByProvider> ByProvider,
    IReadOnlyList<UsageByProject> ByProject);
