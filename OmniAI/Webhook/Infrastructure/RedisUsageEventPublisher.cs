using System.Text.Json;
using Microsoft.Extensions.Options;
using Shared.Messaging;
using StackExchange.Redis;
using Webhook.Domain.Abstractions;

namespace Webhook.Infrastructure;

public class RedisUsageEventPublisher : IUsageEventPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly QueueBackpressureOptions _options;

    public RedisUsageEventPublisher(IConnectionMultiplexer redis, IOptions<QueueBackpressureOptions> options)
    {
        _redis = redis;
        _options = options.Value;
    }

    public async Task<PublishOutcome> PublishAsync(UsageEventMessage message, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        RedisKey streamKey = _options.StreamKey;

        var currentLength = await db.StreamLengthAsync(streamKey);
        if (currentLength >= _options.MaxQueueSize)
        {
            return PublishOutcome.QueueFull;
        }

        var payload = JsonSerializer.Serialize(message);
        await db.StreamAddAsync(streamKey, "payload", payload);

        return PublishOutcome.Published;
    }
}
