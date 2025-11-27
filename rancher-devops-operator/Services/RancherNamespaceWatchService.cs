using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using rancher_devops_operator.Entities;

namespace rancher_devops_operator.Services;

/// <summary>
/// Background service that watches for namespace changes in Rancher clusters using Kubernetes Watch API
/// Periodically checks for new clusters and establishes watches for each
/// </summary>
public class RancherNamespaceWatchService : BackgroundService
{
    private readonly ILogger<RancherNamespaceWatchService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IKubernetesClient _kubernetesClient;
    private readonly IRancherApiService _rancherApi;
    private readonly IRancherAuthService _authService;
    private readonly IKubernetesEventService _eventService;
    private readonly string _rancherUrl;
    private readonly TimeSpan _clusterCheckInterval;
    private readonly Dictionary<string, CancellationTokenSource> _activeWatches = new();
    private readonly object _watchesLock = new();

    public RancherNamespaceWatchService(
        ILogger<RancherNamespaceWatchService> logger,
        IConfiguration configuration,
        IKubernetesClient kubernetesClient,
        IRancherApiService rancherApi,
        IRancherAuthService authService,
        IKubernetesEventService eventService)
    {
        _logger = logger;
        _configuration = configuration;
        _kubernetesClient = kubernetesClient;
        _rancherApi = rancherApi;
        _authService = authService;
        _eventService = eventService;
        _rancherUrl = configuration["Rancher:Url"] ?? "https://rancher.local";
        
        // Check for new clusters every 5 minutes by default
        var intervalMinutes = configuration.GetValue<int>("Rancher:ClusterCheckInterval", 5);
        _clusterCheckInterval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Rancher Namespace Watch Service starting (cluster check interval: {Interval} minutes)", 
            _clusterCheckInterval.TotalMinutes);

