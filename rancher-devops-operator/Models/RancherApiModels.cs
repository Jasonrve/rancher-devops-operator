using System.Text.Json.Serialization;

namespace rancher_devops_operator.Models;

/// <summary>
/// Rancher API response for cluster list
/// </summary>
public class RancherClusterList
{
    [JsonPropertyName("data")]
    public List<RancherCluster> Data { get; set; } = new();
}

/// <summary>
/// Rancher cluster model
/// </summary>
public class RancherCluster
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// Rancher project request/response model
/// </summary>
public class RancherProjectRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "project";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("clusterId")]
    public string ClusterId { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("resourceQuota")]
    public RancherResourceQuota? ResourceQuota { get; set; }

    // Optional annotations to mark ownership/management
    [JsonPropertyName("annotations")]
    public Dictionary<string, string>? Annotations { get; set; }
}

public class RancherProject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("clusterId")]
    public string ClusterId { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("annotations")]
    public Dictionary<string, string>? Annotations { get; set; }
}

public class RancherProjectList
{
    [JsonPropertyName("data")]
    public List<RancherProject> Data { get; set; } = new();
}

public class RancherResourceQuota
{
    [JsonPropertyName("limit")]
    public Dictionary<string, string>? Limit { get; set; }
}

/// <summary>
/// Rancher namespace request model
/// </summary>
public class RancherNamespaceRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "namespace";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    // Optional annotations to mark ownership/management
    [JsonPropertyName("annotations")]
    public Dictionary<string, string>? Annotations { get; set; }

    [JsonPropertyName("labels")]
    public Dictionary<string, string>? Labels { get; set; }
}

public class RancherNamespace
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("annotations")]
    public Dictionary<string, string>? Annotations { get; set; }

    [JsonPropertyName("labels")]
    public Dictionary<string, string>? Labels { get; set; }
}

public class RancherNamespaceList
{
    [JsonPropertyName("data")]
    public List<RancherNamespace> Data { get; set; } = new();
}

/// <summary>
/// Rancher project role binding (member) model
/// </summary>
public class RancherProjectRoleBindingRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "projectRoleTemplateBinding";

    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("roleTemplateId")]
    public string RoleTemplateId { get; set; } = string.Empty;

    [JsonPropertyName("userPrincipalId")]
    public string? UserPrincipalId { get; set; }

    [JsonPropertyName("groupPrincipalId")]
    public string? GroupPrincipalId { get; set; }
}

public class RancherProjectRoleBinding
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("roleTemplateId")]
    public string RoleTemplateId { get; set; } = string.Empty;

    [JsonPropertyName("userPrincipalId")]
    public string? UserPrincipalId { get; set; }

    [JsonPropertyName("groupPrincipalId")]
    public string? GroupPrincipalId { get; set; }
}

public class RancherProjectRoleBindingList
{
    [JsonPropertyName("data")]
    public List<RancherProjectRoleBinding> Data { get; set; } = new();
}

public class RancherPrincipal
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("principalType")]
    public string PrincipalType { get; set; } = string.Empty;
}

public class RancherPrincipalList
{
    [JsonPropertyName("data")]
    public List<RancherPrincipal> Data { get; set; } = new();
}

public class RancherNamespaceMoveRequest
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;
}

public class RancherPrincipalSearchRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("principalType")]
    public string? PrincipalType { get; set; }
}
