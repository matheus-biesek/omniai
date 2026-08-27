using System.Security.Claims;
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
        if (claimsPrincipal.Identity is not { IsAuthenticated: true })
        {
            throw new UnauthorizedAccessException("Autenticação necessária.");
        }

        var domainFilter = new UsageStatisticsFilter(
            filter?.Project,
            filter?.Provider,
            filter?.From,
            filter?.To);

        return await useCase.ExecutarAsync(domainFilter, cancellationToken);
    }
}
