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
    private readonly TimeSpan _pollingInterval;
    private readonly string _observeMethod;
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
        
        // Check for new clusters every 5 minutes by default (supports both Rancher:* and flat keys)
        var intervalMinutes = configuration.GetValue<int>("Rancher:ClusterCheckInterval",
            configuration.GetValue<int>("ClusterCheckInterval", 5));
        _clusterCheckInterval = TimeSpan.FromMinutes(intervalMinutes);
        
        // Polling interval for poll mode (default 2 minutes)
        var pollMinutes = configuration.GetValue<int>("Rancher:PollingInterval",
            configuration.GetValue<int>("PollingInterval", 2));
        _pollingInterval = TimeSpan.FromMinutes(pollMinutes);
        
        // Observe method: "watch" (default), "poll", or "none"
        _observeMethod = (configuration.GetValue<string>("Rancher:ObserveMethod",
            configuration.GetValue<string>("ObserveMethod", "watch")) ?? "watch").ToLowerInvariant();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Rancher Namespace Observe Service starting (method: {Method}, cluster check: {ClusterInterval}min, polling: {PollInterval}min)", 
            _observeMethod, _clusterCheckInterval.TotalMinutes, _pollingInterval.TotalMinutes);

        // If observe method is 'none', skip all namespace watching
        if (_observeMethod == "none")
        {
            _logger.LogInformation("Observe method set to 'none' - namespace watching disabled");
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        // Wait a bit before first run to allow operator to start up
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_observeMethod == "poll")
                {
                    await CheckAndUpdateClusterWatchesAsync(stoppingToken);
                    await PollNamespacesAsync(stoppingToken);
                    await Task.Delay(_pollingInterval, stoppingToken);
                }
                else // watch mode
                {
                    await CheckAndUpdateClusterWatchesAsync(stoppingToken);
                    await Task.Delay(_clusterCheckInterval, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in observe service");
            }
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

        _logger.LogInformation("Rancher Namespace Observe Service stopped");
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

        _logger.LogDebug("Found {Count} cluster(s) to watch: {Clusters}", 
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

                // Only start watch task in watch mode, poll mode just needs the k8s client
                if (_observeMethod == "watch")
                {
                    _ = Task.Run(async () => await WatchClusterNamespacesAsync(clusterWatch, watchCts.Token), stoppingToken);
                }
                
                _logger.LogWarning("Cluster connected: {ClusterName} ({ClusterId}) - observe method: {Method}", 
                    clusterName, clusterId, _observeMethod);
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
                _logger.LogWarning("Cluster disconnected: {ClusterName} (no longer needed for observe)", clusterName);
                var watch = _activeWatches[clusterName];
                watch.CancellationTokenSource.Cancel();
                watch.CancellationTokenSource.Dispose();
                watch.K8sClient.Dispose();
                _activeWatches.Remove(clusterName);
            }
        }
    }

    private async Task PollNamespacesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Polling namespaces across all clusters");

        // Get all projects with Observe policy
        var projects = await GetObserveProjectsAsync(cancellationToken);
        if (!projects.Any())
        {
            _logger.LogDebug("No projects with Observe policy found for polling");
            return;
        }

        // Group by cluster
        var projectsByCluster = projects.GroupBy(p => p.Spec.ClusterName);

        foreach (var clusterGroup in projectsByCluster)
        {
            var clusterName = clusterGroup.Key;
            
            ClusterWatch? clusterWatch;
            lock (_watchesLock)
            {
                if (!_activeWatches.TryGetValue(clusterName, out clusterWatch))
                {
                    _logger.LogWarning("No active connection for cluster {ClusterName}, skipping poll", clusterName);
                    continue;
                }
            }

            try
            {
                // List all namespaces in this cluster
                var namespaces = await clusterWatch.K8sClient.CoreV1.ListNamespaceAsync(cancellationToken: cancellationToken);
                
                foreach (var ns in namespaces.Items)
                {
                    await ProcessNamespaceForProjectsAsync(clusterName, ns, clusterGroup.ToList(), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling namespaces for cluster {ClusterName}", clusterName);
            }
        }
    }

    private async Task<List<V1Project>> GetObserveProjectsAsync(CancellationToken cancellationToken)
    {
        var allProjects = await _kubernetesClient.ListAsync<V1Project>(cancellationToken: cancellationToken);
        return allProjects.Where(p => 
        {
            if (p.Spec.ManagementPolicies == null || p.Spec.ManagementPolicies.Count == 0)
                return false;
            return p.Spec.ManagementPolicies.Any(policy => 
                string.Equals(policy, "Observe", StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    private async Task WatchClusterNamespacesAsync(ClusterWatch clusterWatch, CancellationToken cancellationToken)
    {
        var reconnectDelay = TimeSpan.FromSeconds(5);
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Starting namespace watch for cluster {ClusterName}", clusterWatch.ClusterName);
                
                // Watch all namespace events in this cluster
                var watcher = clusterWatch.K8sClient.CoreV1.ListNamespaceWithHttpMessagesAsync(
                    watch: true,
                    cancellationToken: cancellationToken);

                await foreach (var (type, item) in watcher.WatchAsync<V1Namespace, V1NamespaceList>(cancellationToken: cancellationToken))
                {
                    // Only process Added and Modified events
                    if (type == WatchEventType.Added || type == WatchEventType.Modified)
                    {
                        await HandleNamespaceEventAsync(clusterWatch.ClusterName, item, cancellationToken);
                    }
                }
                
                _logger.LogWarning("Namespace watch ended for cluster {ClusterName}, will reconnect", clusterWatch.ClusterName);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Watch cancelled for cluster {ClusterName}", clusterWatch.ClusterName);
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

    private async Task HandleNamespaceEventAsync(string clusterName, V1Namespace ns, CancellationToken cancellationToken)
    {
        try
        {
            // Get all projects with Observe policy for this cluster first
            var projects = await GetObserveProjectsAsync(cancellationToken);
            var relevantProjects = projects
                .Where(p => string.Equals(p.Spec.ClusterName, clusterName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!relevantProjects.Any())
            {
                // No CRDs care about this cluster, skip
                return;
            }

            await ProcessNamespaceForProjectsAsync(clusterName, ns, relevantProjects, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling namespace watch event");
        }
    }

    /// <summary>
    /// Shared method to process a namespace and update relevant project CRDs
    /// Used by both watch and poll methods
    /// </summary>
    private async Task ProcessNamespaceForProjectsAsync(
        string clusterName, 
        V1Namespace ns, 
        List<V1Project> relevantProjects,
        CancellationToken cancellationToken)
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

        // Skip if no project assignment yet
        if (string.IsNullOrEmpty(projectId))
        {
            return;
        }

        // Find the Project CRD that manages this Rancher project
        V1Project? targetProject = null;
        foreach (var project in relevantProjects)
        {
            // Check if this CRD manages the project with the matching projectId
            if (project.Status?.ProjectId != projectId)
            {
                continue;
            }

            // Check if namespace is already in the CRD spec - if so, skip silently
            if (project.Spec.Namespaces.Any(n => n.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            targetProject = project;
            break;
        }

        if (targetProject == null)
        {
            // No CRD manages this namespace's project
            return;
        }

        // Add namespace to the CRD
        await AddNamespaceToCrdAsync(clusterName, namespaceName, targetProject, cancellationToken);
    }

    /// <summary>
    /// Shared method to add a namespace to a CRD spec with retry logic
    /// </summary>
    private async Task AddNamespaceToCrdAsync(
        string clusterName,
        string namespaceName, 
        V1Project targetProject, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Namespace discovered [{ClusterName}]: {Namespace} will be added to project {ProjectName}", 
            clusterName, namespaceName, targetProject.Metadata.Name);

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
                var eventMessage = _observeMethod == "poll" 
                    ? $"Discovered and added namespace '{namespaceName}' to project via polling"
                    : $"Discovered and added namespace '{namespaceName}' to project via watch";
                    
                await _eventService.CreateEventAsync(
                    targetProject,
                    "NamespaceDiscovered",
                    eventMessage,
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
                var refetchedProject = await _kubernetesClient.GetAsync<V1Project>(targetProject.Metadata.Name, cancellationToken: cancellationToken);
                if (refetchedProject == null)
                {
                    _logger.LogError("Failed to refetch CRD {Name} for retry", targetProject.Metadata.Name);
                    break;
                }
                
                targetProject = refetchedProject;
                
                // Check if another process already added it
                if (targetProject.Spec.Namespaces.Any(n => n.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("Namespace {Namespace} was already added by another process", namespaceName);
                    break;
                }
            }
        }
    }
}
