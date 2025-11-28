using KubeOps.Operator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using rancher_devops_operator;
using rancher_devops_operator.Services;

var builder = Host.CreateApplicationBuilder(args);

// Ensure environment variables are properly loaded
builder.Configuration.AddEnvironmentVariables();

// Ensure logging uses appsettings.json configuration only (no env overrides needed)
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.SingleLine = true;
});

// Add HttpClient for Rancher API
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

// Add separate HttpClient for authentication (without Bearer token initially)
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

// Register Rancher services
builder.Services.AddSingleton<IRancherAuthService, RancherAuthService>();
builder.Services.AddSingleton<IRancherApiService, RancherApiService>();
builder.Services.AddSingleton<IKubernetesEventService, KubernetesEventService>();

// Add namespace watch service for Observe policy (periodic polling)
builder.Services.AddHostedService<RancherNamespaceWatchService>();

// Add Kubernetes operator
builder.Services
    .AddKubernetesOperator()
    .RegisterComponents();

var host = builder.Build();

// Log startup configuration (non-sensitive values only)
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var config = host.Services.GetRequiredService<IConfiguration>();

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
logger.LogInformation("=============================================");

// Start Prometheus metrics server
var metricsServer = new KestrelMetricServer(port: 9090);
metricsServer.Start();

await host.RunAsync();
