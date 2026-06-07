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
    public async Task ToolsListWithoutToken_DefaultsToViewerRole()
    {
        var catalog = new McpToolCatalog();
        var tokenStore = new StaticTokenStore();
        var executor = new McpToolExecutor(catalog, new NoopRancherApiService(), tokenStore);
        var server = new McpServer(catalog, executor, tokenStore, NullLogger<McpServer>.Instance);

        var response = await ExecuteAsync(server, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list",
            @params = new { },
        });

        var tools = response.GetProperty("result").GetProperty("tools");
        Assert.Contains(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "list_rancher_clusters");
        Assert.DoesNotContain(tools.EnumerateArray(), tool => tool.GetProperty("name").GetString() == "create_mcp_token");
    }

    [Fact]
    public async Task InvalidBearerToken_Returns401()
    {
        var catalog = new McpToolCatalog();
        var tokenStore = new StaticTokenStore();
        var executor = new McpToolExecutor(catalog, new NoopRancherApiService(), tokenStore);
        var server = new McpServer(catalog, executor, tokenStore, NullLogger<McpServer>.Instance);

        var context = BuildContext(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list",
            @params = new { },
        }, authorization: "Bearer bad-token");

        var result = await server.HandleAsync(context, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task ViewerCannotCallAdminOnlyTool()
    {
        var catalog = new McpToolCatalog();
        var tokenStore = new StaticTokenStore();
        tokenStore.SetToken("valid-viewer", new McpTokenRecord("token-secret", "hash", McpRole.Viewer, DateTimeOffset.UtcNow));
        var executor = new McpToolExecutor(catalog, new NoopRancherApiService(), tokenStore);
        var server = new McpServer(catalog, executor, tokenStore, NullLogger<McpServer>.Instance);

        var context = BuildContext(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new
            {
                name = "create_mcp_token",
                arguments = new { role = "admin" },
            },
        }, authorization: "Bearer valid-viewer");

        var result = await server.HandleAsync(context, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
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
        public Task<IReadOnlyList<rancher_devops_operator.Models.RancherCluster>> ListClustersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<rancher_devops_operator.Models.RancherCluster>>([]);
        public Task<IReadOnlyList<rancher_devops_operator.Models.RancherProject>> ListProjectsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<rancher_devops_operator.Models.RancherProject>>([]);
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
        public Task<string> InvokeRawAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken) => Task.FromResult("{}");
    }

    private sealed class StaticTokenStore : IMcpTokenStore
    {
        private readonly Dictionary<string, McpTokenRecord> _records = new(StringComparer.OrdinalIgnoreCase);

        public void SetToken(string rawToken, McpTokenRecord record)
        {
            _records[rawToken] = record;
        }

        public Task<McpTokenRecord?> ResolveAsync(string rawToken, CancellationToken cancellationToken)
            => Task.FromResult(_records.TryGetValue(rawToken, out var record) ? record : null);

        public Task<IReadOnlyList<McpTokenRecord>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<McpTokenRecord>>(_records.Values.ToArray());

        public Task<McpTokenCreationResult> CreateAsync(McpRole role, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<bool> DeleteAsync(string secretName, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task SeedBootstrapTokenAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
