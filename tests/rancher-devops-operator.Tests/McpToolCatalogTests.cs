using rancher_devops_operator.Mcp;

using Xunit;
namespace rancher_devops_operator.Tests;

public class McpToolCatalogTests
{
    [Fact]
    public void ViewerRole_SeesReadOnlyAndTokenlessToolsOnly()
    {
        var catalog = new McpToolCatalog();

        var tools = catalog.GetTools(McpRole.Viewer).Select(tool => tool.Name).ToArray();

        Assert.Contains("list_rancher_clusters", tools);
        Assert.Contains("list_projects", tools);
        Assert.Contains("whoami", tools);
        Assert.DoesNotContain("delete_rancher_cluster", tools);
        Assert.DoesNotContain("create_mcp_token", tools);
    }

    [Fact]
    public void AdminRole_SeesTokenManagementAndWriteTools()
    {
        var catalog = new McpToolCatalog();

        var tools = catalog.GetTools(McpRole.Admin).Select(tool => tool.Name).ToArray();

        Assert.Contains("create_mcp_token", tools);
        Assert.Contains("revoke_mcp_token", tools);
        Assert.Contains("delete_rancher_cluster", tools);
        Assert.Contains("install_rancher_app", tools);
    }
}
