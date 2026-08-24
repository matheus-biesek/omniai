namespace Shared.Entities;

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();
}
