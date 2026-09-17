namespace ApiGraphQL.Infrastructure;

public class LoginRateLimitOptions
{
    public const string SectionName = "LoginRateLimit";

    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}
