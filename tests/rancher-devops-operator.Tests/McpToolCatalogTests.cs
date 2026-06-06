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

        Assert.Contains("cluster_list", tools);
        Assert.Contains("project_list", tools);
        Assert.Contains("kubernetes_get", tools);
        Assert.DoesNotContain("kubernetes_delete", tools);
        Assert.DoesNotContain("mcp_token_create", tools);
    }

    [Fact]
    public void AdminRole_SeesTokenManagementAndWriteTools()
    {
        var catalog = new McpToolCatalog();

        var tools = catalog.GetTools(McpRole.Admin).Select(tool => tool.Name).ToArray();

        Assert.Contains("mcp_token_create", tools);
        Assert.Contains("mcp_token_delete", tools);
        Assert.Contains("kubernetes_delete", tools);
    }
}
