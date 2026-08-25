namespace Webhook.Application.ReceberMetrica;

public enum ReceberMetricaStatus
{
    Aceito,
    ApiKeyInvalida,
    FilaCheia,
}

public sealed record ReceberMetricaResult(ReceberMetricaStatus Status);
