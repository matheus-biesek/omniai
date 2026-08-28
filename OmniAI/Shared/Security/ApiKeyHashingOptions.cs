namespace Shared.Security;

public class ApiKeyHashingOptions
{
    public const string SectionName = "ApiKeyHashing";

    public string PepperSecret { get; set; } = string.Empty;
}
