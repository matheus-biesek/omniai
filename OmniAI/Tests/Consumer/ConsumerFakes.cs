using Consumer.Domain.Abstractions;
using Shared.Entities;

namespace Tests.Consumer;

internal sealed class FakeProjectLookup : IProjectLookup
{
    public Dictionary<string, Guid> Projects { get; } = new();

    public Task<Guid?> FindProjectIdByNameAsync(string projectName, CancellationToken cancellationToken)
        => Task.FromResult(Projects.TryGetValue(projectName, out var id) ? id : (Guid?)null);
}

internal sealed class FakeUsageRecordRepository : IUsageRecordRepository
{
    public List<UsageRecord> Records { get; } = new();
    public Exception? ThrowOnAdd { get; set; }

    public Task<bool> ExistsForEventLogAsync(Guid sourceEventLogId, CancellationToken cancellationToken)
        => Task.FromResult(Records.Any(r => r.SourceEventLogId == sourceEventLogId));

    public Task<UsageRecord> AddAsync(UsageRecord record, CancellationToken cancellationToken)
    {
        if (ThrowOnAdd is not null)
        {
            throw ThrowOnAdd;
        }

        record.Id = Records.Count + 1;
        Records.Add(record);
        return Task.FromResult(record);
    }
}

internal sealed class FakeUsageEventLogRepository : IUsageEventLogRepository
{
    public List<UsageEventLog> Added { get; } = new();
    public List<Guid> Processed { get; } = new();
    public List<(Guid Id, int Attempt, string Error, DateTime NextRetryAt)> Transient { get; } = new();
    public List<(Guid Id, string Error)> Permanent { get; } = new();
    public Exception? ThrowOnMarkProcessed { get; set; }
    public Func<UsageEventLog, bool>? FailAddWhen { get; set; }

    public Task AddAsync(UsageEventLog log, CancellationToken cancellationToken)
    {
        if (FailAddWhen?.Invoke(log) == true)
        {
            throw new InvalidOperationException("falha simulada ao gravar log");
        }

        Added.Add(log);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageEventLog>> GetDueForProcessingAsync(int maxBatchSize, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<UsageEventLog>>(Array.Empty<UsageEventLog>());

    public Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken)
    {
        if (ThrowOnMarkProcessed is not null)
        {
            throw ThrowOnMarkProcessed;
        }

        Processed.Add(id);
        return Task.CompletedTask;
    }

    public Task MarkTransientFailureAsync(Guid id, int attemptCount, string error, DateTime nextRetryAt, CancellationToken cancellationToken)
    {
        Transient.Add((id, attemptCount, error, nextRetryAt));
        return Task.CompletedTask;
    }

    public Task MarkPermanentFailureAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        Permanent.Add((id, error));
        return Task.CompletedTask;
    }
}

internal sealed class FakeNotifier : IUsageNotifier
{
    public List<(UsageRecord Record, string Project)> Notified { get; } = new();
    public Exception? ThrowOnNotify { get; set; }

    public Task NotifyAsync(UsageRecord record, string projectName, CancellationToken cancellationToken)
    {
        if (ThrowOnNotify is not null)
        {
            throw ThrowOnNotify;
        }

        Notified.Add((record, projectName));
        return Task.CompletedTask;
    }
}
