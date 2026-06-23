namespace rancher_devops_operator.Mcp;

public interface IMcpToolCatalog
{
    IReadOnlyList<McpToolDefinition> GetTools(McpRole role);
    McpToolDefinition? Find(string name);
}

public sealed class McpToolCatalog : IMcpToolCatalog
{
    private static McpToolDefinition Tool(string name, string description, McpRole minimumRole = McpRole.Viewer, bool readOnly = true, string category = "rancher")
        => new(name, description, minimumRole, ReadOnly: readOnly, Category: category, Implemented: true);

    private static readonly IReadOnlyList<McpToolDefinition> AllTools = new[]
    {
        Tool("whoami", "Return the authenticated principal, token identity, and anonymity state.", category: "identity"),
        Tool("get_effective_role", "Show the effective MCP role for the current principal.", category: "identity"),
        Tool("list_allowed_tools", "List the tools visible to the current role.", category: "identity"),
        Tool("explain_user_access", "Summarize the current user's effective access and allowed tool families.", category: "identity"),

        Tool("get_rancher_version", "Fetch the Rancher server version or version setting.", category: "rancher"),
        Tool("check_rancher_api_health", "Check the Rancher API health/status endpoint.", category: "rancher"),
        Tool("get_rancher_server_health", "Check the Rancher server health endpoint.", category: "rancher"),
        Tool("list_rancher_clusters", "List Rancher clusters.", category: "clusters"),
        Tool("get_rancher_cluster", "Get a Rancher cluster by id or name.", category: "clusters"),
        Tool("get_cluster_summary", "Summarize a cluster's core metadata and state.", category: "clusters"),
        Tool("get_cluster_status", "Show the current status for a cluster.", category: "clusters"),
        Tool("get_cluster_agent_status", "Show the downstream cluster agent status.", category: "clusters"),
        Tool("get_cluster_registration_status", "Show the cluster registration/import status.", category: "clusters"),
        Tool("get_downstream_cluster_connectivity", "Summarize the connectivity posture of a downstream cluster.", category: "clusters"),
        Tool("get_cluster_agent_diagnostics", "Return a diagnostic bundle for the downstream cluster agent.", category: "clusters"),
        Tool("get_rancher_recent_warnings", "Return the latest Rancher warning events.", category: "clusters"),

        Tool("list_projects", "List Rancher projects.", category: "projects"),
        Tool("get_project", "Get a Rancher project by id or by name within a cluster.", category: "projects"),
        Tool("list_project_namespaces", "List namespaces assigned to a project.", category: "projects"),
        Tool("list_project_members", "List the project role bindings for a project.", category: "projects"),
        Tool("list_project_role_template_bindings", "List project role template bindings.", category: "projects"),
        Tool("list_cluster_role_template_bindings", "List cluster role template bindings.", category: "projects"),

        Tool("list_rancher_users", "List Rancher users.", category: "identity"),
        Tool("list_rancher_groups", "List Rancher groups.", category: "identity"),
        Tool("list_global_roles", "List Rancher global roles.", category: "identity"),
        Tool("list_role_templates", "List Rancher role templates.", category: "identity"),

        Tool("list_fleet_gitrepos", "List Fleet GitRepos.", category: "fleet"),
        Tool("get_fleet_gitrepo", "Get a Fleet GitRepo by id or name.", category: "fleet"),
        Tool("list_fleet_bundles", "List Fleet bundles.", category: "fleet"),
        Tool("get_fleet_bundle_status", "Return the status for a Fleet bundle.", category: "fleet"),
        Tool("get_fleet_sync_status", "Return the sync status for a Fleet GitRepo or bundle.", category: "fleet"),
        Tool("get_fleet_deployment_errors", "Return Fleet deployment errors or failure summaries.", category: "fleet"),

        Tool("list_rancher_apps", "List Rancher apps.", category: "apps"),
        Tool("get_rancher_app", "Get a Rancher app by id or name.", category: "apps"),
        Tool("get_rancher_app_values", "Fetch the configured values for a Rancher app.", category: "apps"),
        Tool("list_rancher_chart_repositories", "List Rancher chart repositories.", category: "apps"),
        Tool("search_rancher_catalog_charts", "Search the Rancher chart catalog.", category: "apps"),
        Tool("get_rancher_webhook_status", "Return webhook status or webhook inventory.", category: "apps"),

        Tool("import_cluster", "Create or import a downstream cluster into Rancher.", McpRole.Admin, readOnly: false, category: "clusters"),
        Tool("generate_cluster_registration_command", "Generate a downstream cluster registration command.", McpRole.Admin, readOnly: false, category: "clusters"),
        Tool("rotate_cluster_registration_token", "Rotate the downstream cluster registration token.", McpRole.Admin, readOnly: false, category: "clusters"),
        Tool("update_cluster_labels", "Update Rancher cluster labels.", McpRole.Admin, readOnly: false, category: "clusters"),
        Tool("update_cluster_annotations", "Update Rancher cluster annotations.", McpRole.Admin, readOnly: false, category: "clusters"),
        Tool("delete_rancher_cluster", "Delete a Rancher cluster.", McpRole.Admin, readOnly: false, category: "clusters"),

        Tool("create_project", "Create a Rancher project.", McpRole.Admin, readOnly: false, category: "projects"),
        Tool("update_project", "Update a Rancher project.", McpRole.Admin, readOnly: false, category: "projects"),
        Tool("delete_project", "Delete a Rancher project.", McpRole.Admin, readOnly: false, category: "projects"),
        Tool("move_namespace_to_project", "Move a namespace to another project.", McpRole.Admin, readOnly: false, category: "projects"),
        Tool("assign_project_member", "Assign a member to a project role.", McpRole.Admin, readOnly: false, category: "projects"),
        Tool("remove_project_member", "Remove a member from a project role binding.", McpRole.Admin, readOnly: false, category: "projects"),
        Tool("assign_global_role", "Assign a global role binding.", McpRole.Admin, readOnly: false, category: "identity"),
        Tool("remove_global_role", "Remove a global role binding.", McpRole.Admin, readOnly: false, category: "identity"),
        Tool("assign_cluster_role", "Assign a cluster role binding.", McpRole.Admin, readOnly: false, category: "clusters"),
        Tool("remove_cluster_role", "Remove a cluster role binding.", McpRole.Admin, readOnly: false, category: "clusters"),
        Tool("assign_project_role", "Assign a project role binding.", McpRole.Admin, readOnly: false, category: "projects"),
        Tool("remove_project_role", "Remove a project role binding.", McpRole.Admin, readOnly: false, category: "projects"),

        Tool("create_fleet_gitrepo", "Create a Fleet GitRepo.", McpRole.Admin, readOnly: false, category: "fleet"),
        Tool("update_fleet_gitrepo", "Update a Fleet GitRepo.", McpRole.Admin, readOnly: false, category: "fleet"),
        Tool("delete_fleet_gitrepo", "Delete a Fleet GitRepo.", McpRole.Admin, readOnly: false, category: "fleet"),
        Tool("force_fleet_sync", "Force a Fleet sync.", McpRole.Admin, readOnly: false, category: "fleet"),
        Tool("pause_fleet_gitrepo", "Pause a Fleet GitRepo.", McpRole.Admin, readOnly: false, category: "fleet"),
        Tool("resume_fleet_gitrepo", "Resume a Fleet GitRepo.", McpRole.Admin, readOnly: false, category: "fleet"),

        Tool("install_rancher_app", "Install a Rancher app.", McpRole.Admin, readOnly: false, category: "apps"),
        Tool("upgrade_rancher_app", "Upgrade a Rancher app.", McpRole.Admin, readOnly: false, category: "apps"),
        Tool("rollback_rancher_app", "Rollback a Rancher app.", McpRole.Admin, readOnly: false, category: "apps"),
        Tool("uninstall_rancher_app", "Uninstall a Rancher app.", McpRole.Admin, readOnly: false, category: "apps"),
        Tool("add_rancher_chart_repository", "Add a Rancher chart repository.", McpRole.Admin, readOnly: false, category: "apps"),
        Tool("refresh_rancher_chart_repository", "Refresh a Rancher chart repository.", McpRole.Admin, readOnly: false, category: "apps"),

        Tool("restart_cluster_agent", "Restart the downstream cluster agent.", McpRole.Admin, readOnly: false, category: "clusters"),
        Tool("redeploy_cluster_agent", "Redeploy the downstream cluster agent.", McpRole.Admin, readOnly: false, category: "clusters"),
        Tool("regenerate_cluster_agent_manifest", "Regenerate the downstream cluster agent manifest.", McpRole.Admin, readOnly: false, category: "clusters"),
    };

    private static readonly IReadOnlyDictionary<string, string> LegacyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cluster_list"] = "list_rancher_clusters",
        ["project_list"] = "list_projects",
    };

    public IReadOnlyList<McpToolDefinition> GetTools(McpRole role)
        => AllTools.Where(tool => tool.MinimumRole <= role).ToArray();

    public McpToolDefinition? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var canonicalName = LegacyAliases.TryGetValue(name, out var alias)
            ? alias
            : name;

        return AllTools.FirstOrDefault(tool => string.Equals(tool.Name, canonicalName, StringComparison.OrdinalIgnoreCase));
    }
}
