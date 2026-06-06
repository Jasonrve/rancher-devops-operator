namespace rancher_devops_operator.Mcp;

public sealed class McpOptions
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 8080;
    public string TokenNamespace { get; set; } = "default";
    public string? BootstrapAdminTokenHash { get; set; }
    public string BootstrapAdminTokenSecretName { get; set; } = "mcp-bootstrap-admin-token";
}
