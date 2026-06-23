using KubeOps.Operator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using rancher_devops_operator;
using rancher_devops_operator.Mcp;
using rancher_devops_operator.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.SingleLine = true;
});

var mcpOptions = builder.Configuration.GetSection("Mcp").Get<McpOptions>() ?? new McpOptions();
builder.WebHost.UseUrls($"http://0.0.0.0:{mcpOptions.Port}");

builder.Services.AddHttpClient("Rancher")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        var allowInsecureSsl = builder.Configuration.GetValue<bool>("Rancher:AllowInsecureSsl");
        if (allowInsecureSsl)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        return handler;
    });

builder.Services.AddHttpClient("RancherAuth")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new HttpClientHandler();
        var allowInsecureSsl = builder.Configuration.GetValue<bool>("Rancher:AllowInsecureSsl");
        if (allowInsecureSsl)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        return handler;
    });

builder.Services.AddSingleton<IRancherPassthroughTokenContext, RancherPassthroughTokenContext>();
builder.Services.AddSingleton<IRancherAuthService, RancherAuthService>();
builder.Services.AddSingleton<IRancherApiService, RancherApiService>();
builder.Services.AddSingleton<IKubernetesEventService, KubernetesEventService>();
builder.Services.AddSingleton<IMcpSecretClient, KubernetesMcpSecretClient>();
builder.Services.AddSingleton<IMcpTokenStore, KubernetesSecretMcpTokenStore>();
builder.Services.AddSingleton<IMcpToolCatalog, McpToolCatalog>();
builder.Services.AddSingleton<IMcpToolExecutor, McpToolExecutor>();
builder.Services.AddSingleton<McpServer>();

builder.Services.AddHostedService<RancherNamespaceWatchService>();

builder.Services
    .AddKubernetesOperator()
    .RegisterComponents();

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var config = app.Services.GetRequiredService<IConfiguration>();

logger.LogInformation("=== Rancher DevOps Operator Configuration ===");
logger.LogInformation("Rancher URL: {Url}", config.GetValue<string>("Rancher:Url", "not-set"));
logger.LogInformation("Allow Insecure SSL: {AllowInsecureSsl}", config.GetValue<bool>("Rancher:AllowInsecureSsl"));
logger.LogInformation("Cleanup Namespaces: {CleanupNamespaces}",
    config.GetValue<bool>("CleanupNamespaces", config.GetValue<bool>("Rancher:CleanupNamespaces", false)));
logger.LogInformation("Observe Method: {ObserveMethod}",
    config.GetValue<string>("ObserveMethod", config.GetValue<string>("Rancher:ObserveMethod", "watch")));
logger.LogInformation("Cluster Check Interval: {ClusterCheckInterval} minutes",
    config.GetValue<int>("ClusterCheckInterval", config.GetValue<int>("Rancher:ClusterCheckInterval", 5)));
logger.LogInformation("Polling Interval: {PollingInterval} minutes",
    config.GetValue<int>("PollingInterval", config.GetValue<int>("Rancher:PollingInterval", 2)));
logger.LogInformation("Auth Method: {AuthMethod}",
    !string.IsNullOrEmpty(config.GetValue<string>("Rancher:Token")) ? "Token" :
    (!string.IsNullOrEmpty(config.GetValue<string>("Rancher:Username")) ? "Username/Password" : "Not configured"));
logger.LogInformation("MCP Enabled: {Enabled} on port {Port}", mcpOptions.Enabled, mcpOptions.Port);
logger.LogInformation("=============================================");

if (mcpOptions.Enabled)
{
    using var scope = app.Services.CreateScope();
    var tokenStore = scope.ServiceProvider.GetRequiredService<IMcpTokenStore>();
    await tokenStore.SeedBootstrapTokenAsync(CancellationToken.None);

    app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
    app.MapPost("/mcp", async (HttpContext ctx, McpServer server, CancellationToken ct) => await server.HandleAsync(ctx, ct));
}

var metricsServer = new KestrelMetricServer(port: 9090);
metricsServer.Start();

await app.RunAsync();
