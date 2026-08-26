namespace Consumer.Domain.Abstractions;

public sealed record PendingRedisEntry(string EntryId, string Payload);

public interface IUsageEventReader
{
    Task EnsureConsumerGroupAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingRedisEntry>> ReadOwnPendingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingRedisEntry>> ReadNewAsync(CancellationToken cancellationToken);

    Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken);
}
