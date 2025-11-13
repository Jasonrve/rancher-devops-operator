using Prometheus;

namespace rancher_devops_operator.Services;

public static class MetricsService
{
    // Counter metrics
    public static readonly Counter ReconciliationsTotal = Metrics.CreateCounter(
        "rancher_operator_reconciliations_total",
        "Total number of reconciliations",
        new CounterConfiguration
        {
            LabelNames = new[] { "resource_name", "result" }
        });

    public static readonly Counter ProjectsCreated = Metrics.CreateCounter(
        "rancher_operator_projects_created_total",
        "Total number of Rancher projects created");

    public static readonly Counter ProjectsDeleted = Metrics.CreateCounter(
        "rancher_operator_projects_deleted_total",
        "Total number of Rancher projects deleted");

    public static readonly Counter NamespacesCreated = Metrics.CreateCounter(
        "rancher_operator_namespaces_created_total",
        "Total number of namespaces created");

    public static readonly Counter NamespacesDeleted = Metrics.CreateCounter(
        "rancher_operator_namespaces_deleted_total",
        "Total number of namespaces deleted");

    public static readonly Counter MembersAdded = Metrics.CreateCounter(
        "rancher_operator_members_added_total",
        "Total number of project members added");

    public static readonly Counter MembersRemoved = Metrics.CreateCounter(
        "rancher_operator_members_removed_total",
        "Total number of project members removed");

    public static readonly Counter ApiCallsTotal = Metrics.CreateCounter(
        "rancher_operator_api_calls_total",
        "Total number of Rancher API calls",
        new CounterConfiguration
        {
            LabelNames = new[] { "operation", "result" }
        });

    public static readonly Counter TokensCreated = Metrics.CreateCounter(
        "rancher_operator_tokens_created_total",
        "Total number of authentication tokens created");

    public static readonly Counter ErrorsTotal = Metrics.CreateCounter(
        "rancher_operator_errors_total",
        "Total number of errors",
        new CounterConfiguration
        {
            LabelNames = new[] { "error_type" }
        });

    // Gauge metrics
    public static readonly Gauge ActiveProjects = Metrics.CreateGauge(
        "rancher_operator_active_projects",
        "Number of active Rancher projects managed by the operator");

    public static readonly Gauge ActiveNamespaces = Metrics.CreateGauge(
        "rancher_operator_active_namespaces",
        "Number of active namespaces managed by the operator");

    public static readonly Gauge ActiveMembers = Metrics.CreateGauge(
        "rancher_operator_active_members",
        "Number of active project members managed by the operator");

    // Histogram metrics
    public static readonly Histogram ReconciliationDuration = Metrics.CreateHistogram(
        "rancher_operator_reconciliation_duration_seconds",
        "Duration of reconciliation operations in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "resource_name" },
            Buckets = Histogram.ExponentialBuckets(0.01, 2, 10)
        });

    public static readonly Histogram ApiCallDuration = Metrics.CreateHistogram(
        "rancher_operator_api_call_duration_seconds",
        "Duration of Rancher API calls in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "operation" },
            Buckets = Histogram.ExponentialBuckets(0.01, 2, 10)
        });

    // Helper methods
    public static void RecordReconciliation(string resourceName, bool success)
    {
        ReconciliationsTotal.WithLabels(resourceName, success ? "success" : "failure").Inc();
    }

    public static void RecordApiCall(string operation, bool success, double duration)
    {
        ApiCallsTotal.WithLabels(operation, success ? "success" : "failure").Inc();
        ApiCallDuration.WithLabels(operation).Observe(duration);
    }

    public static void RecordError(string errorType)
    {
        ErrorsTotal.WithLabels(errorType).Inc();
    }
}
