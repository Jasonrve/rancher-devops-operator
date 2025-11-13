using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using rancher_devops_operator.Models;

namespace rancher_devops_operator.Services;

public interface IRancherApiService
{
    Task<string?> GetClusterIdByNameAsync(string clusterName, CancellationToken cancellationToken);
    Task<RancherProject?> CreateProjectAsync(string clusterId, string projectName, string? description, CancellationToken cancellationToken);
    Task<RancherProject?> GetProjectByNameAsync(string clusterId, string projectName, CancellationToken cancellationToken);
    Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken);
    Task<RancherNamespace?> CreateNamespaceAsync(string projectId, string namespaceName, CancellationToken cancellationToken);
    Task<List<RancherNamespace>> GetProjectNamespacesAsync(string projectId, CancellationToken cancellationToken);
    Task<bool> DeleteNamespaceAsync(string clusterId, string namespaceName, CancellationToken cancellationToken);
    Task<RancherProjectRoleBinding?> CreateProjectMemberAsync(string projectId, string principalId, string role, CancellationToken cancellationToken);
    Task<List<RancherProjectRoleBinding>> GetProjectMembersAsync(string projectId, CancellationToken cancellationToken);
    Task<bool> DeleteProjectMemberAsync(string bindingId, CancellationToken cancellationToken);
}

