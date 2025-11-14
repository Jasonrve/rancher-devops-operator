# Rancher DevOps Operator - Project Summary

## Overview
A Kubernetes operator built with .NET 9 and KubeOps for managing Rancher projects and namespaces declaratively using Custom Resource Definitions (CRDs).

## Key Features
- **Declarative Project Management**: Define Rancher projects using Kubernetes CRDs
- **Namespace Management**: Automatically create and link namespaces to projects
- **Member Management**: Configure project members and their roles
- **Cluster Name Resolution**: Reference clusters by name instead of cluster IDs
- **Resource Quotas**: Set project-level resource quotas (optional)
- **Flexible Authentication**: Supports both API tokens and username/password with automatic token management
- **Observability**: Prometheus metrics and Kubernetes events for full operational visibility
- **Native AOT**: Compiled with .NET 9 Native AOT for fast startup (24.7 MB container) and reduced memory footprint

## Project Structure

```
rancher-devops-operator/
├── .github/
│   └── workflows/
│       └── build.yml                  # GitHub Actions workflow for building and pushing Docker images
├── .vscode/
│   ├── launch.json                    # VS Code debug configuration
│   └── tasks.json                     # VS Code build tasks
├── examples/
│   └── rancher-project-sample.yaml    # Sample RancherProject CRD
├── helm/
│   └── rancher-devops-operator/
│       ├── Chart.yaml
│       ├── values.yaml
│       └── templates/
│           ├── _helpers.tpl
│           ├── clusterrole.yaml       # Cluster-wide permissions
│           ├── clusterrolebinding.yaml
│           ├── crd.yaml               # RancherProject CRD definition
│           ├── deployment.yaml
│           ├── secret.yaml
│           └── serviceaccount.yaml
├── rancher-devops-operator/
│   ├── Controller/
│   │   └── RancherProjectController.cs  # Main reconciliation logic
│   ├── Entities/
│   │   └── V1RancherProject.cs       # CRD entity definition
│   ├── Models/
│   │   └── RancherApiModels.cs       # Rancher API models
│   ├── Services/
│   │   ├── RancherApiService.cs      # Rancher API client
│   │   ├── RancherAuthService.cs     # Authentication and token management
│   │   ├── MetricsService.cs         # Prometheus metrics
│   │   └── KubernetesEventService.cs # Kubernetes events
│   ├── Program.cs                     # Application entry point
│   ├── RancherJsonSerializerContext.cs # AOT-compatible JSON serialization
│   ├── appsettings.json              # Application configuration
│   └── rancher-devops-operator.csproj
├── .dockerignore
├── .gitignore
├── Dockerfile                         # Multi-stage build with AOT support
├── README.md
└── rancher-devops-operator.sln

```

## Components

### 1. Custom Resource Definition (CRD)
**File**: `Entities/V1RancherProject.cs`

Defines the `RancherProject` custom resource with:
- `clusterName`: Name of the Rancher cluster
- `displayName`: Project display name
- `description`: Optional project description
- `namespaces`: List of namespaces to create
- `members`: Project members with roles
- `resourceQuota`: Optional resource quotas

### 2. Controller
**File**: `Controller/RancherProjectController.cs`

Implements `IEntityController<V1RancherProject>` with:
- `ReconcileAsync`: Creates/updates Rancher projects, namespaces, and members
- `DeletedAsync`: Cleans up Rancher resources when CRD is deleted

Reconciliation Flow:
1. Resolve cluster name to cluster ID
2. Create or get Rancher project
3. Create namespaces and link to project
4. Configure project members and roles
5. Update CRD status

### 3. Rancher Authentication Service
**File**: `Services/RancherAuthService.cs`

Manages authentication with Rancher API supporting two methods:

#### Token-based Authentication (Recommended for Production)
- Uses static API token provided via configuration
- No token expiry or rotation
- Simpler configuration
- Best for service accounts and long-lived deployments

#### Username/Password Authentication
- Automatically creates API tokens via Rancher login API
- Tokens are cached and automatically renewed (12-hour TTL)
- Good for development or when token rotation is required
- Thread-safe token management with semaphore locking

Features:
- Automatic token creation and caching
- Token expiry management
- Thread-safe concurrent access
- Configurable via environment variables or secrets

### 4. Rancher API Service
**File**: `Services/RancherApiService.cs`

Provides methods for interacting with the Rancher API:
- Cluster management (get cluster by name)
- Project operations (create, get, delete)
- Namespace management (create, list, delete)
- Member management (add, list, remove)

Automatically ensures authentication before each API call using `RancherAuthService`.
Uses source-generated JSON serialization for AOT compatibility.

### 5. JSON Serialization Context
**File**: `RancherJsonSerializerContext.cs`

AOT-compatible JSON serialization using System.Text.Json source generators.

## Configuration

### Authentication Methods

The operator supports two authentication methods:

#### 1. Static API Token (Recommended for Production)
Set via environment variables:
```bash
Rancher__Url=https://rancher.local
Rancher__Token=token-xxxxx:xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
Rancher__AllowInsecureSsl=false
```

Or in Helm values:
```yaml
rancher:
  url: "https://rancher.local"
  token: "token-xxxxx:xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
  allowInsecureSsl: false
```

