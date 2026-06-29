# Installation and Configuration

The Helm chart is the primary way to deploy the operator.

## Chart location

- Chart: `helm/rancher-devops-operator`
- Default image: `ghcr.io/jasonrve/rancher-devops-operator:0.5.1`
- Metrics port: `9090`

## Quick install

```bash
helm upgrade --install rancher-devops-operator ./helm/rancher-devops-operator \
  --namespace rancher-devops-system \
  --create-namespace \
  --set rancher.url=https://rancher.example.com \
  --set rancher.existingSecret=rancher-creds
```

## Values you are most likely to change

From `helm/rancher-devops-operator/values.yaml`:

| Key | Default | Purpose |
| --- | --- | --- |
| `image.repository` | `ghcr.io/jasonrve/rancher-devops-operator` | Container image repository. |
| `image.tag` | `0.5.1` | Image tag. |
| `rancher.url` | `https://rancher.local` | Rancher base URL. |
| `rancher.token` | `''` | Static API token. |
| `rancher.username` | `''` | Username for auto-token flow. |
| `rancher.password` | `''` | Password for auto-token flow. |
| `rancher.allowInsecureSsl` | `false` | Skip certificate validation for development use only. |
| `rancher.existingSecret` | `''` | Reference a secret instead of plain values. |
| `env.ClusterCheckInterval` | `5` | Minutes between cluster discovery loops. |
| `env.ObserveMethod` | `watch` | `watch`, `poll`, or `none`. |
| `env.PollingInterval` | `2` | Minutes between poll loops when `ObserveMethod=poll`. |
| `env.CleanupNamespaces` | `false` | Delete removed namespaces only when enabled and allowed by policy. |

## Authentication options

The code accepts either:

1. `Rancher:Token` / `rancher.token`
2. `Rancher:Username` + `Rancher:Password` / `rancher.username` + `rancher.password`
3. `rancher.existingSecret` with configurable keys

### Environment variables recognized by the operator

- `Rancher__Url`
- `Rancher__Token`
- `Rancher__Username`
- `Rancher__Password`
- `Rancher__AllowInsecureSsl`
- `Rancher__CleanupNamespaces`
- `ObserveMethod`
- `ClusterCheckInterval`
- `PollingInterval`

## ServiceMonitor

Prometheus scraping can be enabled with the chart's ServiceMonitor block:

```yaml
metrics:
  serviceMonitor:
    enabled: true
    interval: 30s
    scrapeTimeout: 10s
```

## Runtime behavior that matters during install

- The operator listens on port `9090` for metrics.
- The namespace watch service can run in `watch`, `poll`, or `none` mode.
- `Observe` on the CRD controls whether existing project state gets imported.
- `CleanupNamespaces=true` only matters if namespace delete is also allowed by the CRD policy.
- A static token is the simplest production setup; username/password is mainly for automatic token creation.
