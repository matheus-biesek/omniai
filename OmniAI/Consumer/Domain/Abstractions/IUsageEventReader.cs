namespace Consumer.Domain.Abstractions;

public sealed record PendingRedisEntry(string EntryId, string Payload);

public interface IUsageEventReader
{
    Task EnsureConsumerGroupAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingRedisEntry>> ReadOwnPendingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingRedisEntry>> ReadNewAsync(CancellationToken cancellationToken);

    /// <summary>Confirma a entrada no consumer group e a remove da stream.</summary>
    Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken);

    /// <summary>Remove da stream entradas ja entregues e confirmadas que ficaram para tras.</summary>
    Task RemoveAcknowledgedAsync(CancellationToken cancellationToken);
}
