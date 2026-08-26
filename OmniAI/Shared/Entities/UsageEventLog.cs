namespace Shared.Entities;

public enum UsageEventLogStatus
{
    Pendente = 0,
    Processado = 1,
    FalhaTransiente = 2,
    FalhaPermanente = 3,
}

public class UsageEventLog
{
    public Guid Id { get; set; }
    public string RedisEntryId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public UsageEventLogStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
