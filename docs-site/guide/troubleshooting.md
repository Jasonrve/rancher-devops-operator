# Troubleshooting

## Cluster not found

If the operator sets `status.phase=Error` with a message like `Cluster 'X' not found`, check that `spec.clusterName` exactly matches the Rancher cluster name.

The controller resolves clusters by name, not by ID.

## Nothing happens after applying a Project

Check these items:

- The chart is installed and the controller is running
- The CRD has `managementPolicies` that include `Create`
- `spec.clusterName` points at a real Rancher cluster
- Rancher credentials are valid
- The operator logs show a reconcile attempt

If you leave `managementPolicies` empty, the code defaults to create-only behavior.

## Observe mode does not import existing namespaces or members

Observe imports only happen when `managementPolicies` includes `Observe`.

Also check:

- `spec.namespaces` is empty if you want the initial import to populate it
- `ObserveMethod` is not set to `none`
- The operator can authenticate to Rancher and list namespaces/members

## A namespace will not move into the project

Namespace movement requires `namespaceManagementPolicies` to allow `Update`.

The controller will skip the move when:

- The namespace is already claimed by another Project CRD
- `Update` is not allowed
- The namespace is marked as manually removed in status

If another CRD claims the namespace, the controller records a `NamespaceConflict` event and stops that reconcile.

## Removed namespaces keep coming back

The operator has explicit protection against recreating namespaces that were removed outside the operator.

Check `status.manuallyRemovedNamespaces`.

If a namespace is listed there, the controller will skip it until you remove the entry from status.

## Namespaces are being deleted when you expected them to be preserved

Namespace deletion only happens when all of these are true:

- `namespaceManagementPolicies` includes `Delete`
- `CleanupNamespaces=true`
- The namespace is removed from `spec.namespaces`

If you want disassociation without deletion, leave `CleanupNamespaces=false`.

## Authentication failures

The two supported auth paths are:

- Static token
- Username/password token creation

For token issues:

- Make sure the token is valid in Rancher
- Make sure the operator is pointed at the right Rancher URL

For username/password issues:

- Check that the password is correct
- Make sure the Rancher account can create API tokens

## Where to look first

1. `kubectl describe project <name>` for events and status
2. `kubectl logs -n rancher-devops-system deployment/rancher-devops-operator`
3. The `Project` manifest's policy fields
4. The chart values for Rancher URL, auth, and cleanup settings
