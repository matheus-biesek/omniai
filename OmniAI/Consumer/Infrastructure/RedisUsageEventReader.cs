using Consumer.Domain.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Consumer.Infrastructure;

public class RedisUsageEventReader : IUsageEventReader
{
    private readonly IConnectionMultiplexer _redis;
    private readonly QueueOptions _options;

    public RedisUsageEventReader(IConnectionMultiplexer redis, IOptions<QueueOptions> options)
    {
        _redis = redis;
        _options = options.Value;
    }

    public async Task EnsureConsumerGroupAsync(CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        try
        {
            await db.StreamCreateConsumerGroupAsync(_options.StreamKey, _options.ConsumerGroup, StreamPosition.NewMessages, createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.StartsWith("BUSYGROUP"))
        {
            // Grupo ja existe - normal em reinicios do processo.
        }
    }

    public Task<IReadOnlyList<PendingRedisEntry>> ReadOwnPendingAsync(CancellationToken cancellationToken)
    {
        return ReadAsync("0", cancellationToken);
    }

    public Task<IReadOnlyList<PendingRedisEntry>> ReadNewAsync(CancellationToken cancellationToken)
    {
        return ReadAsync(StreamPosition.NewMessages, cancellationToken);
    }

    public async Task AcknowledgeAsync(string entryId, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        await db.StreamAcknowledgeAsync(_options.StreamKey, _options.ConsumerGroup, entryId);
    }

    private async Task<IReadOnlyList<PendingRedisEntry>> ReadAsync(RedisValue position, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var entries = await db.StreamReadGroupAsync(
            _options.StreamKey,
            _options.ConsumerGroup,
            _options.ConsumerName,
            position,
            count: _options.BatchSize);

        return entries
            .Select(e => new PendingRedisEntry(e.Id.ToString(), ExtractPayload(e)))
            .ToList();
    }

    private static string ExtractPayload(StreamEntry entry)
    {
        foreach (var field in entry.Values)
        {
            if (field.Name == "payload")
            {
                return field.Value.ToString();
            }
        }

        return string.Empty;
    }
}
