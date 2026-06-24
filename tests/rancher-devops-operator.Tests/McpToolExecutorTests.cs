using System.Text.Json;
using rancher_devops_operator.Mcp;
using rancher_devops_operator.Models;
using rancher_devops_operator.Services;

using Xunit;

namespace rancher_devops_operator.Tests;

public class McpToolExecutorTests
{
    [Theory]
    [MemberData(nameof(ToolCases))]
    public async Task ExecuteAsync_RoutesRancherTools(string toolName, string? argumentsJson, string expectedCall)
    {
        var api = new RecordingRancherApiService();
        var executor = new McpToolExecutor(new McpToolCatalog(), api);

        JsonElement? args = null;
        if (!string.IsNullOrWhiteSpace(argumentsJson))
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            args = doc.RootElement.Clone();
        }

        var result = await executor.ExecuteAsync(toolName, args, McpPrincipal.AnonymousViewer(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(expectedCall, api.LastCall);
    }

    [Fact]
    public async Task UnknownTool_ReturnsHelpfulText()
    {
        var executor = new McpToolExecutor(new McpToolCatalog(), new RecordingRancherApiService());
        var result = await executor.ExecuteAsync("not_a_tool", null, McpPrincipal.AnonymousViewer(), CancellationToken.None);

        var text = JsonSerializer.Serialize(result);
        Assert.Contains("Unknown tool", text);
    }

    public static IEnumerable<object[]> ToolCases()
    {
        yield return ["cluster_list", null, "ListClustersAsync"];
        yield return ["cluster_get_id", "{\"clusterName\":\"cluster-a\"}", "GetClusterIdByNameAsync:cluster-a"];
        yield return ["cluster_get_kubeconfig", "{\"clusterId\":\"c-1\"}", "GetClusterKubeconfigAsync:c-1"];
        yield return ["project_list", null, "ListProjectsAsync"];
        yield return ["project_get", "{\"clusterId\":\"c-1\",\"projectName\":\"proj-a\"}", "GetProjectByNameAsync:c-1:proj-a"];
        yield return ["project_create", "{\"clusterId\":\"c-1\",\"projectName\":\"proj-a\",\"description\":\"desc\"}", "CreateProjectAsync:c-1:proj-a:desc"];
        yield return ["project_delete", "{\"projectId\":\"p-1\"}", "DeleteProjectAsync:p-1"];
        yield return ["namespace_create", "{\"projectId\":\"p-1\",\"namespaceName\":\"ns-a\"}", "CreateNamespaceAsync:p-1:ns-a"];
        yield return ["namespace_get", "{\"clusterId\":\"c-1\",\"namespaceName\":\"ns-a\"}", "GetNamespaceAsync:c-1:ns-a"];
        yield return ["namespace_update_project", "{\"clusterId\":\"c-1\",\"namespaceName\":\"ns-a\",\"newProjectId\":\"p-2\"}", "UpdateNamespaceProjectAsync:c-1:ns-a:p-2"];
        yield return ["namespace_remove_project", "{\"clusterId\":\"c-1\",\"namespaceName\":\"ns-a\"}", "RemoveNamespaceFromProjectAsync:c-1:ns-a"];
        yield return ["namespace_list_by_project", "{\"projectId\":\"p-1\"}", "GetProjectNamespacesAsync:p-1"];
        yield return ["namespace_delete", "{\"clusterId\":\"c-1\",\"namespaceName\":\"ns-a\"}", "DeleteNamespaceAsync:c-1:ns-a"];
        yield return ["namespace_ensure_managed_by", "{\"clusterId\":\"c-1\",\"namespaceName\":\"ns-a\",\"createdByOperator\":true}", "EnsureNamespaceManagedByAsync:c-1:ns-a:True"];
        yield return ["project_member_create", "{\"projectId\":\"p-1\",\"principalId\":\"u-1\",\"role\":\"project-member\"}", "CreateProjectMemberAsync:p-1:u-1:project-member"];
        yield return ["project_member_list", "{\"projectId\":\"p-1\"}", "GetProjectMembersAsync:p-1"];
        yield return ["project_member_delete", "{\"bindingId\":\"b-1\"}", "DeleteProjectMemberAsync:b-1"];
        yield return ["principal_get_by_name", "{\"principalName\":\"user-a\"}", "GetPrincipalByNameAsync:user-a"];
    }

    private sealed class RecordingRancherApiService : IRancherApiService
    {
        public string? LastCall { get; private set; }

        public Task<List<RancherCluster>> ListClustersAsync(CancellationToken cancellationToken)
        {
            LastCall = "ListClustersAsync";
            return Task.FromResult(new List<RancherCluster>());
        }

        public Task<List<RancherProject>> ListProjectsAsync(CancellationToken cancellationToken)
        {
            LastCall = "ListProjectsAsync";
            return Task.FromResult(new List<RancherProject>());
        }

        public Task<string?> GetClusterIdByNameAsync(string clusterName, CancellationToken cancellationToken)
        {
            LastCall = $"GetClusterIdByNameAsync:{clusterName}";
            return Task.FromResult<string?>("cluster-id");
        }

        public Task<string?> GetClusterKubeconfigAsync(string clusterId, CancellationToken cancellationToken)
        {
            LastCall = $"GetClusterKubeconfigAsync:{clusterId}";
            return Task.FromResult<string?>("kubeconfig");
        }

        public Task<RancherProject?> CreateProjectAsync(string clusterId, string projectName, string? description, CancellationToken cancellationToken)
        {
            LastCall = $"CreateProjectAsync:{clusterId}:{projectName}:{description}";
            return Task.FromResult<RancherProject?>(new RancherProject());
        }

        public Task<RancherProject?> GetProjectByNameAsync(string clusterId, string projectName, CancellationToken cancellationToken)
        {
            LastCall = $"GetProjectByNameAsync:{clusterId}:{projectName}";
            return Task.FromResult<RancherProject?>(new RancherProject());
        }

        public Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken)
        {
            LastCall = $"DeleteProjectAsync:{projectId}";
            return Task.FromResult(true);
        }

        public Task<RancherNamespace?> CreateNamespaceAsync(string projectId, string namespaceName, CancellationToken cancellationToken)
        {
            LastCall = $"CreateNamespaceAsync:{projectId}:{namespaceName}";
            return Task.FromResult<RancherNamespace?>(new RancherNamespace());
        }

        public Task<RancherNamespace?> GetNamespaceAsync(string clusterId, string namespaceName, CancellationToken cancellationToken)
        {
            LastCall = $"GetNamespaceAsync:{clusterId}:{namespaceName}";
            return Task.FromResult<RancherNamespace?>(new RancherNamespace());
        }

        public Task<RancherNamespace?> UpdateNamespaceProjectAsync(string clusterId, string namespaceName, string newProjectId, CancellationToken cancellationToken)
        {
            LastCall = $"UpdateNamespaceProjectAsync:{clusterId}:{namespaceName}:{newProjectId}";
            return Task.FromResult<RancherNamespace?>(new RancherNamespace());
        }

        public Task<bool> RemoveNamespaceFromProjectAsync(string clusterId, string namespaceName, CancellationToken cancellationToken)
        {
            LastCall = $"RemoveNamespaceFromProjectAsync:{clusterId}:{namespaceName}";
            return Task.FromResult(true);
        }

        public Task<List<RancherNamespace>> GetProjectNamespacesAsync(string projectId, CancellationToken cancellationToken)
        {
            LastCall = $"GetProjectNamespacesAsync:{projectId}";
            return Task.FromResult(new List<RancherNamespace>());
        }

        public Task<bool> DeleteNamespaceAsync(string clusterId, string namespaceName, CancellationToken cancellationToken)
        {
            LastCall = $"DeleteNamespaceAsync:{clusterId}:{namespaceName}";
            return Task.FromResult(true);
        }

        public Task<bool> EnsureNamespaceManagedByAsync(string clusterId, string namespaceName, bool createdByOperator, CancellationToken cancellationToken)
        {
            LastCall = $"EnsureNamespaceManagedByAsync:{clusterId}:{namespaceName}:{createdByOperator}";
            return Task.FromResult(true);
        }

        public Task<RancherProjectRoleBinding?> CreateProjectMemberAsync(string projectId, string principalId, string role, CancellationToken cancellationToken)
        {
            LastCall = $"CreateProjectMemberAsync:{projectId}:{principalId}:{role}";
            return Task.FromResult<RancherProjectRoleBinding?>(new RancherProjectRoleBinding());
        }

        public Task<List<RancherProjectRoleBinding>> GetProjectMembersAsync(string projectId, CancellationToken cancellationToken)
        {
            LastCall = $"GetProjectMembersAsync:{projectId}";
            return Task.FromResult(new List<RancherProjectRoleBinding>());
        }

        public Task<bool> DeleteProjectMemberAsync(string bindingId, CancellationToken cancellationToken)
        {
            LastCall = $"DeleteProjectMemberAsync:{bindingId}";
            return Task.FromResult(true);
        }

        public Task<RancherPrincipal?> GetPrincipalByNameAsync(string principalName, CancellationToken cancellationToken)
        {
            LastCall = $"GetPrincipalByNameAsync:{principalName}";
            return Task.FromResult<RancherPrincipal?>(new RancherPrincipal());
        }
    }
}
