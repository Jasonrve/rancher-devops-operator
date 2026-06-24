namespace rancher_devops_operator.Mcp;

public interface IMcpToolCatalog
{
    IReadOnlyList<McpToolDefinition> GetTools(McpRole role);
    McpToolDefinition? Find(string name);
}

public sealed class McpToolCatalog : IMcpToolCatalog
{
    private static readonly IReadOnlyList<McpToolDefinition> AllTools = new[]
    {
        new McpToolDefinition("cluster_list", "List Rancher clusters.", McpRole.Viewer, ReadOnly: true, Category: "rancher"),
        new McpToolDefinition("cluster_get_id", "Resolve a Rancher cluster name to its cluster ID.", McpRole.Viewer, ReadOnly: true, Category: "rancher"),
        new McpToolDefinition("cluster_get_kubeconfig", "Fetch a cluster kubeconfig from Rancher.", McpRole.Viewer, ReadOnly: true, Category: "rancher"),

        new McpToolDefinition("project_list", "List Rancher projects.", McpRole.Viewer, ReadOnly: true, Category: "rancher"),
        new McpToolDefinition("project_get", "Get a Rancher project by cluster and project name.", McpRole.Viewer, ReadOnly: true, Category: "rancher"),
        new McpToolDefinition("project_create", "Create a Rancher project.", McpRole.Viewer, ReadOnly: false, Category: "rancher"),
        new McpToolDefinition("project_delete", "Delete a Rancher project.", McpRole.Viewer, ReadOnly: false, Category: "rancher"),

        new McpToolDefinition("namespace_create", "Create a namespace inside a Rancher project.", McpRole.Viewer, ReadOnly: false, Category: "rancher"),
        new McpToolDefinition("namespace_get", "Get a namespace by cluster and name.", McpRole.Viewer, ReadOnly: true, Category: "rancher"),
        new McpToolDefinition("namespace_update_project", "Move a namespace to another Rancher project.", McpRole.Viewer, ReadOnly: false, Category: "rancher"),
        new McpToolDefinition("namespace_remove_project", "Remove a namespace from its Rancher project.", McpRole.Viewer, ReadOnly: false, Category: "rancher"),
        new McpToolDefinition("namespace_list_by_project", "List namespaces for a Rancher project.", McpRole.Viewer, ReadOnly: true, Category: "rancher"),
        new McpToolDefinition("namespace_delete", "Delete a namespace from a Rancher cluster.", McpRole.Viewer, ReadOnly: false, Category: "rancher"),
        new McpToolDefinition("namespace_ensure_managed_by", "Mark a namespace as managed or unmanaged by the operator.", McpRole.Viewer, ReadOnly: false, Category: "rancher"),

        new McpToolDefinition("project_member_create", "Add a member to a Rancher project.", McpRole.Viewer, ReadOnly: false, Category: "rancher"),
        new McpToolDefinition("project_member_list", "List Rancher project members.", McpRole.Viewer, ReadOnly: true, Category: "rancher"),
        new McpToolDefinition("project_member_delete", "Delete a Rancher project member binding.", McpRole.Viewer, ReadOnly: false, Category: "rancher"),
        new McpToolDefinition("principal_get_by_name", "Resolve a Rancher principal by name.", McpRole.Viewer, ReadOnly: true, Category: "rancher"),

        new McpToolDefinition("list_fleet_gitrepos", "List Fleet GitRepos.", McpRole.Viewer, ReadOnly: true, Category: "fleet"),
        new McpToolDefinition("get_fleet_gitrepo", "Get a Fleet GitRepo by id or name.", McpRole.Viewer, ReadOnly: true, Category: "fleet"),
        new McpToolDefinition("list_fleet_bundles", "List Fleet bundles.", McpRole.Viewer, ReadOnly: true, Category: "fleet"),
        new McpToolDefinition("get_fleet_bundle_status", "Return the status for a Fleet bundle.", McpRole.Viewer, ReadOnly: true, Category: "fleet"),
        new McpToolDefinition("get_fleet_sync_status", "Return the sync status for a Fleet GitRepo or bundle.", McpRole.Viewer, ReadOnly: true, Category: "fleet"),
        new McpToolDefinition("get_fleet_deployment_errors", "Return Fleet deployment errors or failure summaries.", McpRole.Viewer, ReadOnly: true, Category: "fleet"),

        new McpToolDefinition("create_fleet_gitrepo", "Create a Fleet GitRepo.", McpRole.Viewer, ReadOnly: false, Category: "fleet"),
        new McpToolDefinition("update_fleet_gitrepo", "Update a Fleet GitRepo.", McpRole.Viewer, ReadOnly: false, Category: "fleet"),
        new McpToolDefinition("delete_fleet_gitrepo", "Delete a Fleet GitRepo.", McpRole.Viewer, ReadOnly: false, Category: "fleet"),
        new McpToolDefinition("force_fleet_sync", "Force a Fleet sync.", McpRole.Viewer, ReadOnly: false, Category: "fleet"),
        new McpToolDefinition("pause_fleet_gitrepo", "Pause a Fleet GitRepo.", McpRole.Viewer, ReadOnly: false, Category: "fleet"),
        new McpToolDefinition("resume_fleet_gitrepo", "Resume a Fleet GitRepo.", McpRole.Viewer, ReadOnly: false, Category: "fleet"),
    };

    public IReadOnlyList<McpToolDefinition> GetTools(McpRole role)
        => AllTools;

    public McpToolDefinition? Find(string name)
        => AllTools.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase));
}
