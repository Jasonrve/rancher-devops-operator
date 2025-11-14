# Observability Guide

This guide explains the observability features of the Rancher DevOps Operator, including Prometheus metrics and Kubernetes events.

## Prometheus Metrics

The operator exposes Prometheus metrics on port **9090** at the `/metrics` endpoint.

### Available Metrics

#### Counter Metrics

| Metric Name | Type | Labels | Description |
|-------------|------|--------|-------------|
| `rancher_operator_reconciliations_total` | Counter | `resource_name`, `result` | Total number of reconciliations (success/failure) |
| `rancher_operator_projects_created_total` | Counter | - | Total number of Rancher projects created |
| `rancher_operator_projects_deleted_total` | Counter | - | Total number of Rancher projects deleted |
| `rancher_operator_namespaces_created_total` | Counter | - | Total number of namespaces created |
| `rancher_operator_namespaces_deleted_total` | Counter | - | Total number of namespaces deleted |
| `rancher_operator_members_added_total` | Counter | - | Total number of project members added |
| `rancher_operator_members_removed_total` | Counter | - | Total number of project members removed |
| `rancher_operator_api_calls_total` | Counter | `operation`, `result` | Total number of Rancher API calls |
| `rancher_operator_tokens_created_total` | Counter | - | Total number of authentication tokens created |
| `rancher_operator_errors_total` | Counter | `error_type` | Total number of errors by type |

#### Gauge Metrics

| Metric Name | Type | Description |
|-------------|------|-------------|
| `rancher_operator_active_projects` | Gauge | Number of active Rancher projects managed |
| `rancher_operator_active_namespaces` | Gauge | Number of active namespaces managed |
| `rancher_operator_active_members` | Gauge | Number of active project members managed |

#### Histogram Metrics

| Metric Name | Type | Labels | Description |
|-------------|------|--------|-------------|
| `rancher_operator_reconciliation_duration_seconds` | Histogram | `resource_name` | Duration of reconciliation operations |
| `rancher_operator_api_call_duration_seconds` | Histogram | `operation` | Duration of Rancher API calls |

### Accessing Metrics

#### Direct Access

```bash
# Port-forward to the operator pod
kubectl port-forward -n rancher-devops-system deployment/rancher-devops-operator 9090:9090

# Access metrics
curl http://localhost:9090/metrics
```

#### Example Metrics Output

```prometheus
# HELP rancher_operator_reconciliations_total Total number of reconciliations
# TYPE rancher_operator_reconciliations_total counter
rancher_operator_reconciliations_total{resource_name="my-project",result="success"} 5

# HELP rancher_operator_projects_created_total Total number of Rancher projects created
# TYPE rancher_operator_projects_created_total counter
rancher_operator_projects_created_total 3

# HELP rancher_operator_reconciliation_duration_seconds Duration of reconciliation operations in seconds
# TYPE rancher_operator_reconciliation_duration_seconds histogram
rancher_operator_reconciliation_duration_seconds_bucket{resource_name="my-project",le="0.01"} 0
rancher_operator_reconciliation_duration_seconds_bucket{resource_name="my-project",le="0.02"} 0
rancher_operator_reconciliation_duration_seconds_bucket{resource_name="my-project",le="0.04"} 1
rancher_operator_reconciliation_duration_seconds_sum{resource_name="my-project"} 0.035
rancher_operator_reconciliation_duration_seconds_count{resource_name="my-project"} 1

# HELP rancher_operator_active_projects Number of active Rancher projects managed by the operator
# TYPE rancher_operator_active_projects gauge
rancher_operator_active_projects 3
```

### Prometheus Integration

#### Using Prometheus Operator

Enable the ServiceMonitor in your Helm values:

```yaml
metrics:
  serviceMonitor:
    enabled: true
    interval: 30s
    scrapeTimeout: 10s
    labels:
      prometheus: kube-prometheus  # Match your Prometheus selector
```

Then install/upgrade:

```bash
helm upgrade --install rancher-devops-operator ./helm/rancher-devops-operator \
  --namespace rancher-devops-system \
  --set metrics.serviceMonitor.enabled=true
```

#### Manual Prometheus Configuration

Add to your `prometheus.yml`:

