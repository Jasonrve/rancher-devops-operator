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

// Start Prometheus metrics server
var metricsServer = new KestrelMetricServer(port: 9090);
metricsServer.Start();

await host.RunAsync();
