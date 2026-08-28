using Shared.Entities;

namespace Consumer.Domain.Abstractions;

public interface IUsageNotifier
{
    Task NotifyAsync(UsageRecord record, string projectName, CancellationToken cancellationToken);
}
