namespace Shared.Entities;

public class ApiKey
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string KeyHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Project Project { get; set; } = null!;
}
