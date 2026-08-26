namespace Consumer.Application.ProcessarEventoDeUso;

public class RetryOptions
{
    public const string SectionName = "Retry";

    public int MaxAttempts { get; set; } = 5;
    public int BaseDelaySeconds { get; set; } = 10;
}
