

<p align="center">
  <a href="https://github.com/Jasonrve/rancher-devops-operator">
    <img src="https://raw.githubusercontent.com/Jasonrve/rancher-devops-operator/main/resources/images/project-icon-transparent.png" width="200" alt="Rancher DevOps Operator"/>
  </a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/status-UNDER_HEAVY_DEVELOPMENT-orange?style=for-the-badge" alt="Project Status: Under Heavy Development" />
</p>

<h1 align="center">Rancher DevOps Operator</h1>

<div align="center" width="100%">
    <p>
        <a href="https://github.com/Jasonrve/rancher-devops-operator/actions"><img alt="GitHub Actions Workflow Status" src="https://img.shields.io/github/actions/workflow/status/Jasonrve/rancher-devops-operator/release.yml?style=flat&logo=github&link=https%3A%2F%2Fgithub.com%2FJasonrve%2Francher-devops-operator%2Factions"></a>
        <a href="https://github.com/Jasonrve/rancher-devops-operator/releases/latest"><img alt="GitHub Tag" src="https://img.shields.io/github/v/tag/Jasonrve/rancher-devops-operator?logo=github&label=latest"></a>
        <a href="https://ghcr.io/Jasonrve/rancher-devops-operator"><img alt="GHCR Tag" src="https://img.shields.io/github/v/tag/Jasonrve/rancher-devops-operator?logo=docker&logoColor=white&label=GHCR"></a>
        <img src="https://img.shields.io/badge/Docker-arm64-blue?logo=docker&logoColor=white" alt="Docker arm64 Support Badge">
        <img src="https://img.shields.io/badge/Docker-amd64-blue?logo=docker&logoColor=white" alt="Docker amd64 Support Badge">
        <img src="https://img.shields.io/badge/container_size-57MB-green" alt="Container Size Badge">
        <img src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 9 Badge">
    </p>
</div>

---

A Kubernetes operator for managing Rancher projects and namespaces declaratively using Custom Resource Definitions (CRDs).

## Features

- **Declarative Project Management**: Define Rancher projects using Kubernetes CRDs
- **Namespace Management**: Automatically create and link namespaces to projects
- **Member Management**: Configure project members and their roles
- **Cluster Name Resolution**: Reference clusters by name instead of cluster IDs
- **Resource Quotas**: Set project-level resource quotas (optional)
- **Flexible Authentication**: Supports both API tokens and username/password with automatic token management
- **Observability**: Prometheus metrics and Kubernetes events for full visibility
 

## Prerequisites

- Kubernetes cluster (1.24+)
- Rancher (2.7+)
- Helm 3
- Rancher authentication (either API token or username/password)

## Quick Start

See [AUTHENTICATION.md](AUTHENTICATION.md) for detailed authentication setup guide.

## Installation

### Using Helm

1. Clone the repository:
```bash
git clone https://github.com/Jasonrve/rancher-devops-operator.git
cd rancher-devops-operator
```

2. Create a values file or update the default values:

**Option 1: Using API Token (Recommended for Production)**
```yaml
rancher:
  url: "https://your-rancher-server"
  token: "your-rancher-api-token"
  allowInsecureSsl: false  # Set to true for development with self-signed certs

image:
  repository: ghcr.io/Jasonrve/rancher-devops-operator
  tag: "latest"
```

**Option 2: Using Username/Password (Auto-creates tokens)**
```yaml
rancher:
  url: "https://your-rancher-server"
  username: "admin"
  password: "your-password"
  allowInsecureSsl: false

image:
  repository: ghcr.io/Jasonrve/rancher-devops-operator
  tag: "latest"
```

**Option 3: Using Existing Secret**
```bash
# Create a secret with token
kubectl create secret generic rancher-creds \
  --namespace rancher-devops-system \
  --from-literal=token=your-token

# Or with username/password
kubectl create secret generic rancher-creds \
  --namespace rancher-devops-system \
  --from-literal=username=admin \
  --from-literal=password=your-password
```

```yaml
rancher:
  url: "https://your-rancher-server"
  existingSecret: "rancher-creds"
  allowInsecureSsl: false

image:
  repository: ghcr.io/Jasonrve/rancher-devops-operator
  tag: "latest"
```

3. Install the operator:
```bash
helm install rancher-devops-operator ./helm/rancher-devops-operator \
  --namespace rancher-devops-system \
  --create-namespace \
  --values your-values.yaml
```

## Usage

### Creating a Rancher Project

