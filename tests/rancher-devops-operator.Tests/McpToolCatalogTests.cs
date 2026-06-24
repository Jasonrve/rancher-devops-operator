using rancher_devops_operator.Mcp;

using Xunit;

namespace rancher_devops_operator.Tests;

public class McpToolCatalogTests
{
    [Fact]
    public void Catalog_ContainsOnlyRancherTools()
    {
        var catalog = new McpToolCatalog();

        var tools = catalog.GetTools(McpRole.Viewer).Select(tool => tool.Name).ToArray();

        Assert.Contains("cluster_list", tools);
        Assert.Contains("project_list", tools);
        Assert.Contains("namespace_create", tools);
        Assert.Contains("principal_get_by_name", tools);
        Assert.DoesNotContain("kubernetes_get", tools);
        Assert.DoesNotContain("mcp_token_create", tools);
    }

    [Fact]
    public void Find_ReturnsNullForUnknownTool()
    {
        var catalog = new McpToolCatalog();

        Assert.Null(catalog.Find("not_real"));
    }
}