```yaml
scrape_configs:
  - job_name: 'rancher-devops-operator'
    kubernetes_sd_configs:
      - role: endpoints
        namespaces:
          names:
            - rancher-devops-system
    relabel_configs:
      - source_labels: [__meta_kubernetes_service_name]
        action: keep
        regex: rancher-devops-operator-metrics
      - source_labels: [__meta_kubernetes_endpoint_port_name]
        action: keep
        regex: metrics
```

### Example Prometheus Queries

```promql
# Reconciliation success rate
sum(rate(rancher_operator_reconciliations_total{result="success"}[5m])) 
/ 
sum(rate(rancher_operator_reconciliations_total[5m])) * 100

# Average reconciliation duration
rate(rancher_operator_reconciliation_duration_seconds_sum[5m]) 
/ 
rate(rancher_operator_reconciliation_duration_seconds_count[5m])

# API call error rate
sum(rate(rancher_operator_api_calls_total{result="failure"}[5m])) 
/ 
sum(rate(rancher_operator_api_calls_total[5m])) * 100

# Number of active projects
rancher_operator_active_projects

# Token creation rate (indicates authentication issues if high)
rate(rancher_operator_tokens_created_total[1h])
```

---

## Kubernetes Events

The operator creates Kubernetes events for important operations to provide visibility into what's happening.

### Event Types

#### Normal Events (type=Normal)

| Reason | Message | When |
|--------|---------|------|
| `ReconcileStarted` | Starting reconciliation | Beginning of reconciliation |
| `ClusterResolved` | Resolved cluster 'name' to ID: id | Cluster name successfully resolved |
| `ProjectFound` | Using existing Rancher project: name (ID: id) | Existing project found |
| `ProjectCreated` | Successfully created Rancher project: name (ID: id) | New project created |
| `NamespaceCreated` | Created namespace: name | Namespace created |
| `MemberAdded` | Added member: principalId with role: role | Member added to project |
| `ReconcileCompleted` | Successfully reconciled RancherProject | Reconciliation completed |
| `DeletionStarted` | Starting deletion of RancherProject | Beginning of deletion |
| `NamespaceDeleted` | Deleted namespace: name | Namespace deleted |
| `ProjectDeleted` | Successfully deleted RancherProject | Project deleted |

#### Warning Events (type=Warning)

| Reason | Message | When |
|--------|---------|------|
| `ClusterNotFound` | Cluster 'name' not found | Cluster name cannot be resolved |
| `ProjectCreationFailed` | Failed to create Rancher project | Project creation failed |
| `NamespaceCreationFailed` | Failed to create namespace: name - error | Namespace creation failed |
| `MemberAddFailed` | Failed to add member: principalId - error | Member addition failed |
| `ReconcileFailed` | Reconciliation failed: error | Reconciliation failed |
| `NamespaceDeletionFailed` | Failed to delete namespace: name - error | Namespace deletion failed |
| `DeletionFailed` | Deletion failed: error | Deletion failed |

### Viewing Events

#### For a specific RancherProject

```bash
kubectl describe rancherproject my-project

# Or view events directly
kubectl get events --field-selector involvedObject.name=my-project
```

#### All operator events

```bash
kubectl get events -n default --field-selector source=rancher-devops-operator

# Watch events in real-time
kubectl get events -n default --field-selector source=rancher-devops-operator --watch
```

#### Filter by event type

```bash
# Only warnings
kubectl get events --field-selector type=Warning,source=rancher-devops-operator

# Only normal events
kubectl get events --field-selector type=Normal,source=rancher-devops-operator
```

### Example Event Output

```bash
$ kubectl describe rancherproject my-project

Events:
  Type    Reason              Age   From                        Message
  ----    ------              ----  ----                        -------
  Normal  ReconcileStarted    2m    rancher-devops-operator     Starting reconciliation
  Normal  ClusterResolved     2m    rancher-devops-operator     Resolved cluster 'local' to ID: c-m-xyz123
  Normal  ProjectCreated      2m    rancher-devops-operator     Successfully created Rancher project: My Project (ID: c-m-xyz123:p-abc123)
  Normal  NamespaceCreated    2m    rancher-devops-operator     Created namespace: app-frontend
  Normal  NamespaceCreated    2m    rancher-devops-operator     Created namespace: app-backend
  Normal  MemberAdded         2m    rancher-devops-operator     Added member: local://user-abc123 with role: project-owner
  Normal  ReconcileCompleted  2m    rancher-devops-operator     Successfully reconciled RancherProject
```

---

## Grafana Dashboard

