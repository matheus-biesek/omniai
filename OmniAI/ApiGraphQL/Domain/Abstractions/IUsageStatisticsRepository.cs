using ApiGraphQL.Domain;

namespace ApiGraphQL.Domain.Abstractions;

public interface IUsageStatisticsRepository
{
    Task<UsageStatisticsResult> GetAsync(UsageStatisticsFilter filter, CancellationToken cancellationToken);
}
