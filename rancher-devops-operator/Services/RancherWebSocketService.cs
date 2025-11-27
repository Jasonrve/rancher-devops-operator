using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using rancher_devops_operator.Entities;

namespace rancher_devops_operator.Services;

public class RancherWebSocketService : BackgroundService
{
    private readonly ILogger<RancherWebSocketService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IKubernetesClient _kubernetesClient;
    private readonly IRancherAuthService _authService;
    private readonly IKubernetesEventService _eventService;
    private ClientWebSocket? _webSocket;
    private readonly string _rancherUrl;
    private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);

    public RancherWebSocketService(
        ILogger<RancherWebSocketService> logger,
        IConfiguration configuration,
        IKubernetesClient kubernetesClient,
        IRancherAuthService authService,
        IKubernetesEventService eventService)
    {
        _logger = logger;
        _configuration = configuration;
        _kubernetesClient = kubernetesClient;
        _authService = authService;
        _eventService = eventService;
        _rancherUrl = configuration["Rancher:Url"] ?? "https://rancher.local";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Rancher WebSocket Service starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndListenAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket connection error. Reconnecting in {Delay}s", _reconnectDelay.TotalSeconds);
                await Task.Delay(_reconnectDelay, stoppingToken);
            }
        }

        _logger.LogInformation("Rancher WebSocket Service stopping...");
    }

    private async Task ConnectAndListenAsync(CancellationToken stoppingToken)
    {
        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();

        // Get authentication token
        var token = await _authService.GetOrCreateTokenAsync(stoppingToken);
        
        // Rancher WebSocket API requires token in Authorization header as Basic auth
        // Token format is "token-xxxxx:yyyyyy" which is username:password
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var base64Token = Convert.ToBase64String(tokenBytes);
        _webSocket.Options.SetRequestHeader("Authorization", $"Basic {base64Token}");
        
        // Allow insecure SSL if configured
        var allowInsecure = _configuration.GetValue<bool>("Rancher:AllowInsecureSsl", false);
        if (allowInsecure)
        {
            _webSocket.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true;
        }

        // Build WebSocket URL - subscribe to namespace resource changes globally (all clusters)
        // Using /v3/subscribe for global-level subscription across all clusters
        var wsUrl = _rancherUrl.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://");
        var subscribeUrl = $"{wsUrl}/v3/subscribe?eventNames=resource.change&resourceType=namespace";

        _logger.LogInformation("Connecting to Rancher WebSocket (global subscription for all clusters): {Url}", subscribeUrl);
        
        try
        {
            await _webSocket.ConnectAsync(new Uri(subscribeUrl), stoppingToken);
            _logger.LogInformation("Connected to Rancher WebSocket");
        }
        catch (WebSocketException wsEx)
        {
            _logger.LogError("WebSocket connection failed with status: {Message}. This might indicate the WebSocket endpoint doesn't exist or requires different authentication.", wsEx.Message);
            throw;
        }

        // Listen for events
        var buffer = new byte[8192];
        while (_webSocket.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
            
            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogWarning("WebSocket closed by server");
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", stoppingToken);
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await HandleNamespaceEventAsync(json, stoppingToken);
            }
        }
    }

    private async Task HandleNamespaceEventAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var eventData = JsonSerializer.Deserialize<RancherWebSocketEvent>(json);
            
            if (eventData?.Data?.Object == null || eventData.Name != "resource.change")
            {
                return;
            }

            var resourceType = eventData.Data.ResourceType;
            if (resourceType != "namespace")
            {
                return;
            }

            var namespaceName = eventData.Data.Object.Name;
            var projectId = eventData.Data.Object.ProjectId;
            
            _logger.LogInformation("Namespace event detected: {EventType} - {Namespace} in project {ProjectId}", 
                eventData.Data.ChangeType, namespaceName, projectId);

            // Only handle "create" events for namespaces with a project assignment
            if (eventData.Data.ChangeType != "create" || string.IsNullOrEmpty(projectId) || string.IsNullOrEmpty(namespaceName))
            {
                return;
            }

            // Find the Project CRD that manages this Rancher project
            var allProjects = await _kubernetesClient.ListAsync<V1Project>(cancellationToken: cancellationToken);
            
            foreach (var project in allProjects)
            {
                // Check if this CRD manages the project and has Observe enabled
                if (project.Status?.ProjectId != projectId)
                {
                    continue;
                }

                var allowObserve = (project.Spec.ManagementPolicies == null || project.Spec.ManagementPolicies.Count == 0)
                    ? false
                    : project.Spec.ManagementPolicies.Any(p => string.Equals(p, "Observe", StringComparison.OrdinalIgnoreCase));

                if (!allowObserve)
                {
                    _logger.LogInformation("Project CRD {Name} manages project {ProjectId} but Observe is not enabled", 
                        project.Metadata.Name, projectId);
                    continue;
                }

                // Check if namespace is already in the CRD spec
                if (project.Spec.Namespaces.Any(ns => ns.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogInformation("Namespace {Namespace} already exists in CRD {Name} spec", 
                        namespaceName, project.Metadata.Name);
                    continue;
                }

                // Add the namespace to the CRD spec
                _logger.LogInformation("Adding namespace {Namespace} to CRD {Name} spec (Observe enabled)", 
                    namespaceName, project.Metadata.Name);
                
                project.Spec.Namespaces.Add(namespaceName);
                await _kubernetesClient.UpdateAsync(project, cancellationToken);
                
                _logger.LogInformation("Successfully updated CRD {Name} with new namespace {Namespace}", 
                    project.Metadata.Name, namespaceName);
                
                // Create Kubernetes event for successful update
                await _eventService.CreateEventAsync(
                    project,
                    "NamespaceDiscovered",
                    $"Discovered and added namespace '{namespaceName}' to project via WebSocket event",
                    "Normal",
                    cancellationToken);
                
                break; // Only update the first matching project
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling namespace event: {Json}", json);
        }
    }

    public override void Dispose()
    {
        _webSocket?.Dispose();
        base.Dispose();
    }

    // WebSocket event models
    private class RancherWebSocketEvent
    {
        public string? Name { get; set; }
        public RancherEventData? Data { get; set; }
    }

    private class RancherEventData
    {
        public string? ResourceType { get; set; }
        public string? ChangeType { get; set; }
        public RancherNamespaceObject? Object { get; set; }
    }

    private class RancherNamespaceObject
    {
        public string? Name { get; set; }
        public string? ProjectId { get; set; }
    }
}
