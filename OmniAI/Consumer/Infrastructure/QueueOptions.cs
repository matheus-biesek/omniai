namespace Consumer.Infrastructure;

public class QueueOptions
{
    public const string SectionName = "Queue";

    public string StreamKey { get; set; } = string.Empty;
    public string ConsumerGroup { get; set; } = string.Empty;
    public string ConsumerName { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 50;
}
