using Shared.Entities;

namespace Consumer.Domain.Abstractions;

public interface IUsageNotifier
{
    Task NotifyAsync(UsageRecord record, CancellationToken cancellationToken);
}
