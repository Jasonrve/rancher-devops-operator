using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using rancher_devops_operator.Mcp;
using rancher_devops_operator.Services;

using Xunit;

namespace rancher_devops_operator.Tests;

public class McpServerTests
{
    [Fact]
    public async Task ToolsList_ReturnsRancherOnlyCatalog()
    {
        var catalog = new McpToolCatalog();
        var authContext = new RancherRequestAuthContext();
        var executor = new McpToolExecutor(catalog, new NoopRancherApiService());
        var server = new McpServer(catalog, executor, authContext, NullLogger<McpServer>.Instance);

        var response = await ExecuteAsync(server, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list",
            @params = new { },
        });

        var tools = response.GetProperty("result").GetProperty("tools");
        Assert.Contains(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "cluster_list");
        Assert.Contains(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "project_member_delete");
        Assert.Contains(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "list_fleet_gitrepos");
        Assert.Contains(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "create_fleet_gitrepo");
        Assert.DoesNotContain(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "mcp_token_create");
        Assert.DoesNotContain(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "kubernetes_get");
    }

    [Fact]
    public async Task ToolsList_AllowsPassThroughAuthorizationHeader()
    {
        var catalog = new McpToolCatalog();
        var authContext = new RancherRequestAuthContext();
        var executor = new McpToolExecutor(catalog, new NoopRancherApiService());
        var server = new McpServer(catalog, executor, authContext, NullLogger<McpServer>.Instance);

        var context = BuildContext(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list",
            @params = new { },
        }, authorization: "Bearer pass-through-token");

        var result = await server.HandleAsync(context, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static DefaultHttpContext BuildContext(object payload, string? authorization = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        context.RequestServices = new ServiceCollection()
            .AddOptions()
            .AddLogging()
            .BuildServiceProvider();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            context.Request.Headers.Authorization = authorization;
        }

        var body = JsonSerializer.Serialize(payload);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ExecuteAsync(McpServer server, object payload)
    {
        var context = BuildContext(payload);
        var result = await server.HandleAsync(context, CancellationToken.None);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        return doc.RootElement.Clone();
    }

    private sealed class NoopRancherApiService : IRancherApiService
    {
        public Task<List<rancher_devops_operator.Models.RancherCluster>> ListClustersAsync(CancellationToken cancellationToken) => Task.FromResult(new List<rancher_devops_operator.Models.RancherCluster>());
        public Task<List<rancher_devops_operator.Models.RancherProject>> ListProjectsAsync(CancellationToken cancellationToken) => Task.FromResult(new List<rancher_devops_operator.Models.RancherProject>());
        public Task<string?> GetClusterIdByNameAsync(string clusterName, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> GetClusterKubeconfigAsync(string clusterId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<rancher_devops_operator.Models.RancherProject?> CreateProjectAsync(string clusterId, string projectName, string? description, CancellationToken cancellationToken) => Task.FromResult<rancher_devops_operator.Models.RancherProject?>(null);
        public Task<rancher_devops_operator.Models.RancherProject?> GetProjectByNameAsync(string clusterId, string projectName, CancellationToken cancellationToken) => Task.FromResult<rancher_devops_operator.Models.RancherProject?>(null);
        public Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<rancher_devops_operator.Models.RancherNamespace?> CreateNamespaceAsync(string projectId, string namespaceName, CancellationToken cancellationToken) => Task.FromResult<rancher_devops_operator.Models.RancherNamespace?>(null);
        public Task<rancher_devops_operator.Models.RancherNamespace?> GetNamespaceAsync(string clusterId, string namespaceName, CancellationToken cancellationToken) => Task.FromResult<rancher_devops_operator.Models.RancherNamespace?>(null);
        public Task<rancher_devops_operator.Models.RancherNamespace?> UpdateNamespaceProjectAsync(string clusterId, string namespaceName, string newProjectId, CancellationToken cancellationToken) => Task.FromResult<rancher_devops_operator.Models.RancherNamespace?>(null);
        public Task<bool> RemoveNamespaceFromProjectAsync(string clusterId, string namespaceName, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<List<rancher_devops_operator.Models.RancherNamespace>> GetProjectNamespacesAsync(string projectId, CancellationToken cancellationToken) => Task.FromResult(new List<rancher_devops_operator.Models.RancherNamespace>());
        public Task<bool> DeleteNamespaceAsync(string clusterId, string namespaceName, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> EnsureNamespaceManagedByAsync(string clusterId, string namespaceName, bool createdByOperator, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<rancher_devops_operator.Models.RancherProjectRoleBinding?> CreateProjectMemberAsync(string projectId, string principalId, string role, CancellationToken cancellationToken) => Task.FromResult<rancher_devops_operator.Models.RancherProjectRoleBinding?>(null);
        public Task<List<rancher_devops_operator.Models.RancherProjectRoleBinding>> GetProjectMembersAsync(string projectId, CancellationToken cancellationToken) => Task.FromResult(new List<rancher_devops_operator.Models.RancherProjectRoleBinding>());
        public Task<bool> DeleteProjectMemberAsync(string bindingId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<rancher_devops_operator.Models.RancherPrincipal?> GetPrincipalByNameAsync(string principalName, CancellationToken cancellationToken) => Task.FromResult<rancher_devops_operator.Models.RancherPrincipal?>(null);
        public Task<JsonElement> ListFleetGitReposAsync(CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> GetFleetGitRepoAsync(string repoId, CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> ListFleetBundlesAsync(CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> GetFleetBundleStatusAsync(string bundleId, CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> GetFleetSyncStatusAsync(string repoId, CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> GetFleetDeploymentErrorsAsync(string? repoId, CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> CreateFleetGitRepoAsync(string name, string? repo, string? branch, IReadOnlyList<string>? paths, IReadOnlyDictionary<string, string>? targets, CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> UpdateFleetGitRepoAsync(string repoId, string? name, string? repo, string? branch, IReadOnlyList<string>? paths, CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> DeleteFleetGitRepoAsync(string repoId, CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> ForceFleetSyncAsync(string repoId, CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> PauseFleetGitRepoAsync(string repoId, CancellationToken cancellationToken) => Task.FromResult(EmptyObject());
        public Task<JsonElement> ResumeFleetGitRepoAsync(string repoId, CancellationToken cancellationToken) => Task.FromResult(EmptyObject());

        private static JsonElement EmptyObject()
        {
            using var doc = JsonDocument.Parse("{}");
            return doc.RootElement.Clone();
        }
    }
}
