using Shared.Entities;

namespace Consumer.Domain.Abstractions;

public interface IUsageRecordRepository
{
    Task<bool> ExistsForEventLogAsync(Guid sourceEventLogId, CancellationToken cancellationToken);

    Task<UsageRecord> AddAsync(UsageRecord record, CancellationToken cancellationToken);
}
