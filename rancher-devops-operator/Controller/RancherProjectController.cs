using System.Diagnostics;
using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Logging;
using rancher_devops_operator.Entities;
using rancher_devops_operator.Services;

namespace rancher_devops_operator.Controller;

[EntityRbac(typeof(V1RancherProject), Verbs = RbacVerb.All)]
public class RancherProjectController : IEntityController<V1RancherProject>
{
    private readonly ILogger<RancherProjectController> _logger;
    private readonly IRancherApiService _rancherApi;
    private readonly IKubernetesClient _kubernetesClient;
    private readonly IKubernetesEventService _eventService;

    public RancherProjectController(
        ILogger<RancherProjectController> logger,
        IRancherApiService rancherApi,
        IKubernetesClient kubernetesClient,
        IKubernetesEventService eventService)
    {
        _logger = logger;
        _rancherApi = rancherApi;
        _kubernetesClient = kubernetesClient;
        _eventService = eventService;
    }

    public async Task ReconcileAsync(V1RancherProject entity, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;

        try
        {
            _logger.LogInformation("Reconciling RancherProject: {Name}", entity.Metadata.Name);
            await _eventService.CreateEventAsync(entity, "ReconcileStarted", "Starting reconciliation", "Normal", cancellationToken);

            // Initialize status if needed
            if (entity.Status == null)
            {
                entity.Status = new V1RancherProject.RancherProjectStatus();
            }

            // Step 1: Get cluster ID from cluster name
            var clusterId = await _rancherApi.GetClusterIdByNameAsync(entity.Spec.ClusterName, cancellationToken);
            if (string.IsNullOrEmpty(clusterId))
            {
                entity.Status.Phase = "Error";
                entity.Status.ErrorMessage = $"Cluster '{entity.Spec.ClusterName}' not found";
                await _kubernetesClient.UpdateStatusAsync(entity, cancellationToken);
                await _eventService.CreateEventAsync(entity, "ClusterNotFound", $"Cluster '{entity.Spec.ClusterName}' not found", "Warning", cancellationToken);
                _logger.LogError("Cluster not found: {ClusterName}", entity.Spec.ClusterName);
                MetricsService.RecordError("cluster_not_found");
                return;
            }

            entity.Status.ClusterId = clusterId;
            await _eventService.CreateEventAsync(entity, "ClusterResolved", $"Resolved cluster '{entity.Spec.ClusterName}' to ID: {clusterId}", "Normal", cancellationToken);

            // Step 2: Create or get Rancher project
            var projectName = entity.Spec.DisplayName ?? entity.Metadata.Name;
            var existingProject = await _rancherApi.GetProjectByNameAsync(clusterId, projectName, cancellationToken);

            if (existingProject == null)
            {
                _logger.LogInformation("Creating new Rancher project: {ProjectName}", projectName);
                await _eventService.CreateEventAsync(entity, "CreatingProject", $"Creating Rancher project: {projectName}", "Normal", cancellationToken);
                
                var newProject = await _rancherApi.CreateProjectAsync(
                    clusterId,
                    projectName,
                    entity.Spec.Description,
                    cancellationToken);

                if (newProject == null)
                {
                    entity.Status.Phase = "Error";
                    entity.Status.ErrorMessage = "Failed to create Rancher project";
                    await _kubernetesClient.UpdateStatusAsync(entity, cancellationToken);
                    await _eventService.CreateEventAsync(entity, "ProjectCreationFailed", "Failed to create Rancher project", "Warning", cancellationToken);
                    MetricsService.RecordError("project_creation_failed");
                    return;
                }

                entity.Status.ProjectId = newProject.Id;
                MetricsService.ProjectsCreated.Inc();
                MetricsService.ActiveProjects.Inc();
                await _eventService.CreateEventAsync(entity, "ProjectCreated", $"Successfully created Rancher project: {projectName} (ID: {newProject.Id})", "Normal", cancellationToken);
            }
            else
            {
                _logger.LogInformation("Found existing Rancher project: {ProjectId}", existingProject.Id);
                entity.Status.ProjectId = existingProject.Id;
                await _eventService.CreateEventAsync(entity, "ProjectFound", $"Using existing Rancher project: {projectName} (ID: {existingProject.Id})", "Normal", cancellationToken);
            }

            // Step 3: Create namespaces
            entity.Status.CreatedNamespaces.Clear();
            var namespaceCount = 0;
            foreach (var namespaceName in entity.Spec.Namespaces)
            {
                try
                {
                    _logger.LogInformation("Creating namespace: {Namespace} in project {ProjectId}", 
                        namespaceName, entity.Status.ProjectId);

                    var existingNamespaces = await _rancherApi.GetProjectNamespacesAsync(entity.Status.ProjectId!, cancellationToken);
                    var existingNs = existingNamespaces.FirstOrDefault(ns => ns.Name == namespaceName);

                    if (existingNs == null)
                    {
                        await _rancherApi.CreateNamespaceAsync(entity.Status.ProjectId!, namespaceName, cancellationToken);
                        MetricsService.NamespacesCreated.Inc();
                        await _eventService.CreateEventAsync(entity, "NamespaceCreated", $"Created namespace: {namespaceName}", "Normal", cancellationToken);
                    }

                    entity.Status.CreatedNamespaces.Add(namespaceName);
                    namespaceCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create namespace: {Namespace}", namespaceName);
                    await _eventService.CreateEventAsync(entity, "NamespaceCreationFailed", $"Failed to create namespace: {namespaceName} - {ex.Message}", "Warning", cancellationToken);
                    MetricsService.RecordError("namespace_creation_failed");
                }
            }
            MetricsService.ActiveNamespaces.Set(namespaceCount);

            // Step 4: Configure project members
            entity.Status.ConfiguredMembers.Clear();
            var memberCount = 0;
            foreach (var member in entity.Spec.Members)
            {
                try
                {
                    _logger.LogInformation("Adding member {PrincipalId} with role {Role} to project {ProjectId}",
                        member.PrincipalId, member.Role, entity.Status.ProjectId);

                    // Check if member already exists
                    var existingMembers = await _rancherApi.GetProjectMembersAsync(entity.Status.ProjectId!, cancellationToken);
                    var existingMember = existingMembers.FirstOrDefault(m =>
                        (m.UserPrincipalId == member.PrincipalId || m.GroupPrincipalId == member.PrincipalId) &&
                        m.RoleTemplateId == member.Role);

                    if (existingMember == null)
                    {
                        await _rancherApi.CreateProjectMemberAsync(
                            entity.Status.ProjectId!,
                            member.PrincipalId,
                            member.Role,
                            cancellationToken);
                        MetricsService.MembersAdded.Inc();
                        await _eventService.CreateEventAsync(entity, "MemberAdded", $"Added member: {member.PrincipalId} with role: {member.Role}", "Normal", cancellationToken);
                    }

                    entity.Status.ConfiguredMembers.Add($"{member.PrincipalId}:{member.Role}");
                    memberCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add member: {PrincipalId}", member.PrincipalId);
                    await _eventService.CreateEventAsync(entity, "MemberAddFailed", $"Failed to add member: {member.PrincipalId} - {ex.Message}", "Warning", cancellationToken);
                    MetricsService.RecordError("member_add_failed");
                }
            }
            MetricsService.ActiveMembers.Set(memberCount);

            // Update status
            entity.Status.Phase = "Ready";
            entity.Status.LastReconcileTime = DateTime.UtcNow;
            entity.Status.ErrorMessage = null;

            await _kubernetesClient.UpdateStatusAsync(entity, cancellationToken);
            await _eventService.CreateEventAsync(entity, "ReconcileCompleted", "Successfully reconciled RancherProject", "Normal", cancellationToken);
            _logger.LogInformation("Successfully reconciled RancherProject: {Name}", entity.Metadata.Name);
            
            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconciling RancherProject: {Name}", entity.Metadata.Name);
            await _eventService.CreateEventAsync(entity, "ReconcileFailed", $"Reconciliation failed: {ex.Message}", "Warning", cancellationToken);
            MetricsService.RecordError("reconciliation_failed");
            
            if (entity.Status != null)
            {
                entity.Status.Phase = "Error";
                entity.Status.ErrorMessage = ex.Message;
                entity.Status.LastReconcileTime = DateTime.UtcNow;
                await _kubernetesClient.UpdateStatusAsync(entity, cancellationToken);
            }
        }
        finally
        {
            stopwatch.Stop();
            MetricsService.ReconciliationDuration.WithLabels(entity.Metadata.Name).Observe(stopwatch.Elapsed.TotalSeconds);
            MetricsService.RecordReconciliation(entity.Metadata.Name, success);
        }
    }

