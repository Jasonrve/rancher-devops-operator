using k8s;
using k8s.Models;
using KubeOps.Abstractions.Entities;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Logging;

namespace rancher_devops_operator.Services;

public interface IKubernetesEventService
{
    Task CreateEventAsync<TEntity>(TEntity entity, string reason, string message, string type = "Normal", CancellationToken cancellationToken = default) 
        where TEntity : IKubernetesObject<V1ObjectMeta>;
}

public class KubernetesEventService : IKubernetesEventService
{
    private readonly IKubernetesClient _client;
    private readonly ILogger<KubernetesEventService> _logger;
    private const string Component = "rancher-devops-operator";

    public KubernetesEventService(IKubernetesClient client, ILogger<KubernetesEventService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task CreateEventAsync<TEntity>(
        TEntity entity, 
        string reason, 
        string message, 
        string type = "Normal",
        CancellationToken cancellationToken = default) 
        where TEntity : IKubernetesObject<V1ObjectMeta>
    {
        try
        {
            var timestamp = DateTime.UtcNow;
            var eventName = $"{entity.Metadata.Name}.{Guid.NewGuid().ToString()[..8]}";

            var kubeEvent = new Corev1Event
            {
                Metadata = new V1ObjectMeta
                {
                    Name = eventName,
                    NamespaceProperty = entity.Metadata.NamespaceProperty ?? "default"
                },
                InvolvedObject = new V1ObjectReference
                {
                    ApiVersion = entity.ApiVersion,
                    Kind = entity.Kind,
                    Name = entity.Metadata.Name,
                    NamespaceProperty = entity.Metadata.NamespaceProperty,
                    Uid = entity.Metadata.Uid,
                    ResourceVersion = entity.Metadata.ResourceVersion
                },
                Reason = reason,
                Message = message,
                Type = type,
                Source = new V1EventSource
                {
                    Component = Component
                },
                FirstTimestamp = timestamp,
                LastTimestamp = timestamp,
                Count = 1,
                ReportingComponent = Component,
                ReportingInstance = Environment.MachineName
            };

            await _client.CreateAsync(kubeEvent, cancellationToken);
            
            _logger.LogDebug(
                "Created Kubernetes event: {Reason} for {Kind}/{Name} - {Message}", 
                reason, 
                entity.Kind, 
                entity.Metadata.Name, 
                message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create Kubernetes event for {Kind}/{Name}", entity.Kind, entity.Metadata.Name);
        }
    }
}
