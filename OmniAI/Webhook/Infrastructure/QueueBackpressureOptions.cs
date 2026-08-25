namespace Webhook.Infrastructure;

public class QueueBackpressureOptions
{
    public const string SectionName = "QueueBackpressure";

    public string StreamKey { get; set; } = string.Empty;
    public int MaxQueueSize { get; set; }
}
