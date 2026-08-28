using System.Security.Cryptography;

namespace Shared.Security;

public static class ApiKeyGenerator
{
    private const string Prefix = "ombk_";
    private const int RandomBytesLength = 32;

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(RandomBytesLength);
        var token = Convert.ToBase64String(bytes)
            .Replace("+", string.Empty)
            .Replace("/", string.Empty)
            .Replace("=", string.Empty);

        return Prefix + token;
    }
}
