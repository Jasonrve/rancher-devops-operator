using k8s;
using k8s.Models;

namespace rancher_devops_operator.Mcp;

public interface IMcpSecretClient
{
    Task<V1SecretList> ListSecretsAsync(string namespaceName, string labelSelector, CancellationToken cancellationToken);
    Task CreateSecretAsync(string namespaceName, V1Secret secret, CancellationToken cancellationToken);
    Task DeleteSecretAsync(string namespaceName, string secretName, CancellationToken cancellationToken);
}

public sealed class KubernetesMcpSecretClient : IMcpSecretClient
{
    private readonly Lazy<Kubernetes> _client;

    public KubernetesMcpSecretClient()
    {
        _client = new Lazy<Kubernetes>(() => new Kubernetes(KubernetesClientConfiguration.InClusterConfig()));
    }

    public Task<V1SecretList> ListSecretsAsync(string namespaceName, string labelSelector, CancellationToken cancellationToken)
    {
        return Client().ListNamespacedSecretAsync(namespaceName, labelSelector: labelSelector, cancellationToken: cancellationToken);
    }

    public async Task CreateSecretAsync(string namespaceName, V1Secret secret, CancellationToken cancellationToken)
    {
        await Client().CreateNamespacedSecretAsync(secret, namespaceName, cancellationToken: cancellationToken);
    }

    public async Task DeleteSecretAsync(string namespaceName, string secretName, CancellationToken cancellationToken)
    {
        await Client().DeleteNamespacedSecretAsync(secretName, namespaceName, cancellationToken: cancellationToken);
    }

    private Kubernetes Client() => _client.Value;
}