        // Wait a bit before first run to allow operator to start up
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndUpdateClusterWatchesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking clusters");
            }

            await Task.Delay(_clusterCheckInterval, stoppingToken);
        }

        // Cleanup all watches on shutdown
        lock (_watchesLock)
        {
            foreach (var cts in _activeWatches.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _activeWatches.Clear();
        }

        _logger.LogInformation("Rancher Namespace Watch Service stopped");
    }

    private async Task CheckAndUpdateClusterWatchesAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Checking for clusters to watch");

        // Get all Project CRDs with Observe policy
        var allProjects = await _kubernetesClient.ListAsync<V1Project>(cancellationToken: stoppingToken);
        var observeProjects = allProjects.Where(p => 
        {
            if (p.Spec.ManagementPolicies == null || p.Spec.ManagementPolicies.Count == 0)
                return false;
            return p.Spec.ManagementPolicies.Any(policy => 
                string.Equals(policy, "Observe", StringComparison.OrdinalIgnoreCase));
        }).ToList();

        if (!observeProjects.Any())
        {
            _logger.LogDebug("No projects with Observe policy found");
            return;
        }

        // Get unique cluster names from projects with Observe policy
        var clusterNames = observeProjects
            .Select(p => p.Spec.ClusterName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .ToList();

        _logger.LogInformation("Found {Count} cluster(s) to watch: {Clusters}", 
            clusterNames.Count, string.Join(", ", clusterNames));

        // Start watches for new clusters
        foreach (var clusterName in clusterNames)
        {
            lock (_watchesLock)
            {
                if (_activeWatches.ContainsKey(clusterName))
                {
                    _logger.LogDebug("Watch already active for cluster {ClusterName}", clusterName);
                    continue;
                }
            }

            // Get cluster ID
            var clusterId = await _rancherApi.GetClusterIdByNameAsync(clusterName, stoppingToken);
            if (string.IsNullOrEmpty(clusterId))
            {
                _logger.LogWarning("Cluster {ClusterName} not found in Rancher", clusterName);
                continue;
            }

            // Start watch for this cluster
            var watchCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            lock (_watchesLock)
            {
                _activeWatches[clusterName] = watchCts;
            }

            _ = Task.Run(async () => await WatchClusterNamespacesAsync(clusterName, clusterId, watchCts.Token), stoppingToken);
            _logger.LogInformation("Started namespace watch for cluster {ClusterName} ({ClusterId})", clusterName, clusterId);
        }

        // Stop watches for clusters no longer needed
        lock (_watchesLock)
        {
            var clustersToRemove = _activeWatches.Keys.Except(clusterNames).ToList();
            foreach (var clusterName in clustersToRemove)
            {
                _logger.LogInformation("Stopping watch for cluster {ClusterName} (no longer needed)", clusterName);
                _activeWatches[clusterName].Cancel();
                _activeWatches[clusterName].Dispose();
                _activeWatches.Remove(clusterName);
            }
        }
    }

    private async Task WatchClusterNamespacesAsync(string clusterName, string clusterId, CancellationToken cancellationToken)
    {
        var reconnectDelay = TimeSpan.FromSeconds(5);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? webSocket = null;
            try
            {
                webSocket = new ClientWebSocket();

                // Get authentication token
                var token = await _authService.GetOrCreateTokenAsync(cancellationToken);
                var tokenBytes = Encoding.UTF8.GetBytes(token);
                var base64Token = Convert.ToBase64String(tokenBytes);
                webSocket.Options.SetRequestHeader("Authorization", $"Basic {base64Token}");

                // Allow insecure SSL if configured
                var allowInsecure = _configuration.GetValue<bool>("Rancher:AllowInsecureSsl", false);
                if (allowInsecure)
                {
                    webSocket.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true;
                }

                // Build Kubernetes Watch API URL through Rancher proxy
                // Format: wss://rancher-url/k8s/clusters/{clusterId}/v1/namespaces?watch=true
                var wsUrl = _rancherUrl.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://");
                var watchUrl = $"{wsUrl}/k8s/clusters/{clusterId}/v1/namespaces?watch=true";

                _logger.LogInformation("Connecting to Kubernetes Watch API for cluster {ClusterName}: {Url}", clusterName, watchUrl);
                
                try
                {
                    await webSocket.ConnectAsync(new Uri(watchUrl), cancellationToken);
                    _logger.LogInformation("Connected to watch for cluster {ClusterName}", clusterName);
                }
                catch (WebSocketException wsEx)
                {
                    _logger.LogError(wsEx, "WebSocket connection failed for cluster {ClusterName}. Status: {Status}, Message: {Message}", 
                        clusterName, wsEx.WebSocketErrorCode, wsEx.Message);
                    throw;
                }

                // Listen for watch events
                var buffer = new byte[16384]; // 16KB buffer for watch events
                var messageBuilder = new StringBuilder();

                while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("Watch closed by server for cluster {ClusterName}", clusterName);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        messageBuilder.Append(chunk);
                        
                        if (result.EndOfMessage)
                        {
                            var completeMessage = messageBuilder.ToString();
                            messageBuilder.Clear();
                            
                            await HandleWatchEventAsync(clusterName, completeMessage, cancellationToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Watch for cluster {ClusterName} cancelled", clusterName);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in watch for cluster {ClusterName}. Reconnecting in {Delay}s", 
                    clusterName, reconnectDelay.TotalSeconds);
            }
            finally
            {
                webSocket?.Dispose();
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(reconnectDelay, cancellationToken);
            }
        }

        _logger.LogInformation("Watch stopped for cluster {ClusterName}", clusterName);
    }

    private async Task HandleWatchEventAsync(string clusterName, string json, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var eventType = typeElement.GetString(); // ADDED, MODIFIED, DELETED
            
            if (!root.TryGetProperty("object", out var objectElement))
            {
                return;
            }

            if (!objectElement.TryGetProperty("metadata", out var metadata))
            {
                return;
            }

            if (!metadata.TryGetProperty("name", out var nameElement))
            {
                return;
            }

            var namespaceName = nameElement.GetString();
            
            // Get project ID from annotations
            string? projectId = null;
            if (metadata.TryGetProperty("annotations", out var annotations))
            {
                if (annotations.TryGetProperty("field.cattle.io/projectId", out var projectIdElement))
                {
                    projectId = projectIdElement.GetString();
                }
            }

            _logger.LogInformation("Namespace watch event [{ClusterName}]: {EventType} - {Namespace} (projectId: {ProjectId})", 
                clusterName, eventType, namespaceName, projectId ?? "none");

            // Only handle ADDED events for namespaces with a project assignment
            if (!string.Equals(eventType, "ADDED", StringComparison.OrdinalIgnoreCase) || 
                string.IsNullOrEmpty(projectId) || 
                string.IsNullOrEmpty(namespaceName))
            {
                return;
            }

            // Find the Project CRD that manages this Rancher project
            var allProjects = await _kubernetesClient.ListAsync<V1Project>(cancellationToken: cancellationToken);
            
            V1Project? targetProject = null;
            foreach (var project in allProjects)
            {
                // Check if this CRD manages the project, is in the right cluster, and has Observe enabled
                if (project.Status?.ProjectId != projectId || 
                    !string.Equals(project.Spec.ClusterName, clusterName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var allowObserve = (project.Spec.ManagementPolicies == null || project.Spec.ManagementPolicies.Count == 0)
                    ? false
                    : project.Spec.ManagementPolicies.Any(p => string.Equals(p, "Observe", StringComparison.OrdinalIgnoreCase));

                if (!allowObserve)
                {
                    continue;
                }

                // Check if namespace is already in the CRD spec
                if (project.Spec.Namespaces.Any(ns => ns.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogDebug("Namespace {Namespace} already exists in CRD {Name} spec", 
                        namespaceName, project.Metadata.Name);
                    continue;
                }

                targetProject = project;
                break;
            }

            if (targetProject == null)
            {
                return;
            }

            // Add the namespace to the CRD spec with retry logic
            _logger.LogInformation("Adding namespace {Namespace} to CRD {Name} spec (Observe enabled)", 
                namespaceName, targetProject.Metadata.Name);
            
            var maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    targetProject.Spec.Namespaces.Add(namespaceName);
                    await _kubernetesClient.UpdateAsync(targetProject, cancellationToken);
                    
                    _logger.LogInformation("Successfully updated CRD {Name} with new namespace {Namespace}", 
                        targetProject.Metadata.Name, namespaceName);

                    // Create Kubernetes event for successful update
                    await _eventService.CreateEventAsync(
                        targetProject,
                        "NamespaceDiscovered",
                        $"Discovered and added namespace '{namespaceName}' to project via Kubernetes Watch",
                        "Normal",
                        cancellationToken);
                    
                    break;
                }
                catch (Exception ex) when (ex.Message.Contains("409") || ex.Message.Contains("Conflict"))
                {
                    if (attempt == maxRetries)
                    {
                        _logger.LogError(ex, "Failed to update CRD {Name} after {Retries} attempts due to conflicts", 
                            targetProject.Metadata.Name, maxRetries);
                        throw;
                    }
                    
                    _logger.LogWarning("Conflict updating CRD {Name} (attempt {Attempt}/{Max}), refetching and retrying", 
                        targetProject.Metadata.Name, attempt, maxRetries);
                    
                    // Refetch the latest version
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                    var latestProject = await _kubernetesClient.GetAsync<V1Project>(targetProject.Metadata.Name, cancellationToken: cancellationToken);
                    
                    if (latestProject == null)
                    {
                        _logger.LogWarning("Project {Name} no longer exists", targetProject.Metadata.Name);
                        return;
                    }
                    
                    // Check again if namespace was already added by another process
                    if (latestProject.Spec.Namespaces.Any(ns => ns.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation("Namespace {Namespace} was already added to CRD {Name} by another process", 
                            namespaceName, targetProject.Metadata.Name);
                        return;
                    }
                    
                    // Use the latest version for next retry
                    targetProject = latestProject;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling watch event for cluster {ClusterName}: {Json}", clusterName, json);
        }
    }
}
