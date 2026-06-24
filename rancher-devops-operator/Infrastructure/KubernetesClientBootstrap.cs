using k8s;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace rancher_devops_operator.Infrastructure;

public static class KubernetesClientBootstrap
{
    private const string KubeconfigEnv = "KUBECONFIG";
    private const string KubeconfigContentEnv = "KUBECONFIG_CONTENT";
    private const string TempKubeconfigDirectory = "/tmp/rancher-devops-operator";

    public static string? ApplyKubeconfigEnvironment(IConfiguration configuration, ILogger? logger = null)
    {
        var inlineContent = configuration.GetValue<string>("Kubernetes:KubeconfigContent")
            ?? Environment.GetEnvironmentVariable(KubeconfigContentEnv);

        if (!string.IsNullOrWhiteSpace(inlineContent))
        {
            Directory.CreateDirectory(TempKubeconfigDirectory);
            var filePath = Path.Combine(TempKubeconfigDirectory, "kubeconfig");
            File.WriteAllText(filePath, inlineContent);
            Environment.SetEnvironmentVariable(KubeconfigEnv, filePath);
            logger?.LogInformation("Applied Kubernetes configuration from inline kubeconfig content at {Path}", filePath);
            return filePath;
        }

        var configuredPath = configuration.GetValue<string>("Kubernetes:KubeconfigPath")
            ?? Environment.GetEnvironmentVariable(KubeconfigEnv);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (File.Exists(configuredPath))
            {
                Environment.SetEnvironmentVariable(KubeconfigEnv, configuredPath);
                logger?.LogInformation("Applied Kubernetes configuration from kubeconfig path {Path}", configuredPath);
                return configuredPath;
            }

            logger?.LogWarning("Configured Kubernetes kubeconfig path {Path} does not exist", configuredPath);
        }

        return Environment.GetEnvironmentVariable(KubeconfigEnv);
    }

    public static bool HasKubernetesCredentials(IConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration.GetValue<string>("Kubernetes:KubeconfigContent"))
            || !string.IsNullOrWhiteSpace(configuration.GetValue<string>("Kubernetes:KubeconfigPath"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KubeconfigEnv))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
    }

    public static KubernetesClientConfiguration CreateClientConfiguration()
    {
        try
        {
            return KubernetesClientConfiguration.BuildDefaultConfig();
        }
        catch
        {
            try
            {
                return KubernetesClientConfiguration.InClusterConfig();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Unable to build a Kubernetes client configuration. Provide Kubernetes:KubeconfigPath, Kubernetes:KubeconfigContent, or run inside a Kubernetes cluster.",
                    ex);
            }
        }
    }
}
