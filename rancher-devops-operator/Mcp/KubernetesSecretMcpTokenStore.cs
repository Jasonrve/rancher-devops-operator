using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace rancher_devops_operator.Mcp;

public interface IMcpTokenStore
{
    Task<McpTokenRecord?> ResolveAsync(string rawToken, CancellationToken cancellationToken);
    Task<IReadOnlyList<McpTokenRecord>> ListAsync(CancellationToken cancellationToken);
    Task<McpTokenCreationResult> CreateAsync(McpRole role, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string secretName, CancellationToken cancellationToken);
    Task SeedBootstrapTokenAsync(CancellationToken cancellationToken);
}

public sealed class KubernetesSecretMcpTokenStore : IMcpTokenStore
{
    private const string TokenComponentLabel = "mcp-token";
    private const string ManagedByLabel = "app.kubernetes.io/managed-by";
    private const string ManagedByValue = "rancher-devops-operator";
    private const string RoleKey = "role";
    private const string HashKey = "tokenHash";
    private const string CreatedAtKey = "createdAt";

    private readonly IMcpSecretClient _secretClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KubernetesSecretMcpTokenStore> _logger;
    private readonly string _namespace;

    public KubernetesSecretMcpTokenStore(IMcpSecretClient secretClient, IConfiguration configuration, ILogger<KubernetesSecretMcpTokenStore> logger)
    {
        _secretClient = secretClient;
        _configuration = configuration;
        _logger = logger;
        _namespace = configuration.GetValue<string>("Mcp:TokenNamespace")
            ?? configuration.GetValue<string>("Kubernetes:Namespace")
            ?? Environment.GetEnvironmentVariable("POD_NAMESPACE")
            ?? "default";
    }

    public async Task<McpTokenRecord?> ResolveAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = McpTokenHasher.ComputeHash(rawToken);
        var tokens = await ListAsync(cancellationToken);
        return tokens.FirstOrDefault(t => string.Equals(t.TokenHash, hash, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<McpTokenRecord>> ListAsync(CancellationToken cancellationToken)
    {
        var selector = $"{ManagedByLabel}={ManagedByValue},mcp.devops.io/{TokenComponentLabel}=true";
        var secretList = await _secretClient.ListSecretsAsync(_namespace, selector, cancellationToken);
        var records = new List<McpTokenRecord>();

        foreach (var secret in secretList.Items)
        {
            var data = secret.StringData ?? new Dictionary<string, string>();
            var hash = ReadSecretField(secret, data, HashKey);
            var roleText = ReadSecretField(secret, data, RoleKey);
            var createdAtText = ReadSecretField(secret, data, CreatedAtKey);
            if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(roleText))
            {
                continue;
            }

            var role = roleText.Equals("admin", StringComparison.OrdinalIgnoreCase) ? McpRole.Admin : McpRole.Viewer;
            var createdAt = DateTimeOffset.TryParse(createdAtText, out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            records.Add(new McpTokenRecord(secret.Metadata?.Name ?? string.Empty, hash, role, createdAt));
        }

        return records.OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public async Task<McpTokenCreationResult> CreateAsync(McpRole role, CancellationToken cancellationToken)
    {
        var rawToken = McpTokenHasher.GenerateRawToken();
        var hash = McpTokenHasher.ComputeHash(rawToken);
        var roleText = role == McpRole.Admin ? "admin" : "viewer";
        var name = $"mcp-token-{hash[..12]}";

        var secret = new V1Secret
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = _namespace,
                Labels = new Dictionary<string, string>
                {
                    [ManagedByLabel] = ManagedByValue,
                    [$"mcp.devops.io/{TokenComponentLabel}"] = "true",
                    [RoleKey] = roleText,
                },
                Annotations = new Dictionary<string, string>
                {
                    ["mcp.devops.io/token-hash"] = hash,
                },
            },
            StringData = new Dictionary<string, string>
            {
                [HashKey] = hash,
                [RoleKey] = roleText,
                [CreatedAtKey] = DateTimeOffset.UtcNow.ToString("O"),
            },
            Type = "Opaque",
        };

        await _secretClient.CreateSecretAsync(_namespace, secret, cancellationToken);
        _logger.LogInformation("Created MCP token secret {SecretName} with role {Role}", name, roleText);

        return new McpTokenCreationResult(rawToken, name, role, hash);
    }

    public async Task<bool> DeleteAsync(string secretName, CancellationToken cancellationToken)
    {
        try
        {
            await _secretClient.DeleteSecretAsync(_namespace, secretName, cancellationToken);
            _logger.LogInformation("Deleted MCP token secret {SecretName}", secretName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete MCP token secret {SecretName}", secretName);
            return false;
        }
    }

    public async Task SeedBootstrapTokenAsync(CancellationToken cancellationToken)
    {
        var bootstrapHash = _configuration["Mcp:BootstrapAdminTokenHash"];
        if (string.IsNullOrWhiteSpace(bootstrapHash))
        {
            return;
        }

        var existing = await ListAsync(cancellationToken);
        if (existing.Any(t => t.Role == McpRole.Admin))
        {
            return;
        }

        var secret = new V1Secret
        {
            Metadata = new V1ObjectMeta
            {
                Name = _configuration.GetValue<string>("Mcp:BootstrapAdminTokenSecretName") ?? "mcp-bootstrap-admin-token",
                NamespaceProperty = _namespace,
                Labels = new Dictionary<string, string>
                {
                    [ManagedByLabel] = ManagedByValue,
                    [$"mcp.devops.io/{TokenComponentLabel}"] = "true",
                    [RoleKey] = "admin",
                    ["mcp.devops.io/bootstrap"] = "true",
                },
            },
            StringData = new Dictionary<string, string>
            {
                [HashKey] = bootstrapHash.Trim().ToLowerInvariant(),
                [RoleKey] = "admin",
                [CreatedAtKey] = DateTimeOffset.UtcNow.ToString("O"),
            },
            Type = "Opaque",
        };

        try
        {
            await _secretClient.CreateSecretAsync(_namespace, secret, cancellationToken);
            _logger.LogInformation("Seeded bootstrap MCP admin token secret {SecretName}", secret.Metadata!.Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bootstrap MCP token seed skipped; secret may already exist");
        }
    }

    private static string? ReadSecretField(V1Secret secret, IDictionary<string, string> stringData, string key)
    {
        if (stringData.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (secret.Data != null && secret.Data.TryGetValue(key, out var raw))
        {
            return System.Text.Encoding.UTF8.GetString(raw);
        }

        return secret.Metadata?.Annotations != null && secret.Metadata.Annotations.TryGetValue($"mcp.devops.io/{key}", out var annotated)
            ? annotated
            : null;
    }
}
