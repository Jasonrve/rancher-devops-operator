# Project CRD Reference

The operator manages a cluster-scoped `Project` custom resource.

## API details

- Group: `rancher.devops.io`
- Version: `v1`
- Kind: `Project`
- Plural: `projects`
- Short name: `pr`
- Scope: `Cluster`

## Spec fields

| Field | Type | Required | Default | Notes |
| --- | --- | --- | --- | --- |
| `clusterName` | string | yes | — | Rancher cluster name, not the cluster ID. |
| `displayName` | string | yes | — | Rancher project display name. If omitted in code paths, the metadata name is used as a fallback. |
| `description` | string | no | — | Project description. |
| `namespaces` | string[] | no | `[]` | Namespaces to create or assign to the project. |
| `members` | object[] | no | `[]` | Project members with their role template IDs. |
| `resourceQuota` | object | no | `null` | Optional CPU and memory quotas. |
| `managementPolicies` | string[] | no | `Create` | Allowed project actions. `Create`, `Delete`, and `Observe` are the supported values. |
| `namespaceManagementPolicies` | string[] | no | `Create`, `Update` | Namespace-specific action gate. `Delete` is opt-in. |

## Management policies

`managementPolicies` controls the high-level operator behavior.

| Policy | What it enables |
| --- | --- |
| `Create` | Create a Rancher project when one does not exist. |
| `Delete` | Delete the Rancher project when the CRD is deleted or a removal path is allowed. |
| `Observe` | Import existing namespaces and members, and let the observe services track namespace changes. |

If the list is empty, the operator defaults to create-only behavior.

## Namespace management policies

`namespaceManagementPolicies` gates namespace-specific actions separately from project lifecycle.

| Policy | What it enables |
| --- | --- |
| `Create` | Create a namespace that does not already exist. |
| `Update` | Assign or move an existing namespace into the project, and disassociate removed namespaces when deletion is not allowed. |
| `Delete` | Delete namespaces only when `CleanupNamespaces=true`; otherwise removal falls back to a non-destructive update path. |

If the list is empty, the operator defaults to `Create` and `Update`.

## Example manifest

```yaml
apiVersion: rancher.devops.io/v1
kind: Project
metadata:
  name: sample-project
spec:
  clusterName: local
  displayName: Sample Project
  description: Sample project managed by the operator
  namespaces:
    - sample-api
    - sample-ui
  members:
    - principalId: local://user-abc123
      role: project-owner
  resourceQuota:
    limitsCpu: "10"
    limitsMemory: 20Gi
    requestsCpu: "2"
    requestsMemory: 4Gi
  managementPolicies:
    - Create
    - Observe
  namespaceManagementPolicies:
    - Create
    - Update
```

## Status fields

| Field | Type | Notes |
| --- | --- | --- |
| `projectId` | string | Rancher project ID, such as `c-xxxxx:p-xxxxx`. |
| `clusterId` | string | Resolved Rancher cluster ID. |
| `phase` | string | Operator phase, commonly `Pending`, `Error`, or success states used by the controller. |
| `createdNamespaces` | string[] | Namespaces created by the operator during the current reconcile. |
| `manuallyRemovedNamespaces` | string[] | Namespaces the controller should not recreate after external removal. |
| `lastReconcileTime` | date-time | Last reconciliation timestamp. |
| `createdTimestamp` | date-time | First successful create or takeover timestamp. |
| `lastUpdatedTimestamp` | date-time | Last successful status update timestamp. |
| `errorMessage` | string | Populated when the reconcile fails. |
| `configuredMembers` | string[] | Members successfully configured during reconcile. |

## Printer columns

The CRD exposes these columns in `kubectl get project` output:

- `Cluster`
- `Project ID`
- `Phase`
- `Age`

## Reconcile flow at a glance

1. Resolve `spec.clusterName` to a Rancher cluster ID.
2. Find or create the Rancher project.
3. If `Observe` is enabled, import existing namespaces and members when the spec is empty.
4. Reconcile namespaces according to the namespace policies and `CleanupNamespaces`.
5. Reconcile members according to the `members` list.
6. Update status and emit Kubernetes events.
