using ApiGraphQL.Domain;
using ApiGraphQL.Domain.Abstractions;

namespace ApiGraphQL.Application.UsageStatistics;

public class GetUsageStatisticsUseCase
{
    private readonly IUsageStatisticsRepository _repository;

    public GetUsageStatisticsUseCase(IUsageStatisticsRepository repository)
    {
        _repository = repository;
    }

    public Task<UsageStatisticsResult> ExecutarAsync(UsageStatisticsFilter filter, CancellationToken cancellationToken)
    {
        return _repository.GetAsync(filter, cancellationToken);
    }
}
