# MCP server: Rancher-only, pass-through auth

The operator exposes an MCP JSON-RPC endpoint for **Rancher functionality only**. Kubernetes-specific MCP tools and MCP token-management tools are not part of the surface.

## Authentication

For MCP calls, the server uses the caller’s incoming `Authorization` header as a **pass-through credential** to Rancher when present.

- If the request includes `Authorization`, the same header is forwarded to Rancher API calls.
- If no header is present, the operator falls back to configured Rancher credentials (`Rancher:Token` or `Rancher:Username` + `Rancher:Password`) for internal/operator-owned background paths.
- There is no separate MCP token RBAC layer in the MCP catalog.

## Tool inventory

Implemented Rancher tools:

- `cluster_list`
- `cluster_get_id`
- `cluster_get_kubeconfig`
- `project_list`
- `project_get`
- `project_create`
- `project_delete`
- `namespace_create`
- `namespace_get`
- `namespace_update_project`
- `namespace_remove_project`
- `namespace_list_by_project`
- `namespace_delete`
- `namespace_ensure_managed_by`
- `project_member_create`
- `project_member_list`
- `project_member_delete`
- `principal_get_by_name` resolves principals by Rancher `name` and `loginName`, which lets local users be found even when the display name differs from the login name.

## Expected behavior

- `tools/list` returns only the Rancher tool catalog above.
- `tools/call` dispatches to Rancher API helpers.
- Tool-level authorization is delegated to Rancher using the caller’s credentials.

## Verification

The repository includes unit tests that cover:

- catalog contents
- MCP request handling
- pass-through authorization behavior
- dispatch for every Rancher MCP tool
