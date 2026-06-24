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

    public McpToolExecutor(IMcpToolCatalog catalog, IRancherApiService rancherApiService)
    {
        _catalog = catalog;
        _rancherApiService = rancherApiService;
    }

    public async Task<object> ExecuteAsync(string toolName, JsonElement? arguments, McpPrincipal principal, CancellationToken cancellationToken)
    {
        var tool = _catalog.Find(toolName);
        if (tool is null)
        {
            return WrapText($"Unknown tool '{toolName}'.");
        }

        return tool.Name switch
        {
            "cluster_list" => WrapJson(await _rancherApiService.ListClustersAsync(cancellationToken)),
            "cluster_get_id" => WrapJson(new
            {
                clusterName = RequireString(arguments, "clusterName", "name"),
                clusterId = await _rancherApiService.GetClusterIdByNameAsync(RequireString(arguments, "clusterName", "name"), cancellationToken),
            }),
            "cluster_get_kubeconfig" => WrapJson(new
            {
                clusterId = RequireString(arguments, "clusterId", "id"),
                kubeconfig = await _rancherApiService.GetClusterKubeconfigAsync(RequireString(arguments, "clusterId", "id"), cancellationToken),
            }),

            "project_list" => WrapJson(await _rancherApiService.ListProjectsAsync(cancellationToken)),
            "project_get" => WrapJson(await _rancherApiService.GetProjectByNameAsync(
                RequireString(arguments, "clusterId", "cluster_id"),
                RequireString(arguments, "projectName", "name"),
                cancellationToken)),
            "project_create" => WrapJson(await _rancherApiService.CreateProjectAsync(
                RequireString(arguments, "clusterId", "cluster_id"),
                RequireString(arguments, "projectName", "name"),
                GetString(arguments, "description"),
                cancellationToken)),
            "project_delete" => WrapJson(new
            {
                projectId = RequireString(arguments, "projectId", "id"),
                deleted = await _rancherApiService.DeleteProjectAsync(RequireString(arguments, "projectId", "id"), cancellationToken),
            }),

            "namespace_create" => WrapJson(await _rancherApiService.CreateNamespaceAsync(
                RequireString(arguments, "projectId", "project_id"),
                RequireString(arguments, "namespaceName", "name"),
                cancellationToken)),
            "namespace_get" => WrapJson(await _rancherApiService.GetNamespaceAsync(
                RequireString(arguments, "clusterId", "cluster_id"),
                RequireString(arguments, "namespaceName", "name"),
                cancellationToken)),
            "namespace_update_project" => WrapJson(await _rancherApiService.UpdateNamespaceProjectAsync(
                RequireString(arguments, "clusterId", "cluster_id"),
                RequireString(arguments, "namespaceName", "name"),
                RequireString(arguments, "newProjectId", "projectId", "project_id"),
                cancellationToken)),
            "namespace_remove_project" => WrapJson(new
            {
                clusterId = RequireString(arguments, "clusterId", "cluster_id"),
                namespaceName = RequireString(arguments, "namespaceName", "name"),
                removed = await _rancherApiService.RemoveNamespaceFromProjectAsync(
                    RequireString(arguments, "clusterId", "cluster_id"),
                    RequireString(arguments, "namespaceName", "name"),
                    cancellationToken),
            }),
            "namespace_list_by_project" => WrapJson(await _rancherApiService.GetProjectNamespacesAsync(
                RequireString(arguments, "projectId", "project_id"),
                cancellationToken)),
            "namespace_delete" => WrapJson(new
            {
                clusterId = RequireString(arguments, "clusterId", "cluster_id"),
                namespaceName = RequireString(arguments, "namespaceName", "name"),
                deleted = await _rancherApiService.DeleteNamespaceAsync(
                    RequireString(arguments, "clusterId", "cluster_id"),
                    RequireString(arguments, "namespaceName", "name"),
                    cancellationToken),
            }),
            "namespace_ensure_managed_by" => WrapJson(new
            {
                clusterId = RequireString(arguments, "clusterId", "cluster_id"),
                namespaceName = RequireString(arguments, "namespaceName", "name"),
                createdByOperator = RequireBool(arguments, "createdByOperator", "created_by_operator"),
                updated = await _rancherApiService.EnsureNamespaceManagedByAsync(
                    RequireString(arguments, "clusterId", "cluster_id"),
                    RequireString(arguments, "namespaceName", "name"),
                    RequireBool(arguments, "createdByOperator", "created_by_operator"),
                    cancellationToken),
            }),

            "project_member_create" => WrapJson(await _rancherApiService.CreateProjectMemberAsync(
                RequireString(arguments, "projectId", "project_id"),
                RequireString(arguments, "principalId", "principal_id"),
                RequireString(arguments, "role"),
                cancellationToken)),
            "project_member_list" => WrapJson(await _rancherApiService.GetProjectMembersAsync(
                RequireString(arguments, "projectId", "project_id"),
                cancellationToken)),
            "project_member_delete" => WrapJson(new
            {
                bindingId = RequireString(arguments, "bindingId", "id"),
                deleted = await _rancherApiService.DeleteProjectMemberAsync(RequireString(arguments, "bindingId", "id"), cancellationToken),
            }),
            "principal_get_by_name" => WrapJson(await _rancherApiService.GetPrincipalByNameAsync(
                RequireString(arguments, "principalName", "name"),
                cancellationToken)),

            _ => WrapText($"Tool '{toolName}' is enabled but no executor was registered."),
        };
    }

    private static string RequireString(JsonElement? arguments, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(arguments, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new ArgumentException($"Missing required argument: {string.Join(" or ", names)}");
    }

    private static bool RequireBool(JsonElement? arguments, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetBool(arguments, name, out var value))
            {
                return value;
            }
        }

        throw new ArgumentException($"Missing required boolean argument: {string.Join(" or ", names)}");
    }

    private static bool TryGetBool(JsonElement? arguments, string name, out bool value)
    {
        value = default;
        if (arguments is null || arguments.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!arguments.Value.TryGetProperty(name, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
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

    private static object WrapJson(object? data) => new
    {
        content = new[]
        {
            new { type = "text", text = JsonSerializer.Serialize(data, JsonOptions) },
        },
    };
}
