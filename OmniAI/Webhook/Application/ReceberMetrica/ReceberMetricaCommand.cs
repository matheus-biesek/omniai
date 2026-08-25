namespace Webhook.Application.ReceberMetrica;

public sealed record ReceberMetricaCommand(
    string ApiKeyRaw,
    string Provider,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal? CostUsd,
    int LatencyMs,
    string Status,
    DateTime Timestamp);
