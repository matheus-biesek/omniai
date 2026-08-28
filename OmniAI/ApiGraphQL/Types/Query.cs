using System.Security.Claims;
using ApiGraphQL.Application.ListProjects;
using ApiGraphQL.Application.UsageStatistics;
using ApiGraphQL.Domain;
using ApiGraphQL.Types.Inputs;

namespace ApiGraphQL.Types;

public class Query
{
    public string Ping() => "pong";

    public async Task<UsageStatisticsResult> UsageStatistics(
        UsageStatisticsFilterInput? filter,
        GetUsageStatisticsUseCase useCase,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        claimsPrincipal.RequireAuthenticated();

        var domainFilter = new UsageStatisticsFilter(
            filter?.Project,
            filter?.Provider,
            filter?.From,
            filter?.To);

        return await useCase.ExecutarAsync(domainFilter, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectSummary>> Projects(
        ListProjectsUseCase useCase,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken cancellationToken)
    {
        claimsPrincipal.RequireAuthenticated();

        return await useCase.ExecutarAsync(cancellationToken);
    }
}
