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
            // Beginning, nao NewMessages: no primeiro boot o Webhook pode aceitar eventos antes do
            // Consumer criar o grupo, e com "$" esses eventos nunca seriam lidos.
            await db.StreamCreateConsumerGroupAsync(_options.StreamKey, _options.ConsumerGroup, StreamPosition.Beginning, createStream: true);
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
        // So e chamado depois que o evento ja esta em usage_event_logs - a partir dai a entrada nao
        // serve mais pra nada no Redis. Remove-la (XDEL) e o que faz o XLEN usado no backpressure do
        // Webhook medir so o que falta processar; sem isso a stream so cresce e, ao atingir
        // MaxQueueSize, o Webhook recusaria tudo para sempre. XACK e XDEL vao juntos (MULTI/EXEC).
        var transaction = _redis.GetDatabase().CreateTransaction();
        var ack = transaction.StreamAcknowledgeAsync(_options.StreamKey, _options.ConsumerGroup, entryId);
        var delete = transaction.StreamDeleteAsync(_options.StreamKey, [entryId]);
        await transaction.ExecuteAsync();
        await Task.WhenAll(ack, delete);
    }

    public async Task RemoveAcknowledgedAsync(CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();

        var lastDeliveredId = (await db.StreamGroupInfoAsync(_options.StreamKey))
            .Where(g => g.Name == _options.ConsumerGroup)
            .Select(g => g.LastDeliveredId)
            .FirstOrDefault();
        if (!TryParseEntryId(lastDeliveredId, out var lastMs, out var lastSeq))
        {
            return;
        }

        // Tudo abaixo do menor pendente (ou, sem pendentes, ate o ultimo entregue) ja foi entregue e
        // confirmado. O que foi entregue depois disso ou ainda nao foi lido fica intacto.
        var pending = await db.StreamPendingAsync(_options.StreamKey, _options.ConsumerGroup);
        RedisValue minId = pending.PendingMessageCount > 0
            ? pending.LowestPendingMessageId
            : $"{lastMs}-{lastSeq + 1}";

        await db.StreamTrimByMinIdAsync(_options.StreamKey, minId);
    }

    private static bool TryParseEntryId(string? entryId, out long milliseconds, out long sequence)
    {
        milliseconds = 0;
        sequence = 0;
        var parts = entryId?.Split('-');
        return parts is { Length: 2 }
            && long.TryParse(parts[0], out milliseconds)
            && long.TryParse(parts[1], out sequence);
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