#### 2. Username/Password (Auto-creates tokens)
Set via environment variables:
```bash
Rancher__Url=https://rancher.local
Rancher__Username=admin
Rancher__Password=your-password
Rancher__AllowInsecureSsl=false
```

Or in Helm values:
```yaml
rancher:
  url: "https://rancher.local"
  username: "admin"
  password: "your-password"
  allowInsecureSsl: false
```

When using username/password:
- The operator automatically creates API tokens via the Rancher login API
- Tokens have a 12-hour TTL and are cached in memory
- Tokens are automatically renewed when expired
- Thread-safe implementation prevents concurrent token creation

#### 3. Using Kubernetes Secrets
Best practice for production - store credentials in Kubernetes secrets:

```bash
# For token authentication
kubectl create secret generic rancher-creds \
  --namespace rancher-devops-system \
  --from-literal=token=token-xxxxx:xxxxxxxx

# For username/password authentication
kubectl create secret generic rancher-creds \
  --namespace rancher-devops-system \
  --from-literal=username=admin \
  --from-literal=password=your-password
```

Then reference in Helm:
```yaml
rancher:
  url: "https://rancher.local"
  existingSecret: "rancher-creds"
  allowInsecureSsl: false
```

### Environment Variables
- `Rancher__Url`: Rancher server URL (e.g., "https://rancher.local") - **Required**
- `Rancher__Token`: Rancher API token for authentication - **Required if not using username/password**
- `Rancher__Username`: Rancher username - **Required if not using token**
- `Rancher__Password`: Rancher password - **Required if not using token**
- `Rancher__AllowInsecureSsl`: Allow self-signed SSL certificates (development only) - Optional, default: false

### Rancher API Permissions
Required permissions for the token or user:
- Manage projects (create, update, delete)
- Manage namespaces (create, update, delete)
- Manage project role template bindings (members)
- View clusters (to resolve cluster names to IDs)

## Deployment

### Using Helm

```bash
helm install rancher-devops-operator ./helm/rancher-devops-operator \
  --namespace rancher-devops-system \
  --create-namespace \
  --set rancher.url="https://your-rancher-server" \
  --set rancher.token="your-api-token"
```

### Docker Build

The Dockerfile uses a multi-stage build:
1. **Build Stage**: Uses .NET SDK Alpine image with native build tools (clang, build-base)
2. **Final Stage**: Uses .NET runtime-deps Alpine image (minimal runtime)

```bash
docker build -t rancher-devops-operator:latest .
```

## GitHub Actions Workflow

**File**: `.github/workflows/build.yml`

Workflow features:
- Manual trigger (workflow_dispatch)
- Multi-architecture support (linux/amd64, linux/arm64)
- Builds and pushes to GitHub Container Registry (ghcr.io)
- Build provenance attestation
- Layer caching for faster builds

## Development

### Prerequisites
- .NET 9 SDK
- Docker
- Kubernetes cluster with Rancher

### Debugging

Use the provided VS Code launch configuration:
1. Set environment variables in `.vscode/launch.json`
2. Press F5 to start debugging

### Building Locally

```bash
cd rancher-devops-operator
dotnet build
```

### Running Locally

```bash
cd rancher-devops-operator
dotnet run
```

## Example Usage

Create a `RancherProject`:

```yaml
apiVersion: rancher.devops.io/v1
kind: RancherProject
metadata:
  name: my-dev-project
spec:
  clusterName: "local"
  displayName: "Development Project"
  description: "Dev environment"
  namespaces:
    - dev-app
    - dev-database
  members:
    - principalId: "local://user-abc123"
      role: "project-owner"
```

Apply the resource:
```bash
kubectl apply -f rancher-project.yaml
```

Check status:
```bash
kubectl get rancherprojects
kubectl describe rancherproject my-dev-project
```

## Technical Highlights

### Native AOT Compilation
- Uses .NET 9 PublishAot for ahead-of-time compilation
- Source-generated JSON serialization for trimming compatibility
- Reduced memory footprint and faster startup times

### RBAC Configuration
The operator requires cluster-level permissions for:
- Managing RancherProject CRDs
- Creating events
- Reading ConfigMaps and Secrets
- Leader election (for multi-replica deployments)

### Security
- Runs as non-root user in containers
- Read-only root filesystem
- Dropped all capabilities
- Security context constraints applied

## Future Enhancements

Potential improvements:
- Support for project resource quotas in Rancher
- Project template support
- Namespace resource limits
- Pod security policies/standards
- Multi-cluster support
- Metrics and monitoring integration
- Validation webhooks

## Troubleshooting

### Common Issues

1. **Cluster not found**
   - Verify `clusterName` matches exactly in Rancher
   - Check Rancher API connectivity

2. **Authentication errors**
   - Verify API token is valid
   - Check token permissions

3. **SSL errors**
   - For development: set `allowInsecureSsl: true`
   - For production: use valid SSL certificates

### Logs

View operator logs:
```bash
kubectl logs -n rancher-devops-system deployment/rancher-devops-operator -f
```

## Resources

- [KubeOps Documentation](https://buehler.github.io/dotnet-operator-sdk/)
- [Rancher API Documentation](https://rancher.com/docs/rancher/v2.x/en/api/)
- [.NET Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
