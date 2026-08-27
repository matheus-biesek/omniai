namespace ApiGraphQL.Application.Login;

public class DashboardCredentialsOptions
{
    public const string SectionName = "Dashboard";

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
