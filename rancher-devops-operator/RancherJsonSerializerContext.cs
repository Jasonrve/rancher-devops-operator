using System.Text.Json.Serialization;
using rancher_devops_operator.Models;
using rancher_devops_operator.Services;

namespace rancher_devops_operator;

[JsonSerializable(typeof(RancherClusterList))]
[JsonSerializable(typeof(RancherCluster))]
[JsonSerializable(typeof(RancherProject))]
[JsonSerializable(typeof(RancherProjectList))]
[JsonSerializable(typeof(RancherProjectRequest))]
[JsonSerializable(typeof(RancherResourceQuota))]
[JsonSerializable(typeof(RancherNamespace))]
[JsonSerializable(typeof(RancherNamespaceList))]
[JsonSerializable(typeof(RancherNamespaceRequest))]
[JsonSerializable(typeof(RancherProjectRoleBinding))]
[JsonSerializable(typeof(RancherProjectRoleBindingList))]
[JsonSerializable(typeof(RancherProjectRoleBindingRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(LoginResponse))]
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class RancherJsonSerializerContext : JsonSerializerContext
{
}
