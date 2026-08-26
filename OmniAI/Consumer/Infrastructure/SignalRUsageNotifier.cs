using Consumer.Domain.Abstractions;
using Consumer.Hubs;
using Microsoft.AspNetCore.SignalR;
using Shared.Entities;

namespace Consumer.Infrastructure;

public class SignalRUsageNotifier : IUsageNotifier
{
    private readonly IHubContext<UsageHub> _hubContext;

    public SignalRUsageNotifier(IHubContext<UsageHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyAsync(UsageRecord record, CancellationToken cancellationToken)
    {
        return _hubContext.Clients.All.SendAsync(
            "UsageReceived",
            new
            {
                record.Id,
                record.ProjectId,
                record.Provider,
                record.Model,
                record.PromptTokens,
                record.CompletionTokens,
                record.TotalTokens,
                record.CostUsd,
                record.LatencyMs,
                record.Status,
                record.OccurredAt,
            },
            cancellationToken);
    }
}
