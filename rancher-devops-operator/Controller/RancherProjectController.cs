using System.Diagnostics;
using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using rancher_devops_operator.Entities;
using rancher_devops_operator.Services;

namespace rancher_devops_operator.Controller;

[EntityRbac(typeof(V1Project), Verbs = RbacVerb.All)]
public class ProjectController : IEntityController<V1Project>
{
    private readonly ILogger<ProjectController> _logger;
    private readonly IRancherApiService _rancherApi;
    private readonly IKubernetesClient _kubernetesClient;
    private readonly IKubernetesEventService _eventService;
    private readonly bool _cleanupNamespaces;

    public ProjectController(
        ILogger<ProjectController> logger,
        IRancherApiService rancherApi,
        IKubernetesClient kubernetesClient,
        IKubernetesEventService eventService,
        IConfiguration configuration)
    {
        _logger = logger;
        _rancherApi = rancherApi;
        _kubernetesClient = kubernetesClient;
        _eventService = eventService;
        // Support both Rancher:CleanupNamespaces and flat CleanupNamespaces
        _cleanupNamespaces = configuration.GetValue<bool>("Rancher:CleanupNamespaces",
            configuration.GetValue<bool>("CleanupNamespaces", false));
    }

    private async Task<bool> IsNamespaceClaimedByOtherCrdAsync(string namespaceName, string currentCrdName, CancellationToken cancellationToken)
    {
        try
        {
            var allProjects = await _kubernetesClient.ListAsync<V1Project>(cancellationToken: cancellationToken);
            foreach (var project in allProjects)
            {
                if (project.Metadata.Name == currentCrdName)
                    continue;

                if (project.Spec.Namespaces.Any(ns => ns.Equals(namespaceName, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Namespace {Namespace} is claimed by another CRD: {CrdName}", namespaceName, project.Metadata.Name);
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if namespace {Namespace} is claimed by other CRD", namespaceName);
            return false;
        }
    }

    public async Task ReconcileAsync(V1Project entity, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;
        
        // Default to Create and Delete if ManagementPolicies is empty (Observe is opt-in)
        bool Allows(string name) => (entity.Spec.ManagementPolicies == null || entity.Spec.ManagementPolicies.Count == 0)
            ? (name == "Create" || name == "Delete")
            : entity.Spec.ManagementPolicies.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        
        var allowCreate = Allows("Create");
        var allowDelete = Allows("Delete");
        var allowObserve = Allows("Observe");

        try
        {
            _logger.LogInformation("Reconciling Project: {Name}", entity.Metadata.Name);
            await _eventService.CreateEventAsync(entity, "ReconcileStarted", "Starting reconciliation", "Normal", cancellationToken);

            if (entity.Status == null)
            {
                entity.Status = new V1Project.ProjectStatus();
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
                // Project doesn't exist - create it if allowed
                if (!allowCreate)
                {
                    _logger.LogInformation("Project {ProjectName} does not exist and Create is not permitted by managementPolicies.", projectName);
                    entity.Status.Phase = "Pending";
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
                // Project exists - take it over
                _logger.LogInformation("Taking over existing Rancher project: {ProjectId}", existingProject.Id);
                entity.Status.ProjectId = existingProject.Id;
                
                // Only discover and update CRD spec if Observe is enabled
                if (allowObserve)
                {
                    _logger.LogInformation("Observe enabled - discovering existing namespaces and members");
                    
                    // Discover existing namespaces in the project
                    try
                    {
                        var existingNamespaces = await _rancherApi.GetProjectNamespacesAsync(existingProject.Id, cancellationToken);
                        var discoveredNamespaces = existingNamespaces.Select(ns => ns.Name).ToList();
                        
                        if (discoveredNamespaces.Any())
                        {
                            _logger.LogInformation("Discovered {Count} existing namespaces in project {ProjectId}", discoveredNamespaces.Count, existingProject.Id);
                            
                            // Add discovered namespaces to spec if not already present
                            foreach (var ns in discoveredNamespaces)
                            {
                                if (!entity.Spec.Namespaces.Any(n => n.Equals(ns, StringComparison.OrdinalIgnoreCase)))
                                {
                                    entity.Spec.Namespaces.Add(ns);
                                    _logger.LogInformation("Added discovered namespace {Namespace} to CRD spec", ns);
                                }
                            }
                            
                            // Update the CRD spec
                            await _kubernetesClient.UpdateAsync(entity, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to discover existing namespaces for project {ProjectId}", existingProject.Id);
                    }
                    
                    // Discover existing members
                    try
                    {
                        var existingMembers = await _rancherApi.GetProjectMembersAsync(existingProject.Id, cancellationToken);
                        if (existingMembers.Any())
                        {
                            _logger.LogInformation("Discovered {Count} existing members in project {ProjectId}", existingMembers.Count, existingProject.Id);
                            
                            // Add discovered members to spec if not already present
                            foreach (var member in existingMembers)
                            {
                                var principalId = member.UserPrincipalId ?? member.GroupPrincipalId;
                                if (!string.IsNullOrEmpty(principalId))
                                {
                                    var alreadyExists = entity.Spec.Members.Any(m => 
                                        m.PrincipalId == principalId && m.Role == member.RoleTemplateId);
                                    
                                    if (!alreadyExists)
                                    {
                                        entity.Spec.Members.Add(new V1Project.ProjectMember
                                        {
                                            PrincipalId = principalId,
                                            Role = member.RoleTemplateId
                                        });
                                        _logger.LogInformation("Added discovered member {PrincipalId} with role {Role} to CRD spec", principalId, member.RoleTemplateId);
                                    }
                                }
                            }
                            
                            // Update the CRD spec
                            await _kubernetesClient.UpdateAsync(entity, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to discover existing members for project {ProjectId}", existingProject.Id);
                    }
                    
                    await _eventService.CreateEventAsync(entity, "ProjectObserved", $"Observed and updated CRD with existing resources from project: {projectName} (ID: {existingProject.Id})", "Normal", cancellationToken);
                }
                else
                {
                    _logger.LogInformation("Observe not enabled - skipping discovery of existing resources");
                    await _eventService.CreateEventAsync(entity, "ProjectTakenOver", $"Took over existing Rancher project: {projectName} (ID: {existingProject.Id})", "Normal", cancellationToken);
                }
            }

            entity.Status.CreatedNamespaces.Clear();
            var namespaceCount = 0;
            foreach (var originalNamespaceName in entity.Spec.Namespaces)
            {
                try
                {
                    var namespaceName = originalNamespaceName.ToLowerInvariant();
                    _logger.LogDebug("Processing namespace: {Namespace} for project {ProjectId}", namespaceName, entity.Status.ProjectId);
                    
                    // Check if namespace exists in Rancher
                    var existingNs = await _rancherApi.GetNamespaceAsync(entity.Status.ClusterId!, namespaceName, cancellationToken);
                    
                    if (existingNs == null)
                    {
                        // Namespace doesn't exist - create it if allowed
                        if (!allowCreate)
                        {
                            _logger.LogInformation("Namespace {Namespace} does not exist and Create is not permitted.", namespaceName);
                            continue;
                        }
                        
                        await _rancherApi.CreateNamespaceAsync(entity.Status.ProjectId!, namespaceName, cancellationToken);
                        MetricsService.NamespacesCreated.Inc();
                        await _eventService.CreateEventAsync(entity, "NamespaceCreated", $"Created namespace: {namespaceName}", "Normal", cancellationToken);
                        entity.Status.CreatedNamespaces.Add(namespaceName);
                        namespaceCount++;
                    }
                    else
                    {
                        // Namespace exists - check if it's already in our project
                        if (existingNs.ProjectId == entity.Status.ProjectId)
                        {
                            _logger.LogDebug("Namespace {Namespace} already assigned to this project", namespaceName);
                            entity.Status.CreatedNamespaces.Add(namespaceName);
                            namespaceCount++;
                        }
                        else if (!string.IsNullOrEmpty(existingNs.ProjectId))
                        {
                            // Namespace is in a different project - check if another CRD claims it
                            var isClaimed = await IsNamespaceClaimedByOtherCrdAsync(namespaceName, entity.Metadata.Name, cancellationToken);
                            if (isClaimed)
                            {
                                var errorMsg = $"Namespace '{namespaceName}' is already claimed by another Project CRD and cannot be moved.";
                                _logger.LogError(errorMsg);
                                await _eventService.CreateEventAsync(entity, "NamespaceConflict", errorMsg, "Warning", cancellationToken);
                                entity.Status.Phase = "Error";
                                entity.Status.ErrorMessage = errorMsg;
                                await _kubernetesClient.UpdateStatusAsync(entity, cancellationToken);
                                MetricsService.RecordError("namespace_conflict");
                                return;
                            }
                            else
                            {
                                // Not claimed by another CRD - we can move it if create is allowed
                                if (!allowCreate)
                                {
                                    _logger.LogInformation("Namespace {Namespace} exists in another project but Create is not permitted.", namespaceName);
                                    continue;
                                }
                                
                                _logger.LogInformation("Moving namespace {Namespace} from project {OldProject} to {NewProject}", 
                                    namespaceName, existingNs.ProjectId, entity.Status.ProjectId);
                                await _rancherApi.UpdateNamespaceProjectAsync(entity.Status.ClusterId!, namespaceName, entity.Status.ProjectId!, cancellationToken);
                                await _eventService.CreateEventAsync(entity, "NamespaceMoved", 
                                    $"Moved namespace {namespaceName} to this project", "Normal", cancellationToken);
                                entity.Status.CreatedNamespaces.Add(namespaceName);
                                namespaceCount++;
                            }
                        }
                        else
                        {
                            // Namespace exists but not assigned to any project - assign it if create is allowed
                            if (!allowCreate)
                            {
                                _logger.LogInformation("Namespace {Namespace} exists but Create is not permitted.", namespaceName);
                                continue;
                            }
                            
                            _logger.LogInformation("Assigning existing namespace {Namespace} to project {ProjectId}", namespaceName, entity.Status.ProjectId);
                            await _rancherApi.UpdateNamespaceProjectAsync(entity.Status.ClusterId!, namespaceName, entity.Status.ProjectId!, cancellationToken);
                            await _eventService.CreateEventAsync(entity, "NamespaceAssigned", 
                                $"Assigned namespace {namespaceName} to this project", "Normal", cancellationToken);
                            entity.Status.CreatedNamespaces.Add(namespaceName);
                            namespaceCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process namespace: {Namespace}", originalNamespaceName);
                    await _eventService.CreateEventAsync(entity, "NamespaceProcessingFailed", $"Failed to process namespace: {originalNamespaceName} - {ex.Message}", "Warning", cancellationToken);
                    entity.Status.Phase = "Error";
                    entity.Status.ErrorMessage = ex.Message;
                    await _kubernetesClient.UpdateStatusAsync(entity, cancellationToken);
                    MetricsService.RecordError("namespace_processing_failed");
                    MetricsService.RecordError("namespace_creation_failed");
                }
            }
            MetricsService.ActiveNamespaces.Set(namespaceCount);

            // Handle namespaces removed from spec - disassociate them from the Rancher project (do not delete)
            if (!string.IsNullOrEmpty(entity.Status.ClusterId) && !string.IsNullOrEmpty(entity.Status.ProjectId))
            {
                try
                {
                    var desiredNamespaces = entity.Spec.Namespaces.Select(ns => ns.ToLowerInvariant()).ToHashSet();
                    var projectNamespaces = await _rancherApi.GetProjectNamespacesAsync(entity.Status.ProjectId, cancellationToken);
                    var actualNamespaces = projectNamespaces.Select(n => n.Name.ToLowerInvariant()).ToHashSet();

                    // Names currently in Rancher project but not in CRD spec should be disassociated
                    var namespacesToRemove = actualNamespaces.Except(desiredNamespaces).ToList();

                    foreach (var removedNs in namespacesToRemove)
                    {
                        try
                        {
                            if (_cleanupNamespaces)
                            {
                                _logger.LogInformation("Deleting namespace {Namespace} (CleanupNamespaces=true)", removedNs);
                                var deleted = await _rancherApi.DeleteNamespaceAsync(entity.Status.ClusterId, removedNs, cancellationToken);
                                if (deleted)
                                {
                                    MetricsService.NamespacesDeleted.Inc();
                                    await _eventService.CreateEventAsync(entity, "NamespaceDeleted", $"Deleted namespace {removedNs}", "Normal", cancellationToken);
                                }
                            }
                            else
                            {
                                _logger.LogInformation("Removing namespace {Namespace} from project {ProjectId} (CleanupNamespaces=false)", removedNs, entity.Status.ProjectId);
                                var removed = await _rancherApi.RemoveNamespaceFromProjectAsync(entity.Status.ClusterId, removedNs, cancellationToken);
                                if (removed)
                                {
                                    await _eventService.CreateEventAsync(entity, "NamespaceRemoved", $"Removed namespace {removedNs} from project (namespace preserved)", "Normal", cancellationToken);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            var action = _cleanupNamespaces ? "delete" : "remove";
                            _logger.LogWarning(ex, "Failed to {Action} namespace {Namespace}", action, removedNs);
                            await _eventService.CreateEventAsync(entity, "NamespaceRemovalFailed", $"Failed to {action} namespace {removedNs}: {ex.Message}", "Warning", cancellationToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to evaluate namespaces for removal from project {ProjectId}", entity.Status.ProjectId);
                }
            }

            entity.Status.ConfiguredMembers.Clear();
            var memberCount = 0;
            foreach (var member in entity.Spec.Members)
            {
                try
                {
                    _logger.LogDebug("Adding member {PrincipalId} with role {Role} to project {ProjectId}", string.IsNullOrWhiteSpace(member.PrincipalId) ? member.PrincipalName : member.PrincipalId, member.Role, entity.Status.ProjectId);
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
            await _eventService.CreateEventAsync(entity, "ReconcileCompleted", "Successfully reconciled Project", "Normal", cancellationToken);
            _logger.LogInformation("Successfully reconciled Project: {Name}", entity.Metadata.Name);
            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconciling Project: {Name}", entity.Metadata.Name);
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

    public async Task DeletedAsync(V1Project entity, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Deleting Project: {Name}", entity.Metadata.Name);
            await _eventService.CreateEventAsync(entity, "DeletionStarted", "Starting deletion of Project", "Normal", cancellationToken);
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
            
            // Remove namespaces from project (but don't delete them)
            if (!string.IsNullOrEmpty(entity.Status.ClusterId))
            {
                foreach (var namespaceName in entity.Status.CreatedNamespaces)
                {
                    try
                    {
                        if (_cleanupNamespaces)
                        {
                            _logger.LogInformation("Deleting namespace {Namespace} (CleanupNamespaces=true)", namespaceName);
                            var deleted = await _rancherApi.DeleteNamespaceAsync(entity.Status.ClusterId, namespaceName, cancellationToken);
                            if (deleted)
                            {
                                MetricsService.NamespacesDeleted.Inc();
                                await _eventService.CreateEventAsync(entity, "NamespaceDeleted", 
                                    $"Deleted namespace {namespaceName}", "Normal", cancellationToken);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("Removing namespace {Namespace} from project (CleanupNamespaces=false)", namespaceName);
                            await _rancherApi.RemoveNamespaceFromProjectAsync(entity.Status.ClusterId, namespaceName, cancellationToken);
                            await _eventService.CreateEventAsync(entity, "NamespaceRemovedFromProject", 
                                $"Removed namespace {namespaceName} from project (namespace preserved)", "Normal", cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        var action = _cleanupNamespaces ? "delete" : "remove";
                        _logger.LogError(ex, "Failed to {Action} namespace {Namespace}", action, namespaceName);
                        await _eventService.CreateEventAsync(entity, "NamespaceRemovalFailed", 
                            $"Failed to {action} namespace {namespaceName}: {ex.Message}", "Warning", cancellationToken);
                        MetricsService.RecordError("namespace_removal_failed");
                    }
                }
            }
            
            await _rancherApi.DeleteProjectAsync(entity.Status.ProjectId, cancellationToken);
            MetricsService.ProjectsDeleted.Inc();
            MetricsService.ActiveProjects.Dec();
            await _eventService.CreateEventAsync(entity, "ProjectDeleted", "Successfully deleted Project", "Normal", cancellationToken);
            _logger.LogInformation("Successfully deleted Project: {Name}", entity.Metadata.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting Project: {Name}", entity.Metadata.Name);
            await _eventService.CreateEventAsync(entity, "DeletionFailed", $"Deletion failed: {ex.Message}", "Warning", cancellationToken);
            MetricsService.RecordError("deletion_failed");
        }
    }
}

