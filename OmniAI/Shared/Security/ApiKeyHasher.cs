using System.Security.Cryptography;
using System.Text;

namespace Shared.Security;

public static class ApiKeyHasher
{
    public static string Hash(string rawApiKey, string pepper)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawApiKey));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