### Sample Dashboard JSON

Create a Grafana dashboard with these panels:

```json
{
  "dashboard": {
    "title": "Rancher DevOps Operator",
    "panels": [
      {
        "title": "Reconciliation Success Rate",
        "targets": [
          {
            "expr": "sum(rate(rancher_operator_reconciliations_total{result=\"success\"}[5m])) / sum(rate(rancher_operator_reconciliations_total[5m])) * 100"
          }
        ]
      },
      {
        "title": "Active Resources",
        "targets": [
          { "expr": "rancher_operator_active_projects", "legendFormat": "Projects" },
          { "expr": "rancher_operator_active_namespaces", "legendFormat": "Namespaces" },
          { "expr": "rancher_operator_active_members", "legendFormat": "Members" }
        ]
      },
      {
        "title": "API Call Duration (95th percentile)",
        "targets": [
          {
            "expr": "histogram_quantile(0.95, rate(rancher_operator_api_call_duration_seconds_bucket[5m]))"
          }
        ]
      },
      {
        "title": "Error Rate by Type",
        "targets": [
          {
            "expr": "rate(rancher_operator_errors_total[5m])"
          }
        ]
      }
    ]
  }
}
```

---

## Alerting

### Sample Prometheus Alert Rules

```yaml
groups:
  - name: rancher-devops-operator
    interval: 30s
    rules:
      - alert: HighReconciliationFailureRate
        expr: |
          sum(rate(rancher_operator_reconciliations_total{result="failure"}[5m])) 
          / 
          sum(rate(rancher_operator_reconciliations_total[5m])) > 0.1
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High reconciliation failure rate"
          description: "More than 10% of reconciliations are failing"

      - alert: SlowReconciliation
        expr: |
          histogram_quantile(0.95, 
            rate(rancher_operator_reconciliation_duration_seconds_bucket[5m])
          ) > 30
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Slow reconciliation operations"
          description: "95th percentile reconciliation time is over 30 seconds"

      - alert: HighAPIErrorRate
        expr: |
          sum(rate(rancher_operator_api_calls_total{result="failure"}[5m])) 
          / 
          sum(rate(rancher_operator_api_calls_total[5m])) > 0.05
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "High Rancher API error rate"
          description: "More than 5% of API calls are failing"

      - alert: FrequentTokenCreation
        expr: rate(rancher_operator_tokens_created_total[1h]) > 2
        for: 30m
        labels:
          severity: warning
        annotations:
          summary: "Frequent authentication token creation"
          description: "Operator is creating tokens frequently, may indicate authentication issues"

      - alert: OperatorDown
        expr: up{job="rancher-devops-operator"} == 0
        for: 5m
        labels:
          severity: critical
        annotations:
          summary: "Rancher DevOps Operator is down"
          description: "The operator pod is not responding to metrics scrapes"
```

---

## Troubleshooting with Metrics and Events

### High Error Rate

```bash
# Check error metrics
curl http://localhost:9090/metrics | grep rancher_operator_errors_total

# Check recent warning events
kubectl get events --field-selector type=Warning,source=rancher-devops-operator --sort-by='.lastTimestamp'
```

### Slow Reconciliation

```bash
# Check duration metrics
curl http://localhost:9090/metrics | grep rancher_operator_reconciliation_duration

# Check which resources are being reconciled
kubectl get rancherprojects -o wide
```

### Authentication Issues

```bash
# Check token creation rate
curl http://localhost:9090/metrics | grep rancher_operator_tokens_created_total

# Check operator logs
kubectl logs -n rancher-devops-system deployment/rancher-devops-operator | grep -i "auth\|token"
```

---

## Best Practices

1. **Set up alerts** for critical metrics (error rates, slow operations)
2. **Monitor token creation rate** - high rates indicate auth problems
3. **Watch for Warning events** - they indicate issues before failures
4. **Set up dashboards** for at-a-glance operational status
5. **Retain metrics** for at least 30 days for trend analysis
6. **Correlate metrics with events** for better troubleshooting

---

## References

- [Prometheus Documentation](https://prometheus.io/docs/)
- [Kubernetes Events](https://kubernetes.io/docs/reference/kubernetes-api/cluster-resources/event-v1/)
- [Prometheus Operator](https://prometheus-operator.dev/)
- [Grafana Dashboards](https://grafana.com/docs/grafana/latest/dashboards/)
