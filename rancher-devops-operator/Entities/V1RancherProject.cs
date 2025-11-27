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
        /// If omitted or empty, defaults to ["Create", "Delete"].
        /// - Create: Allows creating projects/namespaces/members
        /// - Delete: Allows deleting projects and removing namespace associations
        /// - Observe: Discovers and adds existing namespaces/members from project to CRD spec
        /// </summary>
        public List<string> ManagementPolicies { get; set; } = new();
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
        /// List of created namespaces
        /// </summary>
        public List<string> CreatedNamespaces { get; set; } = new();

        /// <summary>
        /// Last reconciliation time
        /// </summary>
        public DateTime? LastReconcileTime { get; set; }

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
