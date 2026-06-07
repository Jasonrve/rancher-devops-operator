# MCP server and token RBAC

The operator now exposes an MCP JSON-RPC endpoint on the same workload, gated by bearer tokens backed by Kubernetes Secrets.

## Enablement

The chart enables MCP by default:

- `mcp.enabled=true`
- `mcp.port=8080`
- `mcp.tokenNamespace=default`

The deployment exposes the MCP port as a separate container port, the chart adds a dedicated ClusterIP service at `{{ include "rancher-devops-operator.fullname" . }}-mcp`, and the chart enables a Traefik ingress by default for external access in the local Rancher cluster.

The default external host is `{{ include "rancher-devops-operator.fullname" . }}.oly.workside.win`, but you can override it with `mcp.ingress.host`.

## Authentication and authorization

Authorization is derived from the bearer token in the `Authorization` header:

- no token: anonymous viewer
- valid token: role is read from the matching Kubernetes Secret
- invalid token: `401 Unauthorized`
- valid token but insufficient role: `403 Forbidden`

The role model is intentionally small:

- `viewer` — read-only tools
- `admin` — everything viewer can do, plus write/admin tools and token management

`tools/list` is filtered by the resolved role, and `tools/call` checks authorization again before execution.

## Secret storage model

Tokens are stored in Kubernetes Secrets only.

Each token Secret stores:

- `tokenHash` — SHA-256 hash of the raw token
- `role` — `viewer` or `admin`
- `createdAt` — timestamp used for listing/order

The raw token itself is never echoed by the operator; the secret name is returned and the token is stored in Kubernetes Secrets.

Token Secrets are labeled so the operator can discover them safely:

- `app.kubernetes.io/managed-by=rancher-devops-operator`
- `mcp.devops.io/mcp-token=true`
- `role=<viewer|admin>`

## Bootstrap path for the first admin token

If there is no admin token yet, set a bootstrap hash in Helm or the environment:

- `Mcp__BootstrapAdminTokenHash`
- optionally `Mcp__BootstrapAdminTokenSecretName`

Recommended workflow:

1. Generate a raw token outside the cluster.
2. Hash it with SHA-256.
3. Set the hash in `Mcp__BootstrapAdminTokenHash`.
4. Deploy the operator.
5. Use the raw token as the first admin bearer token.

Example:

```bash
RAW_TOKEN="mcp_$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')"
TOKEN_HASH="$(printf '%s' "$RAW_TOKEN" | sha256sum | awk '{print $1}')"
helm upgrade --install rancher-devops-operator ./helm/rancher-devops-operator \
  --set-string mcp.bootstrapAdminTokenHash="$TOKEN_HASH"
```

Keep the raw token somewhere safe; the cluster only stores the hash.

## Token management tools

Admin-only tools:

- `list_mcp_tokens`
- `create_mcp_token`
- `rotate_mcp_token`
- `revoke_mcp_token`

`create_mcp_token` stores the token hash in Kubernetes and returns only the secret name and role metadata.
`revoke_mcp_token` removes the matching Secret.

## Tool inventory

Implemented tools are grouped by access level:

Viewer tools:

- `whoami`
- `get_effective_role`
- `list_allowed_tools`
- `explain_user_access`
- `get_rancher_version`
- `check_rancher_api_health`
- `get_rancher_server_health`
- `list_rancher_clusters`
- `get_rancher_cluster`
- `get_cluster_summary`
- `get_cluster_status`
- `get_cluster_agent_status`
- `get_cluster_registration_status`
- `get_downstream_cluster_connectivity`
- `get_cluster_agent_diagnostics`
- `get_rancher_recent_warnings`
- `list_projects`
- `get_project`
- `list_project_namespaces`
- `list_project_members`
- `list_project_role_template_bindings`
- `list_cluster_role_template_bindings`
- `list_rancher_users`
- `list_rancher_groups`
- `list_global_roles`
- `list_role_templates`
- `list_fleet_gitrepos`
- `get_fleet_gitrepo`
- `list_fleet_bundles`
- `get_fleet_bundle_status`
- `get_fleet_sync_status`
- `get_fleet_deployment_errors`
- `list_rancher_apps`
- `get_rancher_app`
- `get_rancher_app_values`
- `list_rancher_chart_repositories`
- `search_rancher_catalog_charts`
- `get_rancher_webhook_status`

Admin-only tools:

- `list_mcp_tokens`
- `create_mcp_token`
- `rotate_mcp_token`
- `revoke_mcp_token`
- `import_cluster`
- `generate_cluster_registration_command`
- `rotate_cluster_registration_token`
- `update_cluster_labels`
- `update_cluster_annotations`
- `delete_rancher_cluster`
- `restart_cluster_agent`
- `redeploy_cluster_agent`
- `regenerate_cluster_agent_manifest`
- `create_project`
- `update_project`
- `delete_project`
- `move_namespace_to_project`
- `assign_project_member`
- `remove_project_member`
- `assign_global_role`
- `remove_global_role`
- `assign_cluster_role`
- `remove_cluster_role`
- `assign_project_role`
- `remove_project_role`
- `create_fleet_gitrepo`
- `update_fleet_gitrepo`
- `delete_fleet_gitrepo`
- `force_fleet_sync`
- `pause_fleet_gitrepo`
- `resume_fleet_gitrepo`
- `install_rancher_app`
- `upgrade_rancher_app`
- `rollback_rancher_app`
- `uninstall_rancher_app`
- `add_rancher_chart_repository`
- `refresh_rancher_chart_repository`

Legacy aliases such as `cluster_list`, `project_list`, and the `mcp_token_*` names are still accepted for compatibility, but they are no longer shown by `tools/list`.

## Example client request

Anonymous viewer list:

curl -s http://operator:8080/mcp \
  -H 'content-type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'

Authenticated token call:

curl -s http://operator:8080/mcp \
  -H 'content-type: application/json' \
  -H "authorization: Bearer ***" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_rancher_clusters","arguments":{}}}'

## Narrow RBAC considerations

The deployment needs a namespaced Role/RoleBinding for the MCP token namespace so the token store can create, list, and delete Secret-backed tokens. Keep the operator service account scoped to the namespace that holds MCP token Secrets.

The chart also exposes a dedicated MCP service port and an ingress route so clusters can route MCP traffic independently from the operator's other endpoints.