public class RancherApiService : IRancherApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RancherApiService> _logger;
    private readonly IRancherAuthService _authService;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        TypeInfoResolver = RancherJsonSerializerContext.Default
    };

    public RancherApiService(
        IHttpClientFactory httpClientFactory, 
        IRancherAuthService authService,
        ILogger<RancherApiService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Rancher");
        _authService = authService;
        _logger = logger;
        
        _authService.ConfigureHttpClient(_httpClient);
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        var token = await _authService.GetOrCreateTokenAsync(cancellationToken);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<string?> GetClusterIdByNameAsync(string clusterName, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var success = false;
        try
        {
            _logger.LogInformation("Fetching cluster ID for cluster name: {ClusterName}", clusterName);
            var response = await _httpClient.GetAsync("/v3/clusters", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var clusterList = JsonSerializer.Deserialize(content, RancherJsonSerializerContext.Default.RancherClusterList);

            var cluster = clusterList?.Data.FirstOrDefault(c => c.Name == clusterName);
            if (cluster == null)
            {
                _logger.LogWarning("Cluster not found: {ClusterName}", clusterName);
                return null;
            }

            _logger.LogInformation("Found cluster ID: {ClusterId} for name: {ClusterName}", cluster.Id, clusterName);
            success = true;
            return cluster.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching cluster ID for name: {ClusterName}", clusterName);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            MetricsService.RecordApiCall("get_cluster", success, stopwatch.Elapsed.TotalSeconds);
        }
    }

    public async Task<RancherProject?> CreateProjectAsync(string clusterId, string projectName, string? description, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Creating project {ProjectName} in cluster {ClusterId}", projectName, clusterId);

            var projectRequest = new RancherProjectRequest
            {
                Name = projectName,
                ClusterId = clusterId,
                Description = description
            };

            var json = JsonSerializer.Serialize(projectRequest, RancherJsonSerializerContext.Default.RancherProjectRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"/v3/projects", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var project = JsonSerializer.Deserialize(responseContent, RancherJsonSerializerContext.Default.RancherProject);

            _logger.LogInformation("Created project {ProjectName} with ID: {ProjectId}", projectName, project?.Id);
            return project;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating project {ProjectName} in cluster {ClusterId}", projectName, clusterId);
            throw;
        }
    }

    public async Task<RancherProject?> GetProjectByNameAsync(string clusterId, string projectName, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Fetching project {ProjectName} in cluster {ClusterId}", projectName, clusterId);
            var response = await _httpClient.GetAsync($"/v3/projects?clusterId={clusterId}", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var projectList = JsonSerializer.Deserialize(content, RancherJsonSerializerContext.Default.RancherProjectList);

            var project = projectList?.Data.FirstOrDefault(p => p.Name == projectName);
            return project;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching project {ProjectName} in cluster {ClusterId}", projectName, clusterId);
            throw;
        }
    }

    public async Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Deleting project {ProjectId}", projectId);
            var response = await _httpClient.DeleteAsync($"/v3/projects/{projectId}", cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Deleted project {ProjectId}", projectId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting project {ProjectId}", projectId);
            return false;
        }
    }

    public async Task<RancherNamespace?> CreateNamespaceAsync(string projectId, string namespaceName, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Creating namespace {NamespaceName} in project {ProjectId}", namespaceName, projectId);

            var namespaceRequest = new RancherNamespaceRequest
            {
                Name = namespaceName,
                ProjectId = projectId
            };

            var json = JsonSerializer.Serialize(namespaceRequest, RancherJsonSerializerContext.Default.RancherNamespaceRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/v3/cluster/namespaces", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var ns = JsonSerializer.Deserialize(responseContent, RancherJsonSerializerContext.Default.RancherNamespace);

            _logger.LogInformation("Created namespace {NamespaceName}", namespaceName);
            return ns;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating namespace {NamespaceName} in project {ProjectId}", namespaceName, projectId);
            throw;
        }
    }

    public async Task<List<RancherNamespace>> GetProjectNamespacesAsync(string projectId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Fetching namespaces for project {ProjectId}", projectId);
            var response = await _httpClient.GetAsync($"/v3/cluster/namespaces?projectId={projectId}", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var namespaceList = JsonSerializer.Deserialize(content, RancherJsonSerializerContext.Default.RancherNamespaceList);

            return namespaceList?.Data ?? new List<RancherNamespace>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching namespaces for project {ProjectId}", projectId);
            throw;
        }
    }

    public async Task<bool> DeleteNamespaceAsync(string clusterId, string namespaceName, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Deleting namespace {NamespaceName} in cluster {ClusterId}", namespaceName, clusterId);
            var response = await _httpClient.DeleteAsync($"/v3/clusters/{clusterId}/namespaces/{namespaceName}", cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Deleted namespace {NamespaceName}", namespaceName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting namespace {NamespaceName}", namespaceName);
            return false;
        }
    }

    public async Task<RancherProjectRoleBinding?> CreateProjectMemberAsync(string projectId, string principalId, string role, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Adding member {PrincipalId} with role {Role} to project {ProjectId}", principalId, role, projectId);

            var bindingRequest = new RancherProjectRoleBindingRequest
            {
                ProjectId = projectId,
                RoleTemplateId = role
            };

            // Determine if it's a user or group principal
            if (principalId.Contains("user", StringComparison.OrdinalIgnoreCase))
            {
                bindingRequest.UserPrincipalId = principalId;
            }
            else
            {
                bindingRequest.GroupPrincipalId = principalId;
            }

            var json = JsonSerializer.Serialize(bindingRequest, RancherJsonSerializerContext.Default.RancherProjectRoleBindingRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/v3/projectRoleTemplateBindings", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var binding = JsonSerializer.Deserialize(responseContent, RancherJsonSerializerContext.Default.RancherProjectRoleBinding);

            _logger.LogInformation("Added member {PrincipalId} to project {ProjectId}", principalId, projectId);
            return binding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding member {PrincipalId} to project {ProjectId}", principalId, projectId);
            throw;
        }
    }

    public async Task<List<RancherProjectRoleBinding>> GetProjectMembersAsync(string projectId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Fetching members for project {ProjectId}", projectId);
            var response = await _httpClient.GetAsync($"/v3/projectRoleTemplateBindings?projectId={projectId}", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var bindingList = JsonSerializer.Deserialize(content, RancherJsonSerializerContext.Default.RancherProjectRoleBindingList);

            return bindingList?.Data ?? new List<RancherProjectRoleBinding>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching members for project {ProjectId}", projectId);
            throw;
        }
    }

    public async Task<bool> DeleteProjectMemberAsync(string bindingId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Deleting project member binding {BindingId}", bindingId);
            var response = await _httpClient.DeleteAsync($"/v3/projectRoleTemplateBindings/{bindingId}", cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Deleted project member binding {BindingId}", bindingId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting project member binding {BindingId}", bindingId);
            return false;
        }
    }
}
