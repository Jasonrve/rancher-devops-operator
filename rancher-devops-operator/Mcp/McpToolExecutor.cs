using System.Net.Http;
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

        return tool.Name switch
        {
            "whoami" => Task.FromResult<object>(WrapJson(BuildWhoamiPayload(principal))),
            "get_effective_role" => Task.FromResult<object>(WrapJson(BuildEffectiveRolePayload(principal))),
            "list_allowed_tools" => Task.FromResult<object>(WrapJson(BuildAllowedToolsPayload(principal))),
            "explain_user_access" => Task.FromResult<object>(WrapJson(BuildAccessExplanationPayload(principal))),

            "get_rancher_version" => RawTextAsync(HttpMethod.Get, "/v3/settings/server-version", null, cancellationToken),
            "check_rancher_api_health" => RawTextAsync(HttpMethod.Get, "/v3", null, cancellationToken),
            "get_rancher_server_health" => RawTextAsync(HttpMethod.Get, "/healthz", null, cancellationToken),
            "list_rancher_clusters" => Task.FromResult<object>(WrapJson(await _rancherApiService.ListClustersAsync(cancellationToken))),
            "get_rancher_cluster" => await GetClusterReadAsync(arguments, cancellationToken),
            "get_cluster_summary" => await GetClusterSummaryAsync(arguments, cancellationToken),
            "get_cluster_status" => await GetClusterStatusAsync(arguments, cancellationToken),
            "get_cluster_agent_status" => await GetClusterAgentStatusAsync(arguments, cancellationToken),
            "get_cluster_registration_status" => await GetClusterRegistrationStatusAsync(arguments, cancellationToken),
            "get_downstream_cluster_connectivity" => await GetDownstreamConnectivityAsync(arguments, cancellationToken),
            "get_cluster_agent_diagnostics" => await GetClusterAgentDiagnosticsAsync(arguments, cancellationToken),
            "get_rancher_recent_warnings" => RawTextAsync(HttpMethod.Get, "/v3/events?limit=25&sort=-timestamp", null, cancellationToken),

            "list_projects" => Task.FromResult<object>(WrapJson(await _rancherApiService.ListProjectsAsync(cancellationToken))),
            "get_project" => await GetProjectReadAsync(arguments, cancellationToken),
            "list_project_namespaces" => await ListProjectNamespacesAsync(arguments, cancellationToken),
            "list_project_members" => await ListProjectMembersAsync(arguments, cancellationToken),
            "list_project_role_template_bindings" => await ListProjectRoleTemplateBindingsAsync(arguments, cancellationToken),
            "list_cluster_role_template_bindings" => RawTextAsync(HttpMethod.Get, "/v3/clusterRoleTemplateBindings", null, cancellationToken),

            "list_rancher_users" => RawTextAsync(HttpMethod.Get, "/v3/users", null, cancellationToken),
            "list_rancher_groups" => RawTextAsync(HttpMethod.Get, "/v3/groups", null, cancellationToken),
            "list_global_roles" => RawTextAsync(HttpMethod.Get, "/v3/globalRoles", null, cancellationToken),
            "list_role_templates" => RawTextAsync(HttpMethod.Get, "/v3/roleTemplates", null, cancellationToken),

            "list_fleet_gitrepos" => RawTextAsync(HttpMethod.Get, "/v1/fleet.cattle.io.gitrepos", null, cancellationToken),
            "get_fleet_gitrepo" => await GetFleetGitRepoAsync(arguments, cancellationToken),
            "list_fleet_bundles" => RawTextAsync(HttpMethod.Get, "/v1/fleet.cattle.io.bundles", null, cancellationToken),
            "get_fleet_bundle_status" => await GetFleetBundleStatusAsync(arguments, cancellationToken),
            "get_fleet_sync_status" => await GetFleetSyncStatusAsync(arguments, cancellationToken),
            "get_fleet_deployment_errors" => await GetFleetDeploymentErrorsAsync(arguments, cancellationToken),

            "list_rancher_apps" => RawTextAsync(HttpMethod.Get, "/v1/apps", null, cancellationToken),
            "get_rancher_app" => await GetRancherAppAsync(arguments, cancellationToken),
            "get_rancher_app_values" => await GetRancherAppValuesAsync(arguments, cancellationToken),
            "list_rancher_chart_repositories" => RawTextAsync(HttpMethod.Get, "/v1/catalog.cattle.io.clusterrepos", null, cancellationToken),
            "search_rancher_catalog_charts" => await SearchRancherCatalogChartsAsync(arguments, cancellationToken),
            "get_rancher_webhook_status" => RawTextAsync(HttpMethod.Get, "/v3/webhooks", null, cancellationToken),

            "list_mcp_tokens" => Task.FromResult<object>(WrapJson(await _tokenStore.ListAsync(cancellationToken))),
            "create_mcp_token" => await CreateTokenAsync(arguments, cancellationToken),
            "rotate_mcp_token" => await RotateTokenAsync(arguments, cancellationToken),
            "revoke_mcp_token" => await RevokeTokenAsync(arguments, cancellationToken),

            "import_cluster" => await ImportClusterAsync(arguments, cancellationToken),
            "generate_cluster_registration_command" => await GenerateClusterRegistrationCommandAsync(arguments, cancellationToken),
            "rotate_cluster_registration_token" => await RotateClusterRegistrationTokenAsync(arguments, cancellationToken),
            "update_cluster_labels" => await UpdateClusterLabelsAsync(arguments, cancellationToken),
            "update_cluster_annotations" => await UpdateClusterAnnotationsAsync(arguments, cancellationToken),
            "delete_rancher_cluster" => await DeleteClusterAsync(arguments, cancellationToken),
            "restart_cluster_agent" => await RestartClusterAgentAsync(arguments, cancellationToken),
            "redeploy_cluster_agent" => await RedeployClusterAgentAsync(arguments, cancellationToken),
            "regenerate_cluster_agent_manifest" => await RegenerateClusterAgentManifestAsync(arguments, cancellationToken),

            "create_project" => await CreateProjectAsync(arguments, cancellationToken),
            "update_project" => await UpdateProjectAsync(arguments, cancellationToken),
            "delete_project" => await DeleteProjectAsync(arguments, cancellationToken),
            "move_namespace_to_project" => await MoveNamespaceToProjectAsync(arguments, cancellationToken),
            "assign_project_member" => await AssignProjectMemberAsync(arguments, cancellationToken),
            "remove_project_member" => await RemoveProjectMemberAsync(arguments, cancellationToken),
            "assign_global_role" => await AssignGlobalRoleAsync(arguments, cancellationToken),
            "remove_global_role" => await RemoveGlobalRoleAsync(arguments, cancellationToken),
            "assign_cluster_role" => await AssignClusterRoleAsync(arguments, cancellationToken),
            "remove_cluster_role" => await RemoveClusterRoleAsync(arguments, cancellationToken),
            "assign_project_role" => await AssignProjectRoleAsync(arguments, cancellationToken),
            "remove_project_role" => await RemoveProjectRoleAsync(arguments, cancellationToken),

            "create_fleet_gitrepo" => await CreateFleetGitRepoAsync(arguments, cancellationToken),
            "update_fleet_gitrepo" => await UpdateFleetGitRepoAsync(arguments, cancellationToken),
            "delete_fleet_gitrepo" => await DeleteFleetGitRepoAsync(arguments, cancellationToken),
            "force_fleet_sync" => await ForceFleetSyncAsync(arguments, cancellationToken),
            "pause_fleet_gitrepo" => await PauseFleetGitRepoAsync(arguments, cancellationToken),
            "resume_fleet_gitrepo" => await ResumeFleetGitRepoAsync(arguments, cancellationToken),

            "install_rancher_app" => await InstallRancherAppAsync(arguments, cancellationToken),
            "upgrade_rancher_app" => await UpgradeRancherAppAsync(arguments, cancellationToken),
            "rollback_rancher_app" => await RollbackRancherAppAsync(arguments, cancellationToken),
            "uninstall_rancher_app" => await UninstallRancherAppAsync(arguments, cancellationToken),
            "add_rancher_chart_repository" => await AddRancherChartRepositoryAsync(arguments, cancellationToken),
            "refresh_rancher_chart_repository" => await RefreshRancherChartRepositoryAsync(arguments, cancellationToken),

            _ => WrapText($"Tool '{toolName}' is enabled but no executor was registered."),
        };
    }

    private static object BuildWhoamiPayload(McpPrincipal principal) => new
    {
        principal = new
        {
            role = principal.Role.ToString().ToLowerInvariant(),
            isAnonymous = principal.IsAnonymous,
            tokenSecretName = principal.TokenSecretName,
        },
    };

    private static object BuildEffectiveRolePayload(McpPrincipal principal) => new
    {
        role = principal.Role.ToString().ToLowerInvariant(),
        isAnonymous = principal.IsAnonymous,
        source = principal.IsAnonymous ? "anonymous" : "token",
    };

    private object BuildAllowedToolsPayload(McpPrincipal principal)
        => new
        {
            role = principal.Role.ToString().ToLowerInvariant(),
            tools = _catalog.GetTools(principal.Role)
                .Select(tool => new
                {
                    name = tool.Name,
                    category = tool.Category,
                    readOnly = tool.ReadOnly,
                    minimumRole = tool.MinimumRole.ToString().ToLowerInvariant(),
                    description = tool.Description,
                })
                .ToArray(),
        };

    private object BuildAccessExplanationPayload(McpPrincipal principal)
        => new
        {
            role = principal.Role.ToString().ToLowerInvariant(),
            isAnonymous = principal.IsAnonymous,
            allowedToolCount = _catalog.GetTools(principal.Role).Count,
            allowedCategories = _catalog.GetTools(principal.Role)
                .Select(tool => tool.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .ToArray(),
            note = principal.IsAnonymous ? "Anonymous principals can only access viewer tools." : "Authenticated principals inherit the role associated with their MCP token.",
        };

    private async Task<object> RawTextAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        => WrapText(await _rancherApiService.InvokeRawAsync(method, path, body, cancellationToken));

    private async Task<object> GetClusterReadAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        return await RawTextAsync(HttpMethod.Get, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}", null, cancellationToken);
    }

    private async Task<object> GetClusterSummaryAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var raw = await _rancherApiService.InvokeRawAsync(HttpMethod.Get, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}", null, cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        var cluster = doc.RootElement.Clone();
        return WrapJson(new
        {
            clusterId,
            clusterName = ReadString(cluster, "name"),
            state = ReadString(cluster, "state"),
            description = ReadString(cluster, "description"),
            raw = cluster,
        });
    }

    private async Task<object> GetClusterStatusAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var raw = await _rancherApiService.InvokeRawAsync(HttpMethod.Get, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}", null, cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        var cluster = doc.RootElement.Clone();
        return WrapJson(new
        {
            clusterId,
            state = ReadString(cluster, "state"),
            conditions = ExtractConditions(cluster),
            raw = cluster,
        });
    }

    private async Task<object> GetClusterAgentStatusAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var raw = await _rancherApiService.InvokeRawAsync(HttpMethod.Get, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}", null, cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        var cluster = doc.RootElement.Clone();
        return WrapJson(new
        {
            clusterId,
            agentReady = ReadString(cluster, "agentReady"),
            agentImage = ReadString(cluster, "agentImage"),
            agentNamespace = ReadString(cluster, "agentNamespace"),
            raw = cluster,
        });
    }

    private async Task<object> GetClusterRegistrationStatusAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        return await RawTextAsync(HttpMethod.Get, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}/clusterregistrationtokens", null, cancellationToken);
    }

    private async Task<object> GetDownstreamConnectivityAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var raw = await _rancherApiService.InvokeRawAsync(HttpMethod.Get, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}", null, cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        var cluster = doc.RootElement.Clone();
        return WrapJson(new
        {
            clusterId,
            connected = !string.Equals(ReadString(cluster, "state"), "inactive", StringComparison.OrdinalIgnoreCase),
            state = ReadString(cluster, "state"),
            raw = cluster,
        });
    }

    private async Task<object> GetClusterAgentDiagnosticsAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var raw = await _rancherApiService.InvokeRawAsync(HttpMethod.Get, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}", null, cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        var cluster = doc.RootElement.Clone();
        return WrapJson(new
        {
            clusterId,
            diagnostics = new
            {
                state = ReadString(cluster, "state"),
                agentReady = ReadString(cluster, "agentReady"),
                agentImage = ReadString(cluster, "agentImage"),
                conditions = ExtractConditions(cluster),
            },
            raw = cluster,
        });
    }

    private async Task<object> GetProjectReadAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var project = await ResolveProjectAsync(arguments, cancellationToken);
        if (project is not null)
        {
            return WrapText(await _rancherApiService.InvokeRawAsync(HttpMethod.Get, $"/v3/projects/{Uri.EscapeDataString(project.Id)}", null, cancellationToken));
        }

        var projectId = GetString(arguments, "id", "projectId");
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("get_project requires id/projectId or clusterId+name");
        }

        return await RawTextAsync(HttpMethod.Get, $"/v3/projects/{Uri.EscapeDataString(projectId)}", null, cancellationToken);
    }

    private async Task<object> ListProjectNamespacesAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectId = await ResolveProjectIdAsync(arguments, cancellationToken);
        return WrapJson(await _rancherApiService.GetProjectNamespacesAsync(projectId, cancellationToken));
    }

    private async Task<object> ListProjectMembersAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectId = await ResolveProjectIdAsync(arguments, cancellationToken);
        return WrapJson(await _rancherApiService.GetProjectMembersAsync(projectId, cancellationToken));
    }

    private async Task<object> ListProjectRoleTemplateBindingsAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectId = await ResolveProjectIdAsync(arguments, cancellationToken);
        return await RawTextAsync(HttpMethod.Get, $"/v3/projectRoleTemplateBindings?projectId={Uri.EscapeDataString(projectId)}", null, cancellationToken);
    }

    private async Task<object> GetFleetGitRepoAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var repoId = GetString(arguments, "id", "gitRepoId", "name", "gitRepoName");
        if (string.IsNullOrWhiteSpace(repoId))
        {
            throw new ArgumentException("get_fleet_gitrepo requires id, gitRepoId, name, or gitRepoName");
        }

        return await RawTextAsync(HttpMethod.Get, $"/v1/fleet.cattle.io.gitrepos/{Uri.EscapeDataString(repoId)}", null, cancellationToken);
    }

    private async Task<object> GetFleetBundleStatusAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var bundleId = GetString(arguments, "id", "bundleId", "name", "bundleName");
        if (string.IsNullOrWhiteSpace(bundleId))
        {
            throw new ArgumentException("get_fleet_bundle_status requires id, bundleId, name, or bundleName");
        }

        return await RawTextAsync(HttpMethod.Get, $"/v1/fleet.cattle.io.bundles/{Uri.EscapeDataString(bundleId)}", null, cancellationToken);
    }

    private async Task<object> GetFleetSyncStatusAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var repoId = GetString(arguments, "gitRepoId", "id", "name", "gitRepoName");
        if (string.IsNullOrWhiteSpace(repoId))
        {
            throw new ArgumentException("get_fleet_sync_status requires gitRepoId, id, name, or gitRepoName");
        }

        return await RawTextAsync(HttpMethod.Get, $"/v1/fleet.cattle.io.gitrepos/{Uri.EscapeDataString(repoId)}", null, cancellationToken);
    }

    private async Task<object> GetFleetDeploymentErrorsAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var repoId = GetString(arguments, "gitRepoId", "id", "name", "gitRepoName");
        if (string.IsNullOrWhiteSpace(repoId))
        {
            return await RawTextAsync(HttpMethod.Get, "/v1/fleet.cattle.io.bundles?limit=50", null, cancellationToken);
        }

        return await RawTextAsync(HttpMethod.Get, $"/v1/fleet.cattle.io.bundles?gitRepoId={Uri.EscapeDataString(repoId)}", null, cancellationToken);
    }

    private async Task<object> GetRancherAppAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var appId = GetString(arguments, "id", "appId", "name", "appName");
        if (string.IsNullOrWhiteSpace(appId))
        {
            throw new ArgumentException("get_rancher_app requires id, appId, name, or appName");
        }

        return await RawTextAsync(HttpMethod.Get, $"/v1/apps/{Uri.EscapeDataString(appId)}", null, cancellationToken);
    }

    private async Task<object> GetRancherAppValuesAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var appId = GetString(arguments, "id", "appId", "name", "appName");
        if (string.IsNullOrWhiteSpace(appId))
        {
            throw new ArgumentException("get_rancher_app_values requires id, appId, name, or appName");
        }

        return await RawTextAsync(HttpMethod.Post, $"/v1/apps/{Uri.EscapeDataString(appId)}?action=getValues", new { }, cancellationToken);
    }

    private async Task<object> SearchRancherCatalogChartsAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var query = GetString(arguments, "name", "query", "chart", "search");
        if (string.IsNullOrWhiteSpace(query))
        {
            return await RawTextAsync(HttpMethod.Get, "/v1/catalog.cattle.io.charts", null, cancellationToken);
        }

        return await RawTextAsync(HttpMethod.Get, $"/v1/catalog.cattle.io.charts?name={Uri.EscapeDataString(query)}", null, cancellationToken);
    }

    private async Task<object> CreateTokenAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var role = ParseRole(arguments, McpRole.Admin);
        var created = await _tokenStore.CreateAsync(role, cancellationToken);
        return WrapJson(new
        {
            secretName = created.SecretName,
            role = created.Role.ToString().ToLowerInvariant(),
            tokenRedacted = true,
        });
    }

    private async Task<object> RotateTokenAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var role = ParseRole(arguments, McpRole.Admin);
        var secretName = GetString(arguments, "secretName", "name");
        var deleted = false;
        if (!string.IsNullOrWhiteSpace(secretName))
        {
            deleted = await _tokenStore.DeleteAsync(secretName, cancellationToken);
        }

        var created = await _tokenStore.CreateAsync(role, cancellationToken);
        return WrapJson(new
        {
            rotated = deleted,
            previousSecretName = secretName,
            secretName = created.SecretName,
            role = created.Role.ToString().ToLowerInvariant(),
            tokenRedacted = true,
        });
    }

    private async Task<object> RevokeTokenAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var secretName = GetString(arguments, "secretName", "name");
        if (string.IsNullOrWhiteSpace(secretName))
        {
            throw new ArgumentException("revoke_mcp_token requires secretName or name");
        }

        var deleted = await _tokenStore.DeleteAsync(secretName, cancellationToken);
        return WrapJson(new { secretName, deleted });
    }

    private async Task<object> ImportClusterAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var name = GetString(arguments, "name", "clusterName") ?? "imported-cluster";
        var body = new
        {
            type = "cluster",
            name,
            description = GetString(arguments, "description"),
            labels = GetStringDictionary(arguments, "labels"),
            annotations = GetStringDictionary(arguments, "annotations"),
        };

        return await RawTextAsync(HttpMethod.Post, "/v3/clusters", body, cancellationToken);
    }

    private async Task<object> GenerateClusterRegistrationCommandAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        return await RawTextAsync(HttpMethod.Post, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}?action=generateRegistrationCommand", new { }, cancellationToken);
    }

    private async Task<object> RotateClusterRegistrationTokenAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        return await RawTextAsync(HttpMethod.Post, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}?action=generateRegistrationToken", new { }, cancellationToken);
    }

    private async Task<object> UpdateClusterLabelsAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var body = new
        {
            labels = GetStringDictionary(arguments, "labels") ?? BuildSingleEntryDictionary(arguments, "labelKey", "labelValue"),
        };

        return await RawTextAsync(HttpMethod.Put, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}", body, cancellationToken);
    }

    private async Task<object> UpdateClusterAnnotationsAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var body = new
        {
            annotations = GetStringDictionary(arguments, "annotations") ?? BuildSingleEntryDictionary(arguments, "annotationKey", "annotationValue"),
        };

        return await RawTextAsync(HttpMethod.Put, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}", body, cancellationToken);
    }

    private async Task<object> DeleteClusterAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        return await RawTextAsync(HttpMethod.Delete, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}", null, cancellationToken);
    }

    private async Task<object> RestartClusterAgentAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        return await RawTextAsync(HttpMethod.Post, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}?action=restartAgent", new { }, cancellationToken);
    }

    private async Task<object> RedeployClusterAgentAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        return await RawTextAsync(HttpMethod.Post, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}?action=redeployAgent", new { }, cancellationToken);
    }

    private async Task<object> RegenerateClusterAgentManifestAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        return await RawTextAsync(HttpMethod.Post, $"/v3/clusters/{Uri.EscapeDataString(clusterId)}?action=generateKubeconfig", new { }, cancellationToken);
    }

    private async Task<object> CreateProjectAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var name = GetString(arguments, "name", "projectName") ?? throw new ArgumentException("create_project requires name or projectName");
        var project = await _rancherApiService.CreateProjectAsync(clusterId, name, GetString(arguments, "description"), cancellationToken);
        return WrapJson(project ?? new { created = false, clusterId, name });
    }

    private async Task<object> UpdateProjectAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectId = await ResolveProjectIdAsync(arguments, cancellationToken);
        var body = new
        {
            name = GetString(arguments, "name", "projectName"),
            description = GetString(arguments, "description"),
            resourceQuota = GetStringDictionary(arguments, "resourceQuota"),
            annotations = GetStringDictionary(arguments, "annotations"),
        };

        return await RawTextAsync(HttpMethod.Put, $"/v3/projects/{Uri.EscapeDataString(projectId)}", body, cancellationToken);
    }

    private async Task<object> DeleteProjectAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectId = await ResolveProjectIdAsync(arguments, cancellationToken);
        var deleted = await _rancherApiService.DeleteProjectAsync(projectId, cancellationToken);
        return WrapJson(new { projectId, deleted });
    }

    private async Task<object> MoveNamespaceToProjectAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var namespaceName = GetString(arguments, "namespaceName", "name") ?? throw new ArgumentException("move_namespace_to_project requires namespaceName or name");
        var targetProjectId = GetString(arguments, "projectId", "targetProjectId", "newProjectId") ?? throw new ArgumentException("move_namespace_to_project requires projectId, targetProjectId, or newProjectId");
        var updated = await _rancherApiService.UpdateNamespaceProjectAsync(clusterId, namespaceName, targetProjectId, cancellationToken);
        return WrapJson(updated ?? new { clusterId, namespaceName, projectId = targetProjectId, updated = false });
    }

    private async Task<object> AssignProjectMemberAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectId = await ResolveProjectIdAsync(arguments, cancellationToken);
        var principalId = await ResolvePrincipalIdAsync(arguments, cancellationToken);
        var role = GetString(arguments, "roleTemplateId", "role", "roleTemplate") ?? "project-member";
        var binding = await _rancherApiService.CreateProjectMemberAsync(projectId, principalId, role, cancellationToken);
        return WrapJson(binding);
    }

    private async Task<object> RemoveProjectMemberAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var bindingId = GetString(arguments, "bindingId", "id");
        if (string.IsNullOrWhiteSpace(bindingId))
        {
            var projectId = await ResolveProjectIdAsync(arguments, cancellationToken);
            var principalId = await ResolvePrincipalIdAsync(arguments, cancellationToken);
            var roleTemplateId = GetString(arguments, "roleTemplateId", "role", "roleTemplate");
            var members = await _rancherApiService.GetProjectMembersAsync(projectId, cancellationToken);
            bindingId = members.FirstOrDefault(binding =>
                string.Equals(binding.UserPrincipalId, principalId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(binding.GroupPrincipalId, principalId, StringComparison.OrdinalIgnoreCase))?.Id;

            if (string.IsNullOrWhiteSpace(bindingId))
            {
                throw new ArgumentException($"remove_project_member could not find a binding for principal '{principalId}'" +
                    (string.IsNullOrWhiteSpace(roleTemplateId) ? string.Empty : $" and role '{roleTemplateId}'"));
            }
        }

        var deleted = await _rancherApiService.DeleteProjectMemberAsync(bindingId, cancellationToken);
        return WrapJson(new { bindingId, deleted });
    }

    private async Task<object> AssignGlobalRoleAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var principalId = await ResolvePrincipalIdAsync(arguments, cancellationToken);
        var roleTemplateId = GetString(arguments, "roleTemplateId", "role", "roleTemplate") ?? throw new ArgumentException("assign_global_role requires roleTemplateId or role");
        var body = new
        {
            type = "globalRoleBinding",
            userPrincipalId = principalId,
            globalRoleId = roleTemplateId,
        };

        return await RawTextAsync(HttpMethod.Post, "/v3/globalRoleBindings", body, cancellationToken);
    }

    private async Task<object> RemoveGlobalRoleAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var bindingId = GetString(arguments, "bindingId", "id") ?? throw new ArgumentException("remove_global_role requires bindingId or id");
        return await RawTextAsync(HttpMethod.Delete, $"/v3/globalRoleBindings/{Uri.EscapeDataString(bindingId)}", null, cancellationToken);
    }

    private async Task<object> AssignClusterRoleAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var principalId = await ResolvePrincipalIdAsync(arguments, cancellationToken);
        var roleTemplateId = GetString(arguments, "roleTemplateId", "role", "roleTemplate") ?? throw new ArgumentException("assign_cluster_role requires roleTemplateId or role");
        var body = new
        {
            type = "clusterRoleTemplateBinding",
            clusterId,
            userPrincipalId = principalId,
            roleTemplateId,
        };

        return await RawTextAsync(HttpMethod.Post, "/v3/clusterRoleTemplateBindings", body, cancellationToken);
    }

    private async Task<object> RemoveClusterRoleAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var bindingId = GetString(arguments, "bindingId", "id") ?? throw new ArgumentException("remove_cluster_role requires bindingId or id");
        return await RawTextAsync(HttpMethod.Delete, $"/v3/clusterRoleTemplateBindings/{Uri.EscapeDataString(bindingId)}", null, cancellationToken);
    }

    private async Task<object> AssignProjectRoleAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectId = await ResolveProjectIdAsync(arguments, cancellationToken);
        var principalId = await ResolvePrincipalIdAsync(arguments, cancellationToken);
        var roleTemplateId = GetString(arguments, "roleTemplateId", "role", "roleTemplate") ?? throw new ArgumentException("assign_project_role requires roleTemplateId or role");
        var binding = await _rancherApiService.CreateProjectMemberAsync(projectId, principalId, roleTemplateId, cancellationToken);
        return WrapJson(binding);
    }

    private async Task<object> RemoveProjectRoleAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        return await RemoveProjectMemberAsync(arguments, cancellationToken);
    }

    private async Task<object> CreateFleetGitRepoAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var body = new
        {
            type = "fleet.cattle.io.gitrepo",
            name = GetString(arguments, "name", "gitRepoName") ?? throw new ArgumentException("create_fleet_gitrepo requires name or gitRepoName"),
            repo = GetString(arguments, "repo", "url"),
            branch = GetString(arguments, "branch"),
            paths = GetStringList(arguments, "paths"),
            targets = GetStringDictionary(arguments, "targets"),
        };

        return await RawTextAsync(HttpMethod.Post, "/v1/fleet.cattle.io.gitrepos", body, cancellationToken);
    }

    private async Task<object> UpdateFleetGitRepoAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var repoId = GetString(arguments, "id", "gitRepoId", "name", "gitRepoName") ?? throw new ArgumentException("update_fleet_gitrepo requires id, gitRepoId, name, or gitRepoName");
        var body = new
        {
            name = GetString(arguments, "name", "gitRepoName"),
            repo = GetString(arguments, "repo", "url"),
            branch = GetString(arguments, "branch"),
            paths = GetStringList(arguments, "paths"),
        };

        return await RawTextAsync(HttpMethod.Put, $"/v1/fleet.cattle.io.gitrepos/{Uri.EscapeDataString(repoId)}", body, cancellationToken);
    }

    private async Task<object> DeleteFleetGitRepoAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var repoId = GetString(arguments, "id", "gitRepoId", "name", "gitRepoName") ?? throw new ArgumentException("delete_fleet_gitrepo requires id, gitRepoId, name, or gitRepoName");
        return await RawTextAsync(HttpMethod.Delete, $"/v1/fleet.cattle.io.gitrepos/{Uri.EscapeDataString(repoId)}", null, cancellationToken);
    }

    private async Task<object> ForceFleetSyncAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var repoId = GetString(arguments, "gitRepoId", "id", "name", "gitRepoName") ?? throw new ArgumentException("force_fleet_sync requires gitRepoId, id, name, or gitRepoName");
        return await RawTextAsync(HttpMethod.Post, $"/v1/fleet.cattle.io.gitrepos/{Uri.EscapeDataString(repoId)}?action=forceSync", new { }, cancellationToken);
    }

    private async Task<object> PauseFleetGitRepoAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var repoId = GetString(arguments, "gitRepoId", "id", "name", "gitRepoName") ?? throw new ArgumentException("pause_fleet_gitrepo requires gitRepoId, id, name, or gitRepoName");
        return await RawTextAsync(HttpMethod.Post, $"/v1/fleet.cattle.io.gitrepos/{Uri.EscapeDataString(repoId)}?action=pause", new { }, cancellationToken);
    }

    private async Task<object> ResumeFleetGitRepoAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var repoId = GetString(arguments, "gitRepoId", "id", "name", "gitRepoName") ?? throw new ArgumentException("resume_fleet_gitrepo requires gitRepoId, id, name, or gitRepoName");
        return await RawTextAsync(HttpMethod.Post, $"/v1/fleet.cattle.io.gitrepos/{Uri.EscapeDataString(repoId)}?action=resume", new { }, cancellationToken);
    }

    private async Task<object> InstallRancherAppAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var body = new
        {
            type = "app",
            name = GetString(arguments, "name", "appName") ?? throw new ArgumentException("install_rancher_app requires name or appName"),
            chart = GetString(arguments, "chart"),
            repo = GetString(arguments, "repo", "repository"),
            namespace = GetString(arguments, "namespace", "namespaceName"),
            values = GetStringDictionary(arguments, "values"),
        };

        return await RawTextAsync(HttpMethod.Post, "/v1/apps", body, cancellationToken);
    }

    private async Task<object> UpgradeRancherAppAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var appId = GetString(arguments, "id", "appId", "name", "appName") ?? throw new ArgumentException("upgrade_rancher_app requires id, appId, name, or appName");
        var body = new
        {
            chart = GetString(arguments, "chart"),
            version = GetString(arguments, "version"),
            values = GetStringDictionary(arguments, "values"),
        };

        return await RawTextAsync(HttpMethod.Post, $"/v1/apps/{Uri.EscapeDataString(appId)}?action=upgrade", body, cancellationToken);
    }

    private async Task<object> RollbackRancherAppAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var appId = GetString(arguments, "id", "appId", "name", "appName") ?? throw new ArgumentException("rollback_rancher_app requires id, appId, name, or appName");
        var body = new
        {
            revision = GetString(arguments, "revision", "version"),
        };

        return await RawTextAsync(HttpMethod.Post, $"/v1/apps/{Uri.EscapeDataString(appId)}?action=rollback", body, cancellationToken);
    }

    private async Task<object> UninstallRancherAppAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var appId = GetString(arguments, "id", "appId", "name", "appName") ?? throw new ArgumentException("uninstall_rancher_app requires id, appId, name, or appName");
        return await RawTextAsync(HttpMethod.Delete, $"/v1/apps/{Uri.EscapeDataString(appId)}", null, cancellationToken);
    }

    private async Task<object> AddRancherChartRepositoryAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var body = new
        {
            type = "catalog.cattle.io.clusterrepo",
            name = GetString(arguments, "name", "repositoryName") ?? throw new ArgumentException("add_rancher_chart_repository requires name or repositoryName"),
            url = GetString(arguments, "url", "repo"),
        };

        return await RawTextAsync(HttpMethod.Post, "/v1/catalog.cattle.io.clusterrepos", body, cancellationToken);
    }

    private async Task<object> RefreshRancherChartRepositoryAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var repoId = GetString(arguments, "id", "repositoryId", "name", "repositoryName") ?? throw new ArgumentException("refresh_rancher_chart_repository requires id, repositoryId, name, or repositoryName");
        return await RawTextAsync(HttpMethod.Post, $"/v1/catalog.cattle.io.clusterrepos/{Uri.EscapeDataString(repoId)}?action=refresh", new { }, cancellationToken);
    }

    private async Task<object> CreateProjectAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = await ResolveClusterIdAsync(arguments, cancellationToken);
        var name = GetString(arguments, "name", "projectName") ?? throw new ArgumentException("create_project requires name or projectName");
        var project = await _rancherApiService.CreateProjectAsync(clusterId, name, GetString(arguments, "description"), cancellationToken);
        return WrapJson(project ?? new { created = false, clusterId, name });
    }

    private static McpRole ParseRole(JsonElement? arguments, McpRole defaultValue)
    {
        var raw = GetString(arguments, "role", "mcpRole");
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

    private async Task<string> ResolveClusterIdAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var clusterId = GetString(arguments, "id", "clusterId");
        if (!string.IsNullOrWhiteSpace(clusterId))
        {
            return clusterId;
        }

        var clusterName = GetString(arguments, "name", "clusterName");
        if (string.IsNullOrWhiteSpace(clusterName))
        {
            throw new ArgumentException("A cluster id or cluster name is required.");
        }

        var resolved = await _rancherApiService.GetClusterIdByNameAsync(clusterName, cancellationToken);
        return resolved ?? throw new ArgumentException($"Cluster '{clusterName}' was not found.");
    }

    private async Task<RancherProject?> ResolveProjectAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "id", "projectId");
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        var clusterId = GetString(arguments, "clusterId");
        var projectName = GetString(arguments, "name", "projectName");
        if (string.IsNullOrWhiteSpace(clusterId) || string.IsNullOrWhiteSpace(projectName))
        {
            return null;
        }

        return await _rancherApiService.GetProjectByNameAsync(clusterId, projectName, cancellationToken);
    }

    private async Task<string> ResolveProjectIdAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var projectId = GetString(arguments, "id", "projectId");
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            return projectId;
        }

        var clusterId = GetString(arguments, "clusterId");
        var projectName = GetString(arguments, "name", "projectName");
        if (string.IsNullOrWhiteSpace(clusterId) || string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("A project id or clusterId+projectName is required.");
        }

        var project = await _rancherApiService.GetProjectByNameAsync(clusterId, projectName, cancellationToken)
            ?? throw new ArgumentException($"Project '{projectName}' was not found in cluster '{clusterId}'.");
        return project.Id;
    }

    private async Task<string> ResolvePrincipalIdAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var principalId = GetString(arguments, "principalId", "userPrincipalId", "groupPrincipalId");
        if (!string.IsNullOrWhiteSpace(principalId))
        {
            return principalId;
        }

        var principalName = GetString(arguments, "principalName", "name", "userName", "groupName");
        if (string.IsNullOrWhiteSpace(principalName))
        {
            throw new ArgumentException("A principalId or principalName is required.");
        }

        var principal = await _rancherApiService.GetPrincipalByNameAsync(principalName, cancellationToken)
            ?? throw new ArgumentException($"Principal '{principalName}' was not found.");
        return principal.Id;
    }

    private static List<string> ExtractConditions(JsonElement cluster)
    {
        if (!cluster.TryGetProperty("conditions", out var conditions) || conditions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return conditions.EnumerateArray()
            .Select(condition => condition.ValueKind == JsonValueKind.Object
                ? ReadString(condition, "message") ?? ReadString(condition, "status") ?? condition.GetRawText()
                : condition.GetRawText())
            .ToList();
    }

    private static Dictionary<string, string> BuildSingleEntryDictionary(JsonElement? arguments, string keyName, string valueName)
    {
        var key = GetString(arguments, keyName);
        var value = GetString(arguments, valueName);
        if (string.IsNullOrWhiteSpace(key) || value is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [key] = value,
        };
    }

    private static Dictionary<string, string>? GetStringDictionary(JsonElement? arguments, params string[] names)
    {
        if (arguments is null || arguments.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (!arguments.Value.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    entries[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }

            return entries;
        }

        return null;
    }

    private static List<string>? GetStringList(JsonElement? arguments, params string[] names)
    {
        if (arguments is null || arguments.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (!arguments.Value.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }

        return null;
    }

    private static string? GetString(JsonElement? arguments, params string[] names)
    {
        if (arguments is null || arguments.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (arguments.Value.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
