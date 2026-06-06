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
        new McpToolDefinition("cluster_list", "List Rancher clusters.", McpRole.Viewer, ReadOnly: true, Category: "rancher", Implemented: true),
        new McpToolDefinition("project_list", "List Rancher projects.", McpRole.Viewer, ReadOnly: true, Category: "rancher", Implemented: true),

        new McpToolDefinition("kubernetes_get", "Get a Kubernetes resource from the connected cluster.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_list", "List Kubernetes resources from the connected cluster.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_get_all", "Aggregate a cross-kind Kubernetes inventory.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_logs", "Read pod logs.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_inspect_pod", "Inspect a pod in detail.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_describe", "Describe a Kubernetes resource.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_events", "List Kubernetes events.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_dep", "Show resource dependencies.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_rollout_history", "Show rollout history for a workload.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_node_analysis", "Summarize node capacity and utilization.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_diff", "Diff two Kubernetes resources.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_watch", "Watch resource changes and emit diffs.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_capacity", "Summarize cluster capacity and utilization.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_workload_health", "Summarize workload health.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_resource_summary", "Aggregate pod and container resource usage.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_event_summary", "Summarize events by reason/kind.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_download_file", "Download a file from a pod.", McpRole.Viewer, ReadOnly: true, Category: "kubernetes", Implemented: false),

        new McpToolDefinition("kubernetes_create", "Create a Kubernetes resource.", McpRole.Admin, ReadOnly: false, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_patch", "Patch a Kubernetes resource.", McpRole.Admin, ReadOnly: false, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_exec", "Execute a command in a pod.", McpRole.Admin, ReadOnly: false, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_upload_file", "Upload a file to a pod.", McpRole.Admin, ReadOnly: false, Category: "kubernetes", Implemented: false),
        new McpToolDefinition("kubernetes_delete", "Delete a Kubernetes resource.", McpRole.Admin, ReadOnly: false, Category: "kubernetes", Implemented: false),

        new McpToolDefinition("mcp_token_list", "List MCP auth tokens.", McpRole.Admin, ReadOnly: true, Category: "tokens", Implemented: true),
        new McpToolDefinition("mcp_token_create", "Create an MCP auth token.", McpRole.Admin, ReadOnly: false, Category: "tokens", Implemented: true),
        new McpToolDefinition("mcp_token_delete", "Delete an MCP auth token.", McpRole.Admin, ReadOnly: false, Category: "tokens", Implemented: true),
    };

    public IReadOnlyList<McpToolDefinition> GetTools(McpRole role)
        => AllTools.Where(tool => tool.MinimumRole <= role).ToArray();

    public McpToolDefinition? Find(string name)
        => AllTools.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase));
}