    public async Task DeletedAsync(V1RancherProject entity, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Deleting RancherProject: {Name}", entity.Metadata.Name);
            await _eventService.CreateEventAsync(entity, "DeletionStarted", "Starting deletion of RancherProject", "Normal", cancellationToken);

            if (string.IsNullOrEmpty(entity.Status?.ProjectId))
            {
                _logger.LogWarning("No project ID found for entity {Name}, skipping deletion", entity.Metadata.Name);
                return;
            }

            // Delete namespaces
            if (!string.IsNullOrEmpty(entity.Status.ClusterId))
            {
                foreach (var namespaceName in entity.Status.CreatedNamespaces)
                {
                    try
                    {
                        _logger.LogInformation("Deleting namespace: {Namespace}", namespaceName);
                        await _rancherApi.DeleteNamespaceAsync(entity.Status.ClusterId, namespaceName, cancellationToken);
                        MetricsService.NamespacesDeleted.Inc();
                        await _eventService.CreateEventAsync(entity, "NamespaceDeleted", $"Deleted namespace: {namespaceName}", "Normal", cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete namespace: {Namespace}", namespaceName);
                        await _eventService.CreateEventAsync(entity, "NamespaceDeletionFailed", $"Failed to delete namespace: {namespaceName} - {ex.Message}", "Warning", cancellationToken);
                        MetricsService.RecordError("namespace_deletion_failed");
                    }
                }
            }

            // Delete the project
            await _rancherApi.DeleteProjectAsync(entity.Status.ProjectId, cancellationToken);
            MetricsService.ProjectsDeleted.Inc();
            MetricsService.ActiveProjects.Dec();
            await _eventService.CreateEventAsync(entity, "ProjectDeleted", $"Successfully deleted RancherProject", "Normal", cancellationToken);
            _logger.LogInformation("Successfully deleted RancherProject: {Name}", entity.Metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting RancherProject: {Name}", entity.Metadata.Name);
            await _eventService.CreateEventAsync(entity, "DeletionFailed", $"Deletion failed: {ex.Message}", "Warning", cancellationToken);
            MetricsService.RecordError("deletion_failed");
        }
    }
}
