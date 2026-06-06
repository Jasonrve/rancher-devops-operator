# Getting Started

This guide gets you from a fresh clone to a working `Project` resource.

## 1. Prerequisites

You need:

- A Kubernetes cluster that can run the operator
- A Rancher server that the operator can reach over HTTPS
- Helm 3
- Either a Rancher API token or Rancher username/password

The repository's Helm chart defaults to:

- `rancher.url: https://rancher.local`
- `rancher.allowInsecureSsl: false`
- `env.ObserveMethod: watch`
- `env.CleanupNamespaces: false`

## 2. Create credentials

The operator supports two auth styles.

### Option A: Static token

```bash
kubectl create namespace rancher-devops-system
kubectl create secret generic rancher-creds \
  --namespace rancher-devops-system \
  --from-literal=token='token-xxxxx:yyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyy'
```

### Option B: Username and password

```bash
kubectl create namespace rancher-devops-system
kubectl create secret generic rancher-creds \
  --namespace rancher-devops-system \
  --from-literal=username='admin' \
  --from-literal=password='your-password'
```

## 3. Install the chart

The chart lives in `helm/rancher-devops-operator`.

```bash
helm upgrade --install rancher-devops-operator ./helm/rancher-devops-operator \
  --namespace rancher-devops-system \
  --create-namespace \
  --set rancher.url=https://rancher.example.com \
  --set rancher.existingSecret=rancher-creds
```

If your Rancher server uses a self-signed certificate, set:

```bash
--set rancher.allowInsecureSsl=true
```

## 4. Apply a Project resource

The CRD kind is `Project` and the API group is `rancher.devops.io/v1`.

```yaml
apiVersion: rancher.devops.io/v1
kind: Project
metadata:
  name: platform-project
spec:
  clusterName: local
  displayName: Platform Project
  description: Platform workloads managed by the operator
  namespaces:
    - platform-api
    - platform-web
  members:
    - principalId: local://user-abc123
      role: project-owner
    - principalId: local://user-def456
      role: project-member
  managementPolicies:
    - Create
    - Observe
  namespaceManagementPolicies:
    - Create
    - Update
```

Apply it:

```bash
kubectl apply -f project.yaml
```

## 5. Check status and events

```bash
kubectl get project platform-project
kubectl describe project platform-project
```

Useful status fields:

- `status.phase`
- `status.projectId`
- `status.clusterId`
- `status.createdNamespaces`
- `status.manuallyRemovedNamespaces`
- `status.configuredMembers`
- `status.errorMessage`

The operator also creates Kubernetes events such as `ReconcileStarted`, `ClusterResolved`, `ProjectCreated`, `NamespaceCreated`, and `MemberAdded`.

## 6. Start small

If you want the safest first run:

- Use a real cluster name from Rancher, not the cluster ID
- Leave `managementPolicies` empty to start in create-only mode
- Keep `CleanupNamespaces=false` until you're comfortable with removal behavior
- Enable `Observe` only when you want the operator to import existing project state or follow namespace changes

## What happens next

After reconcile, the operator will:

1. Resolve the cluster name to a Rancher cluster ID
2. Create or take over the Rancher project
3. Create or reassign namespaces based on policy
4. Add members with their role template IDs
5. Update the CRD status with the result