Create a Project custom resource:

```yaml
apiVersion: rancher.devops.io/v1
kind: Project
metadata:
  name: my-project
spec:
  clusterName: "local"  # Name of your Rancher cluster
  displayName: "My Application Project"
  description: "Project for my application"
  
  namespaces:
    - app-frontend
    - app-backend
    - app-database
  
  members:
    - principalId: "local://user-abc123"
      role: "project-owner"
    - principalId: "local://user-def456"
      role: "project-member"
  
  resourceQuota:
    limitsCpu: "10"
    limitsMemory: "20Gi"
    requestsCpu: "2"
    requestsMemory: "4Gi"
```

Apply the resource:
```bash
kubectl apply -f rancher-project.yaml
```

### Checking Status

```bash
kubectl get projects
kubectl describe project my-project
```

### Common Roles

- `project-owner`: Full access to the project
- `project-member`: Standard member access
- `read-only`: Read-only access

## Development

### Prerequisites

- .NET 9 SDK
- Docker
- Kubernetes cluster (k3d, kind, minikube, or real cluster)

### Building

```bash
dotnet build
```

### Running Locally

1. Update `appsettings.json` or set environment variables:

**Option 1: Using Token**
```json
{
  "Rancher": {
    "Url": "https://rancher.local",
    "Token": "your-token-here",
    "AllowInsecureSsl": true
  }
}
```

**Option 2: Using Username/Password**
```json
{
  "Rancher": {
    "Url": "https://rancher.local",
    "Username": "admin",
    "Password": "your-password",
    "AllowInsecureSsl": true
  }
}
```

**Or set environment variables:**
```bash
# Using token
export Rancher__Url="https://rancher.local"
export Rancher__Token="your-token-here"
export Rancher__AllowInsecureSsl=true

# Or using username/password
export Rancher__Url="https://rancher.local"
export Rancher__Username="admin"
export Rancher__Password="your-password"
export Rancher__AllowInsecureSsl=true
```

2. Run the operator:
```bash
cd rancher-devops-operator
dotnet run
```

### Building Docker Image

```bash
docker build -t rancher-devops-operator:latest .
```

### Running Tests

```bash
dotnet test
```

## Architecture

The operator consists of:

- **V1Project CRD**: Defines the desired state of Rancher projects
- **ProjectController**: Reconciles the CRD with actual Rancher state
- **RancherApiService**: Handles communication with the Rancher API
- **RancherAuthService**: Manages authentication (token or username/password)
- **MetricsService**: Exposes Prometheus metrics on port 9090
- **KubernetesEventService**: Creates Kubernetes events for visibility
 

## Observability

The operator provides comprehensive observability through:

- **Prometheus Metrics**: Exposed on port 9090 (`/metrics`)
  - Reconciliation success/failure rates
  - API call duration and error rates
  - Active resource counts
  - Token creation metrics
- **Kubernetes Events**: Created for all major operations
  - Project creation/deletion
  - Namespace management
  - Member additions
  - Errors and warnings

See [OBSERVABILITY.md](OBSERVABILITY.md) for detailed metrics documentation and Grafana dashboard examples.

### Quick Metrics Access

```bash
# Port-forward to metrics endpoint
kubectl port-forward -n rancher-devops-system deployment/rancher-devops-operator 9090:9090

# View metrics
curl http://localhost:9090/metrics
```

### View Events

```bash
# For a specific project
kubectl describe project my-project

# All operator events
kubectl get events --field-selector source=rancher-devops-operator
```

## Configuration

### Environment Variables

- `Rancher__Url`: Rancher server URL
- `Rancher__Token`: Rancher API token
- `Rancher__AllowInsecureSsl`: Allow insecure SSL (development only)

### Rancher API Token

The operator requires a Rancher API token with the following permissions:
- Manage projects
- Manage namespaces
- Manage project members

To create a token:
1. Log in to Rancher
2. Go to User Settings → API & Keys
3. Create a new API key
4. Save the token securely

## Troubleshooting

### Check operator logs:
```bash
kubectl logs -n rancher-devops-system deployment/rancher-devops-operator
```

### Common Issues

1. **Cluster not found**: Ensure `clusterName` matches the exact cluster name in Rancher
2. **Authentication errors**: Verify your Rancher API token is valid and has correct permissions
3. **SSL errors**: Set `allowInsecureSsl: true` for self-signed certificates (development only)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

[Your License Here]

## Support

For issues and questions, please open an issue on GitHub.
