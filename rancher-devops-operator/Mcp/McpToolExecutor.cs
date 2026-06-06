using System.Text.Json;
using rancher_devops_operator.Models;
using rancher_devops_operator.Services;

namespace rancher_devops_operator.Mcp;

public interface IMcpToolExecutor
{
    Task<object> ExecuteAsync(string toolName, JsonElement? arguments, McpPrincipal principal, CancellationToken cancellationToken);
}

public sealed class McpToolExecutor : IMcpToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IMcpToolCatalog _catalog;
    private readonly IRancherApiService _rancherApiService;
    private readonly IMcpTokenStore _tokenStore;

    public McpToolExecutor(IMcpToolCatalog catalog, IRancherApiService rancherApiService, IMcpTokenStore tokenStore)
    {
        _catalog = catalog;
        _rancherApiService = rancherApiService;
        _tokenStore = tokenStore;
    }

    public async Task<object> ExecuteAsync(string toolName, JsonElement? arguments, McpPrincipal principal, CancellationToken cancellationToken)
    {
        var tool = _catalog.Find(toolName);
        if (tool is null)
        {
            return WrapText($"Unknown tool '{toolName}'.");
        }

        if (tool.MinimumRole > principal.Role)
        {
            throw new UnauthorizedAccessException($"Tool '{toolName}' requires {tool.MinimumRole}.");
        }

        if (!tool.Implemented)
        {
            return WrapText($"Tool '{toolName}' is registered for inventory/compatibility but is not implemented in this build. See docs/mcp.md for the current implementation matrix.");
        }

        return tool.Name switch
        {
            "cluster_list" => WrapJson(await _rancherApiService.ListClustersAsync(cancellationToken)),
            "project_list" => WrapJson(await _rancherApiService.ListProjectsAsync(cancellationToken)),
            "mcp_token_list" => WrapJson(await _tokenStore.ListAsync(cancellationToken)),
            "mcp_token_create" => await CreateTokenAsync(arguments, cancellationToken),
            "mcp_token_delete" => await DeleteTokenAsync(arguments, cancellationToken),
            _ => WrapText($"Tool '{toolName}' is enabled but no executor was registered."),
        };
    }

    private async Task<object> CreateTokenAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var role = ParseRole(arguments, defaultValue: McpRole.Viewer);
        var created = await _tokenStore.CreateAsync(role, cancellationToken);
        return WrapJson(new
        {
            secretName = created.SecretName,
            role = created.Role.ToString().ToLowerInvariant(),
            token = created.RawToken,
        });
    }

    private async Task<object> DeleteTokenAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var secretName = GetString(arguments, "secretName") ?? GetString(arguments, "name");
        if (string.IsNullOrWhiteSpace(secretName))
        {
            throw new ArgumentException("mcp_token_delete requires secretName");
        }

        var deleted = await _tokenStore.DeleteAsync(secretName, cancellationToken);
        return WrapJson(new { secretName, deleted });
    }

    private static McpRole ParseRole(JsonElement? arguments, McpRole defaultValue)
    {
        var raw = GetString(arguments, "role");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "admin" => McpRole.Admin,
            "viewer" => McpRole.Viewer,
            _ => defaultValue,
        };
    }

    private static string? GetString(JsonElement? arguments, string name)
    {
        if (arguments is null || arguments.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return arguments.Value.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static object WrapText(string text) => new
    {
        content = new[]
        {
            new { type = "text", text },
        },
    };

    private static object WrapJson(object data) => new
    {
        content = new[]
        {
            new { type = "text", text = JsonSerializer.Serialize(data, JsonOptions) },
        },
    };
}
