namespace Shared.Messaging;

public sealed class UsageEventMessage
{
    public required string Project { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required int PromptTokens { get; init; }
    public required int CompletionTokens { get; init; }
    public required int TotalTokens { get; init; }
    public decimal? CostUsd { get; init; }
    public required int LatencyMs { get; init; }
    public required string Status { get; init; }
    public required DateTime Timestamp { get; init; }
}
