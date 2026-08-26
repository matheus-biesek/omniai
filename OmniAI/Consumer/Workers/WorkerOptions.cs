namespace Consumer.Workers;

public class WorkerOptions
{
    public const string SectionName = "Worker";

    public int PollIntervalMs { get; set; } = 500;
}
