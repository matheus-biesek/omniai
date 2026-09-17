using Shared.Entities;

namespace Consumer.Domain.Abstractions;

public interface IUsageEventLogRepository
{
    /// <summary>Grava o log; retorna false (sem gravar) se ja existe log para a mesma entrada do Redis.</summary>
    Task<bool> TryAddAsync(UsageEventLog log, CancellationToken cancellationToken);

    Task<IReadOnlyList<UsageEventLog>> GetDueForProcessingAsync(int maxBatchSize, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken);

    Task MarkTransientFailureAsync(Guid id, int attemptCount, string error, DateTime nextRetryAt, CancellationToken cancellationToken);

    Task MarkPermanentFailureAsync(Guid id, string error, CancellationToken cancellationToken);
}
