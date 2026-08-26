namespace Shared.Entities;

public class UsageRecord
{
    public long Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid SourceEventLogId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public int LatencyMs { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }

    public Project Project { get; set; } = null!;
    public UsageEventLog SourceEventLog { get; set; } = null!;
}
