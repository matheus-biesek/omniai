using Shared.Entities;

namespace Consumer.Domain.Abstractions;

public interface IUsageEventLogRepository
{
    Task AddAsync(UsageEventLog log, CancellationToken cancellationToken);

    Task<IReadOnlyList<UsageEventLog>> GetDueForProcessingAsync(int maxBatchSize, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken);

    Task MarkTransientFailureAsync(Guid id, int attemptCount, string error, DateTime nextRetryAt, CancellationToken cancellationToken);

    Task MarkPermanentFailureAsync(Guid id, string error, CancellationToken cancellationToken);
}
