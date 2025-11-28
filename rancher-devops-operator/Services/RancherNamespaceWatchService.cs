using System.Text.Json;
using k8s;
using k8s.Models;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using rancher_devops_operator.Entities;

namespace rancher_devops_operator.Services;

/// <summary>
/// Background service that watches for namespace changes in downstream Rancher clusters
/// Uses Rancher-generated kubeconfigs to establish direct watches to each cluster
/// </summary>
public class RancherNamespaceWatchService : BackgroundService
{
    private readonly ILogger<RancherNamespaceWatchService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IKubernetesClient _kubernetesClient;
    private readonly IRancherApiService _rancherApi;
    private readonly IKubernetesEventService _eventService;
    private readonly TimeSpan _clusterCheckInterval;
    private readonly Dictionary<string, ClusterWatch> _activeWatches = new();
    private readonly object _watchesLock = new();

    private class ClusterWatch
    {
        public required string ClusterName { get; init; }
        public required string ClusterId { get; init; }
        public required IKubernetes K8sClient { get; init; }
        public required CancellationTokenSource CancellationTokenSource { get; init; }
    }

    public RancherNamespaceWatchService(
        ILogger<RancherNamespaceWatchService> logger,
        IConfiguration configuration,
        IKubernetesClient kubernetesClient,
        IRancherApiService rancherApi,
        IKubernetesEventService eventService)
    {
        _logger = logger;
        _configuration = configuration;
        _kubernetesClient = kubernetesClient;
        _rancherApi = rancherApi;
        _eventService = eventService;
        
        // Check for new clusters every 5 minutes by default
        var intervalMinutes = configuration.GetValue<int>("Rancher:ClusterCheckInterval", 5);
        _clusterCheckInterval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Rancher Namespace Watch Service starting (checking clusters every {Interval} minutes)", 
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
            foreach (var watch in _activeWatches.Values)
            {
                watch.CancellationTokenSource.Cancel();
                watch.CancellationTokenSource.Dispose();
                watch.K8sClient.Dispose();
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

            // Get kubeconfig for this cluster
            var kubeconfig = await _rancherApi.GetClusterKubeconfigAsync(clusterId, stoppingToken);
            if (string.IsNullOrEmpty(kubeconfig))
            {
                _logger.LogWarning("Could not get kubeconfig for cluster {ClusterName}", clusterName);
                continue;
            }

            try
            {
                // Create Kubernetes client for this cluster using kubeconfig content
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(kubeconfig));
                var k8sConfig = KubernetesClientConfiguration.BuildConfigFromConfigFile(stream);
                var k8sClient = new Kubernetes(k8sConfig);

                // Start watch for this cluster
                var watchCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var clusterWatch = new ClusterWatch
                {
                    ClusterName = clusterName,
                    ClusterId = clusterId,
                    K8sClient = k8sClient,
                    CancellationTokenSource = watchCts
                };

                lock (_watchesLock)
                {
                    _activeWatches[clusterName] = clusterWatch;
                }

                _ = Task.Run(async () => await WatchClusterNamespacesAsync(clusterWatch, watchCts.Token), stoppingToken);
                _logger.LogInformation("Started namespace watch for cluster {ClusterName} ({ClusterId})", clusterName, clusterId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Kubernetes client for cluster {ClusterName}", clusterName);
            }
        }

        // Stop watches for clusters no longer needed
        lock (_watchesLock)
        {
            var clustersToRemove = _activeWatches.Keys.Except(clusterNames).ToList();
            foreach (var clusterName in clustersToRemove)
            {
                _logger.LogInformation("Stopping watch for cluster {ClusterName} (no longer needed)", clusterName);
                var watch = _activeWatches[clusterName];
                watch.CancellationTokenSource.Cancel();
                watch.CancellationTokenSource.Dispose();
                watch.K8sClient.Dispose();
                _activeWatches.Remove(clusterName);
            }
        }
    }

    private async Task WatchClusterNamespacesAsync(ClusterWatch clusterWatch, CancellationToken cancellationToken)
    {
        var reconnectDelay = TimeSpan.FromSeconds(5);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting namespace watch for cluster {ClusterName}", clusterWatch.ClusterName);
                
                // Watch all namespace events in this cluster
                var watcher = clusterWatch.K8sClient.CoreV1.ListNamespaceWithHttpMessagesAsync(
                    watch: true,
                    cancellationToken: cancellationToken);

                await foreach (var (type, item) in watcher.WatchAsync<V1Namespace, V1NamespaceList>(cancellationToken: cancellationToken))
                {
                    await HandleNamespaceEventAsync(clusterWatch.ClusterName, type, item, cancellationToken);
                }
                
                _logger.LogWarning("Namespace watch ended for cluster {ClusterName}, will reconnect", clusterWatch.ClusterName);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Watch cancelled for cluster {ClusterName}", clusterWatch.ClusterName);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in watch for cluster {ClusterName}. Reconnecting in {Delay}s", 
                    clusterWatch.ClusterName, reconnectDelay.TotalSeconds);
                
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(reconnectDelay, cancellationToken);
                }
            }
        }
    }

    private async Task HandleNamespaceEventAsync(string clusterName, WatchEventType eventType, V1Namespace ns, CancellationToken cancellationToken)
    {
        try
        {
            var namespaceName = ns.Metadata?.Name;
            if (string.IsNullOrEmpty(namespaceName))
            {
                return;
            }

            // Get project ID from Rancher annotation
            string? projectId = null;
            if (ns.Metadata?.Annotations != null && 
                ns.Metadata.Annotations.TryGetValue("field.cattle.io/projectId", out var projId))
            {
                projectId = projId;
            }

            _logger.LogInformation("Namespace event [{ClusterName}]: {EventType} - {Namespace} (projectId: {ProjectId})", 
                clusterName, eventType, namespaceName, projectId ?? "none");

            // Handle both Added and Modified events to catch when projectId annotation is added later
            if (eventType != WatchEventType.Added && eventType != WatchEventType.Modified)
            {
                _logger.LogDebug("Ignoring {EventType} event for namespace {Namespace}", eventType, namespaceName);
                return;
            }

            // Skip if no project assignment yet
            if (string.IsNullOrEmpty(projectId))
            {
                _logger.LogDebug("Namespace {Namespace} does not have projectId annotation yet, skipping", namespaceName);
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
                if (project.Spec.Namespaces.Any(n => n.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)))
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
                        $"Discovered and added namespace '{namespaceName}' to project via native Kubernetes Watch",
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
                    
                    // Wait before retry with exponential backoff
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                    
                    // Refetch the latest version
                    targetProject = await _kubernetesClient.GetAsync<V1Project>(targetProject.Metadata.Name, cancellationToken: cancellationToken);
                    if (targetProject == null)
                    {
                        _logger.LogError("Failed to refetch CRD {Name} for retry", targetProject?.Metadata.Name);
                        break;
                    }
                    
                    // Check if another process already added it
                    if (targetProject.Spec.Namespaces.Any(n => n.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation("Namespace {Namespace} was already added by another process", namespaceName);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling namespace watch event");
        }
    }
}
