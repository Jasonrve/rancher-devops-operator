using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using rancher_devops_operator.Mcp;

using Xunit;
namespace rancher_devops_operator.Tests;

public class McpTokenStoreTests
{
    [Fact]
    public async Task CreateAsync_StoresOnlyHashAndRole_AndResolveAsyncFindsToken()
    {
        var secretClient = new InMemoryMcpSecretClient();
        var store = new KubernetesSecretMcpTokenStore(
            secretClient,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:TokenNamespace"] = "mcp-system",
            }).Build(),
            NullLogger<KubernetesSecretMcpTokenStore>.Instance);

        var created = await store.CreateAsync(McpRole.Admin, CancellationToken.None);

        Assert.StartsWith("mcp_", created.RawToken);
        Assert.NotEqual(created.RawToken, created.TokenHash);

        var stored = Assert.Single(secretClient.Secrets);
        Assert.Equal("mcp-system", stored.Metadata!.NamespaceProperty);
        Assert.Equal("admin", stored.StringData!["role"]);
        Assert.Equal(created.TokenHash, stored.StringData["tokenHash"]);
        Assert.Equal(created.TokenHash, stored.Metadata.Annotations!["mcp.devops.io/token-hash"]);
        Assert.DoesNotContain(stored.StringData.Values, value => value == created.RawToken);

        var resolved = await store.ResolveAsync(created.RawToken, CancellationToken.None);
        Assert.NotNull(resolved);
        Assert.Equal(McpRole.Admin, resolved!.Role);
        Assert.Equal(created.TokenHash, resolved.TokenHash);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTokenSecret()
    {
        var secretClient = new InMemoryMcpSecretClient();
        var store = new KubernetesSecretMcpTokenStore(
            secretClient,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:TokenNamespace"] = "mcp-system",
            }).Build(),
            NullLogger<KubernetesSecretMcpTokenStore>.Instance);

        var created = await store.CreateAsync(McpRole.Viewer, CancellationToken.None);

        var deleted = await store.DeleteAsync(created.SecretName, CancellationToken.None);

        Assert.True(deleted);
        Assert.Empty(secretClient.Secrets);
    }

    private sealed class InMemoryMcpSecretClient : IMcpSecretClient
    {
        public List<V1Secret> Secrets { get; } = new();

        public Task<V1SecretList> ListSecretsAsync(string namespaceName, string labelSelector, CancellationToken cancellationToken)
        {
            var list = new V1SecretList
            {
                Items = Secrets.Select(Clone).ToList(),
            };
            return Task.FromResult(list);
        }

        public Task CreateSecretAsync(string namespaceName, V1Secret secret, CancellationToken cancellationToken)
        {
            Secrets.Add(Clone(secret));
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(string namespaceName, string secretName, CancellationToken cancellationToken)
        {
            Secrets.RemoveAll(secret => string.Equals(secret.Metadata?.Name, secretName, StringComparison.Ordinal));
            return Task.CompletedTask;
        }

        private static V1Secret Clone(V1Secret secret)
            => new()
            {
                ApiVersion = secret.ApiVersion,
                Kind = secret.Kind,
                Metadata = new V1ObjectMeta
                {
                    Name = secret.Metadata?.Name,
                    NamespaceProperty = secret.Metadata?.NamespaceProperty,
                    Labels = secret.Metadata?.Labels is null ? null : new Dictionary<string, string>(secret.Metadata.Labels),
                    Annotations = secret.Metadata?.Annotations is null ? null : new Dictionary<string, string>(secret.Metadata.Annotations),
                },
                StringData = secret.StringData is null ? null : new Dictionary<string, string>(secret.StringData),
                Data = secret.Data is null ? null : secret.Data.ToDictionary(entry => entry.Key, entry => entry.Value),
                Type = secret.Type,
            };
    }
}
