using System.Security.Cryptography;
using System.Text;

namespace rancher_devops_operator.Mcp;

public static class McpTokenHasher
{
    public static string GenerateRawToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "mcp_" + Base64UrlEncode(bytes);
    }

    public static string ComputeHash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string NormalizeRole(string role)
    {
        return role.Trim().ToLowerInvariant() switch
        {
            "admin" => "admin",
            "viewer" => "viewer",
            _ => throw new ArgumentOutOfRangeException(nameof(role), "Role must be admin or viewer."),
        };
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> input)
    {
        var base64 = Convert.ToBase64String(input);
        return base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
