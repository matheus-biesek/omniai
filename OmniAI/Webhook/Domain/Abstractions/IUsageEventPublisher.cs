using Shared.Messaging;

namespace Webhook.Domain.Abstractions;

public enum PublishOutcome
{
    Published,
    QueueFull,
}

public interface IUsageEventPublisher
{
    Task<PublishOutcome> PublishAsync(UsageEventMessage message, CancellationToken cancellationToken);
}
