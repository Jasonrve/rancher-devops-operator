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
    Task<RancherNamespace?> GetNamespaceAsync(string clusterId, string namespaceName, CancellationToken cancellationToken);
    Task<RancherNamespace?> UpdateNamespaceProjectAsync(string clusterId, string namespaceName, string newProjectId, CancellationToken cancellationToken);
    Task<bool> RemoveNamespaceFromProjectAsync(string clusterId, string namespaceName, CancellationToken cancellationToken);
    Task<List<RancherNamespace>> GetProjectNamespacesAsync(string projectId, CancellationToken cancellationToken);
    Task<bool> DeleteNamespaceAsync(string clusterId, string namespaceName, CancellationToken cancellationToken);
    Task<RancherProjectRoleBinding?> CreateProjectMemberAsync(string projectId, string principalId, string role, CancellationToken cancellationToken);
    Task<List<RancherProjectRoleBinding>> GetProjectMembersAsync(string projectId, CancellationToken cancellationToken);
    Task<bool> DeleteProjectMemberAsync(string bindingId, CancellationToken cancellationToken);
    Task<string?> GetPrincipalIdByNameAsync(string principalName, CancellationToken cancellationToken);
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
    private const string ManagedByKey = "app.kubernetes.io/managed-by";
    private const string ManagedByValue = "rancher-devops-operator";

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
                Description = description,
                Annotations = new Dictionary<string, string>
                {
                    [ManagedByKey] = ManagedByValue
                }
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
            // Verify annotations to ensure we only delete resources managed by this operator
            var getResponse = await _httpClient.GetAsync($"/v3/projects/{projectId}", cancellationToken);
            if (!getResponse.IsSuccessStatusCode)
            {
                var body = await getResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Cannot verify ownership for project {ProjectId}. Skipping delete. Status {Status} Body: {Body}", projectId, (int)getResponse.StatusCode, body);
                return false;
            }
            var getJson = await getResponse.Content.ReadAsStringAsync(cancellationToken);
            var existing = JsonSerializer.Deserialize(getJson, RancherJsonSerializerContext.Default.RancherProject);
            if (existing?.Annotations == null || !existing.Annotations.TryGetValue(ManagedByKey, out var val) || !string.Equals(val, ManagedByValue, StringComparison.Ordinal))
            {
                _logger.LogWarning("Project {ProjectId} is not managed by this operator. Skipping delete.", projectId);
                return false;
            }
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
            // Enforce lowercase RFC 1123 compliant name
            namespaceName = namespaceName.ToLowerInvariant();
            // Derive clusterId from compound projectId (format: clusterId:projectId)
            var clusterIdPart = projectId.Contains(':') ? projectId.Split(':')[0] : string.Empty;
            if (string.IsNullOrEmpty(clusterIdPart))
            {
                _logger.LogWarning("Could not derive clusterId from projectId {ProjectId}", projectId);
            }

            var namespaceRequest = new RancherNamespaceRequest
            {
                Name = namespaceName,
                ProjectId = projectId,
                Annotations = new Dictionary<string, string>
                {
                    [ManagedByKey] = ManagedByValue
                },
                Labels = new Dictionary<string, string>
                {
                    [ManagedByKey] = ManagedByValue
                }
            };

            var json = JsonSerializer.Serialize(namespaceRequest, RancherJsonSerializerContext.Default.RancherNamespaceRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Rancher endpoint: /v3/clusters/{clusterId}/namespaces
            var response = await _httpClient.PostAsync($"/v3/clusters/{clusterIdPart}/namespaces", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Namespace create failed for {NamespaceName} (project {ProjectId}) status {Status}: {Body}", namespaceName, projectId, (int)response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }

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
            var clusterIdPart = projectId.Contains(':') ? projectId.Split(':')[0] : string.Empty;
            var encodedProjectId = Uri.EscapeDataString(projectId);
            // Correct Rancher list endpoint includes cluster path and projectId filter
            var response = await _httpClient.GetAsync($"/v3/clusters/{clusterIdPart}/namespaces?projectId={encodedProjectId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Namespace list failed for project {ProjectId} status {Status}: {Body}", projectId, (int)response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }

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

    public async Task<RancherNamespace?> GetNamespaceAsync(string clusterId, string namespaceName, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Fetching namespace {NamespaceName} in cluster {ClusterId}", namespaceName, clusterId);
            var response = await _httpClient.GetAsync($"/v3/clusters/{clusterId}/namespaces/{namespaceName}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Namespace get failed for {NamespaceName} status {Status}: {Body}", namespaceName, (int)response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var ns = JsonSerializer.Deserialize(content, RancherJsonSerializerContext.Default.RancherNamespace);
            return ns;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching namespace {NamespaceName}", namespaceName);
            throw;
        }
    }

    public async Task<RancherNamespace?> UpdateNamespaceProjectAsync(string clusterId, string namespaceName, string newProjectId, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Updating namespace {NamespaceName} to project {ProjectId}", namespaceName, newProjectId);
            
            // Fetch existing namespace first
            var existing = await GetNamespaceAsync(clusterId, namespaceName, cancellationToken);
            if (existing == null)
            {
                _logger.LogWarning("Namespace {NamespaceName} not found", namespaceName);
                return null;
            }

            // Update with new projectId, preserving labels
            var updateRequest = new RancherNamespaceRequest
            {
                Name = namespaceName,
                ProjectId = newProjectId,
                Labels = existing.Labels ?? new Dictionary<string, string>()
            };

            // Ensure managed-by label is set
            updateRequest.Labels[ManagedByKey] = ManagedByValue;

            var json = JsonSerializer.Serialize(updateRequest, RancherJsonSerializerContext.Default.RancherNamespaceRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync($"/v3/clusters/{clusterId}/namespaces/{namespaceName}", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Namespace update failed for {NamespaceName} status {Status}: {Body}", namespaceName, (int)response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var updated = JsonSerializer.Deserialize(responseContent, RancherJsonSerializerContext.Default.RancherNamespace);
            _logger.LogInformation("Updated namespace {NamespaceName} to project {ProjectId}", namespaceName, newProjectId);
            return updated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating namespace {NamespaceName}", namespaceName);
            throw;
        }
    }

    public async Task<bool> RemoveNamespaceFromProjectAsync(string clusterId, string namespaceName, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Removing namespace {NamespaceName} from its project", namespaceName);
            
            var existing = await GetNamespaceAsync(clusterId, namespaceName, cancellationToken);
            if (existing == null)
            {
                _logger.LogWarning("Namespace {NamespaceName} not found", namespaceName);
                return false;
            }

            // Check if it's managed by us
            if (existing.Labels == null || !existing.Labels.TryGetValue(ManagedByKey, out var val) || !string.Equals(val, ManagedByValue, StringComparison.Ordinal))
            {
                _logger.LogWarning("Namespace {NamespaceName} is not managed by this operator. Cannot remove from project.", namespaceName);
                return false;
            }

            // Update with empty/null projectId to disassociate
            var updateRequest = new RancherNamespaceRequest
            {
                Name = namespaceName,
                ProjectId = string.Empty,
                Labels = existing.Labels
            };

            var json = JsonSerializer.Serialize(updateRequest, RancherJsonSerializerContext.Default.RancherNamespaceRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync($"/v3/clusters/{clusterId}/namespaces/{namespaceName}", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Namespace project removal failed for {NamespaceName} status {Status}: {Body}", namespaceName, (int)response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode();
            }

            _logger.LogInformation("Removed namespace {NamespaceName} from project", namespaceName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing namespace {NamespaceName} from project", namespaceName);
            return false;
        }
    }

    public async Task<bool> DeleteNamespaceAsync(string clusterId, string namespaceName, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Deleting namespace {NamespaceName} in cluster {ClusterId}", namespaceName, clusterId);
            // Verify annotations to ensure we only delete namespaces managed by this operator
            var getResponse = await _httpClient.GetAsync($"/v3/clusters/{clusterId}/namespaces/{namespaceName}", cancellationToken);
            if (!getResponse.IsSuccessStatusCode)
            {
                var body = await getResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Cannot verify ownership for namespace {Namespace}. Skipping delete. Status {Status} Body: {Body}", namespaceName, (int)getResponse.StatusCode, body);
                return false;
            }
            var getJson = await getResponse.Content.ReadAsStringAsync(cancellationToken);
            var existing = JsonSerializer.Deserialize(getJson, RancherJsonSerializerContext.Default.RancherNamespace);
            if (existing?.Annotations == null || !existing.Annotations.TryGetValue(ManagedByKey, out var val) || !string.Equals(val, ManagedByValue, StringComparison.Ordinal))
            {
                _logger.LogWarning("Namespace {Namespace} is not managed by this operator. Skipping delete.", namespaceName);
                return false;
            }
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

    public async Task<string?> GetPrincipalIdByNameAsync(string principalName, CancellationToken cancellationToken)
    {
        await EnsureAuthenticatedAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Resolving principal name {PrincipalName}", principalName);
            var encoded = Uri.EscapeDataString(principalName);
            var response = await _httpClient.GetAsync($"/v3/principals?name={encoded}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to lookup principal {PrincipalName} status {Status}: {Body}", principalName, (int)response.StatusCode, body);
                return null;
            }
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = JsonSerializer.Deserialize(content, RancherJsonSerializerContext.Default.RancherPrincipalList);
            var match = list?.Data.FirstOrDefault(p => p.Name.Equals(principalName, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                _logger.LogWarning("Principal name {PrincipalName} not found", principalName);
                return null;
            }
            _logger.LogInformation("Resolved principal name {PrincipalName} to ID {PrincipalId}", principalName, match.Id);
            return match.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving principal name {PrincipalName}", principalName);
            return null;
        }
    }
}
