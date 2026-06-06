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

The raw token itself is returned only once when the token is created. It is never written to logs or to the Secret.

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

- `mcp_token_list`
- `mcp_token_create`
- `mcp_token_delete`

`mcp_token_create` returns the raw token once and stores the hash in a Secret.
`mcp_token_delete` removes the matching Secret.

## Tool inventory

Implemented today:

- `cluster_list` — Rancher cluster inventory
- `project_list` — Rancher project inventory
- `mcp_token_list` — admin token inventory
- `mcp_token_create` — create a new token Secret
- `mcp_token_delete` — delete a token Secret

Inventory-mapped but not implemented in this build:

- `kubernetes_get`
- `kubernetes_list`
- `kubernetes_get_all`
- `kubernetes_logs`
- `kubernetes_inspect_pod`
- `kubernetes_describe`
- `kubernetes_events`
- `kubernetes_dep`
- `kubernetes_rollout_history`
- `kubernetes_node_analysis`
- `kubernetes_diff`
- `kubernetes_watch`
- `kubernetes_capacity`
- `kubernetes_workload_health`
- `kubernetes_resource_summary`
- `kubernetes_event_summary`
- `kubernetes_download_file`
- `kubernetes_create`
- `kubernetes_patch`
- `kubernetes_exec`
- `kubernetes_upload_file`
- `kubernetes_delete`

These are registered for compatibility with the upstream `futuretea/rancher-mcp-server` inventory, but the current .NET operator build only implements the Rancher list tools and the token lifecycle tools. The unimplemented Kubernetes tools intentionally return a short explanatory message so clients can see the catalog without assuming false support.

## Example client request

Anonymous viewer list:

```bash
curl -s http://operator:8080/mcp \
  -H 'content-type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

Authenticated token call:

```bash
curl -s http://operator:8080/mcp \
  -H 'content-type: application/json' \
  -H "authorization: Bearer ***" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"cluster_list","arguments":{}}}'
```

## Narrow RBAC considerations

The deployment needs a namespaced Role/RoleBinding for the MCP token namespace so the token store can create, list, and delete Secret-backed tokens. Keep the operator service account scoped to the namespace that holds MCP token Secrets.

The chart also exposes a dedicated MCP service port and an ingress route so clusters can route MCP traffic independently from the operator's other endpoints.
