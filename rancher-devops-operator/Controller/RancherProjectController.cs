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
        bool Allows(string name) => (entity.Spec.ManagementPolicies == null || entity.Spec.ManagementPolicies.Count == 0)
            ? true
            : entity.Spec.ManagementPolicies.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        var allowCreate = Allows("Create");
        var allowDelete = Allows("Delete");
        var allowObserve = Allows("Observe");

        try
        {
            _logger.LogInformation("Reconciling RancherProject: {Name}", entity.Metadata.Name);
            await _eventService.CreateEventAsync(entity, "ReconcileStarted", "Starting reconciliation", "Normal", cancellationToken);

            if (entity.Status == null)
            {
                entity.Status = new V1RancherProject.RancherProjectStatus();
            }

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

            var projectName = entity.Spec.DisplayName ?? entity.Metadata.Name;
            var existingProject = await _rancherApi.GetProjectByNameAsync(clusterId, projectName, cancellationToken);
            if (existingProject == null)
            {
                if (!allowCreate)
                {
                    _logger.LogInformation("Project {ProjectName} does not exist and Create is not permitted by managementPolicies.", projectName);
                    entity.Status.Phase = "Observed";
                    await _kubernetesClient.UpdateStatusAsync(entity, cancellationToken);
                    return;
                }

                _logger.LogInformation("Creating new Rancher project: {ProjectName}", projectName);
                await _eventService.CreateEventAsync(entity, "CreatingProject", $"Creating Rancher project: {projectName}", "Normal", cancellationToken);
                var newProject = await _rancherApi.CreateProjectAsync(clusterId, projectName, entity.Spec.Description, cancellationToken);
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
                if (allowCreate)
                {
                    _logger.LogWarning("Project {ProjectName} already exists but Create is requested by managementPolicies.", projectName);
                    entity.Status.ProjectId = existingProject.Id;
                    entity.Status.Phase = "Error";
                    entity.Status.ErrorMessage = $"Rancher project '{projectName}' already exists.";
                    await _kubernetesClient.UpdateStatusAsync(entity, cancellationToken);
                    await _eventService.CreateEventAsync(
                        entity,
                        "ProjectAlreadyExists",
                        $"Rancher project '{projectName}' already exists while Create is requested.",
                        "Warning",
                        cancellationToken);
                    MetricsService.RecordError("project_already_exists");
                    return;
                }

                _logger.LogInformation("Found existing Rancher project: {ProjectId}", existingProject.Id);
                entity.Status.ProjectId = existingProject.Id;
                entity.Status.Phase = "Observed";
                await _eventService.CreateEventAsync(entity, "ProjectFound", $"Using existing Rancher project: {projectName} (ID: {existingProject.Id})", "Normal", cancellationToken);
            }

            entity.Status.CreatedNamespaces.Clear();
            var namespaceCount = 0;
            foreach (var originalNamespaceName in entity.Spec.Namespaces)
            {
                try
                {
                    var namespaceName = originalNamespaceName.ToLowerInvariant();
                    _logger.LogInformation("Creating namespace: {Namespace} in project {ProjectId}", namespaceName, entity.Status.ProjectId);
                    var existingNamespaces = await _rancherApi.GetProjectNamespacesAsync(entity.Status.ProjectId!, cancellationToken);
                    var existingNs = existingNamespaces.FirstOrDefault(ns => ns.Name.Equals(namespaceName, StringComparison.OrdinalIgnoreCase));
                    if (existingNs == null && allowCreate)
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
                    _logger.LogError(ex, "Failed to create namespace: {Namespace}", originalNamespaceName);
                    await _eventService.CreateEventAsync(entity, "NamespaceCreationFailed", $"Failed to create namespace: {originalNamespaceName} - {ex.Message}", "Warning", cancellationToken);
                    entity.Status.Phase = "Error";
                    entity.Status.ErrorMessage = ex.Message;
                    await _kubernetesClient.UpdateStatusAsync(entity, cancellationToken);
                    MetricsService.RecordError("namespace_creation_failed");
                }
            }
            MetricsService.ActiveNamespaces.Set(namespaceCount);

            entity.Status.ConfiguredMembers.Clear();
            var memberCount = 0;
            foreach (var member in entity.Spec.Members)
            {
                try
                {
                    _logger.LogInformation("Adding member {PrincipalId} with role {Role} to project {ProjectId}", string.IsNullOrWhiteSpace(member.PrincipalId) ? member.PrincipalName : member.PrincipalId, member.Role, entity.Status.ProjectId);
                    var effectivePrincipalId = member.PrincipalId;
                    if (string.IsNullOrWhiteSpace(effectivePrincipalId) && !string.IsNullOrWhiteSpace(member.PrincipalName))
                    {
                        effectivePrincipalId = await _rancherApi.GetPrincipalIdByNameAsync(member.PrincipalName, cancellationToken);
                        if (string.IsNullOrWhiteSpace(effectivePrincipalId))
                        {
                            throw new InvalidOperationException($"Principal name '{member.PrincipalName}' could not be resolved.");
                        }
                    }
                    var existingMembers = await _rancherApi.GetProjectMembersAsync(entity.Status.ProjectId!, cancellationToken);
                    var existingMember = existingMembers.FirstOrDefault(m => (m.UserPrincipalId == effectivePrincipalId || m.GroupPrincipalId == effectivePrincipalId) && m.RoleTemplateId == member.Role);
                    if (existingMember == null && allowCreate)
                    {
                        await _rancherApi.CreateProjectMemberAsync(entity.Status.ProjectId!, effectivePrincipalId!, member.Role, cancellationToken);
                        MetricsService.MembersAdded.Inc();
                        await _eventService.CreateEventAsync(entity, "MemberAdded", $"Added member: {(member.PrincipalName ?? effectivePrincipalId)} with role: {member.Role}", "Normal", cancellationToken);
                    }
                    entity.Status.ConfiguredMembers.Add($"{effectivePrincipalId}:{member.Role}");
                    memberCount++;
                }
                catch (Exception ex)
                {
                    var identifierType = string.IsNullOrWhiteSpace(member.PrincipalId) ? "name" : "id";
                    var identifierValue = string.IsNullOrWhiteSpace(member.PrincipalId) ? member.PrincipalName : member.PrincipalId;
                    _logger.LogError(ex, "Failed to add member by {IdentifierType}: {Identifier}", identifierType, identifierValue);
                    await _eventService.CreateEventAsync(entity, "MemberAddFailed", $"Failed to add member: {(member.PrincipalName ?? member.PrincipalId)} - {ex.Message}", "Warning", cancellationToken);
                    MetricsService.RecordError("member_add_failed");
                }
            }
            MetricsService.ActiveMembers.Set(memberCount);

            if (entity.Status.ProjectId is not null && allowCreate)
            {
                entity.Status.Phase = "Active";
            }

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
            bool Allows(string name) => (entity.Spec.ManagementPolicies == null || entity.Spec.ManagementPolicies.Count == 0)
                ? true
                : entity.Spec.ManagementPolicies.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
            var allowDelete = Allows("Delete");

            if (!allowDelete)
            {
                _logger.LogInformation("managementPolicies does not include Delete; skipping deletion for {Name}", entity.Metadata.Name);
                return;
            }
            if (string.IsNullOrEmpty(entity.Status?.ProjectId))
            {
                _logger.LogWarning("No project ID found for entity {Name}, skipping deletion", entity.Metadata.Name);
                return;
            }
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
            await _rancherApi.DeleteProjectAsync(entity.Status.ProjectId, cancellationToken);
            MetricsService.ProjectsDeleted.Inc();
            MetricsService.ActiveProjects.Dec();
            await _eventService.CreateEventAsync(entity, "ProjectDeleted", "Successfully deleted RancherProject", "Normal", cancellationToken);
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

