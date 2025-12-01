using k8s.Models;
using KubeOps.Abstractions.Entities;

namespace rancher_devops_operator.Entities;

// Renamed from V1RancherProject to V1Project; Kind changed to Project.
[KubernetesEntity(Group = "rancher.devops.io", ApiVersion = "v1", Kind = "Project")]
public class V1Project : CustomKubernetesEntity<V1Project.ProjectSpec, V1Project.ProjectStatus>
{
    public class ProjectSpec
    {
        /// <summary>
        /// Name of the Rancher cluster (not cluster ID)
        /// </summary>
        public string ClusterName { get; set; } = string.Empty;

        /// <summary>
        /// Display name for the Rancher project
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Description of the project
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// List of namespaces to create and associate with this project
        /// </summary>
        public List<string> Namespaces { get; set; } = new();

        /// <summary>
        /// Project members with their roles
        /// </summary>
        public List<ProjectMember> Members { get; set; } = new();

        /// <summary>
        /// Resource quotas for the project (optional)
        /// </summary>
        public ResourceQuota? ResourceQuota { get; set; }

        /// <summary>
        /// Management policies controlling allowed actions: "Create", "Delete", "Observe".
        /// If omitted or empty, defaults to ["Create"] (Delete/Observe are opt-in).
        /// - Create: Allows creating projects/namespaces/members
        /// - Delete: Allows deleting projects and removing namespace associations
        /// - Observe: Discovers and adds existing namespaces/members from project to CRD spec
        /// </summary>
        public List<string> ManagementPolicies { get; set; } = new(){ "Create" };

        /// <summary>
        /// Namespace-specific management policies controlling namespace actions: "Create", "Update", "Delete".
        /// If omitted or empty, defaults to ["Create", "Update"] (Delete is opt-in).
        /// - Create: Allows creating namespaces
        /// - Update: Allows (re)assigning namespaces into or out of the Rancher project (disassociation/move)
        /// - Delete: Allows deleting namespaces ONLY when controller CleanupNamespaces=true (otherwise acts like Update)
        /// </summary>
        public List<string> NamespaceManagementPolicies { get; set; } = new() { "Create", "Update" };
    }

    public class ProjectMember
    {
        /// <summary>
        /// Principal ID (user or group)
        /// </summary>
        public string PrincipalId { get; set; } = string.Empty;

        /// <summary>
        /// Optional principal name to resolve to an ID if PrincipalId not provided
        /// </summary>
        public string? PrincipalName { get; set; }

        /// <summary>
        /// Role to assign (e.g., "project-owner", "project-member")
        /// </summary>
        public string Role { get; set; } = string.Empty;
    }

    public class ResourceQuota
    {
        public string? LimitsCpu { get; set; }
        public string? LimitsMemory { get; set; }
        public string? RequestsCpu { get; set; }
        public string? RequestsMemory { get; set; }
    }

    public class ProjectStatus
    {
        /// <summary>
        /// Rancher project ID (c-xxxxx:p-xxxxx)
        /// </summary>
        public string? ProjectId { get; set; }

        /// <summary>
        /// Rancher cluster ID
        /// </summary>
        public string? ClusterId { get; set; }

        /// <summary>
        /// Current phase of the resource
        /// </summary>
        public string Phase { get; set; } = "Pending";

        /// <summary>
        /// List of namespaces created by the operator (not merely assigned or moved)
        /// </summary>
        public List<string> CreatedNamespaces { get; set; } = new();

        /// <summary>
        /// List of namespaces that were manually removed (cluster/Rancher UI) and should NOT be recreated even if present in spec
        /// </summary>
        public List<string> ManuallyRemovedNamespaces { get; set; } = new();

        /// <summary>
        /// Last reconciliation time
        /// </summary>
        public DateTime? LastReconcileTime { get; set; }

        /// <summary>
        /// Timestamp when the controller first successfully created or took over the project
        /// </summary>
        public DateTime? CreatedTimestamp { get; set; }

        /// <summary>
        /// Timestamp of last successful status update (end of reconcile)
        /// </summary>
        public DateTime? LastUpdatedTimestamp { get; set; }

        /// <summary>
        /// Error message if any
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// List of configured members
        /// </summary>
        public List<string> ConfiguredMembers { get; set; } = new();
    }
}
