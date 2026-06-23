# MCP passthrough identity

The MCP endpoint now uses *passthrough identity*: every MCP request must include a Rancher bearer token, and that exact token is forwarded to Rancher for the downstream API calls made by the MCP server.

The operator still needs its own Rancher access for background reconciliation. That operator credential is separate from the MCP request identity.

## Enablement

The chart enables MCP by default:

- `mcp.enabled=true`
- `mcp.port=8080`
- `mcp.tokenNamespace=default`

The deployment exposes the MCP port as a separate container port, the chart adds a dedicated ClusterIP service at `{{ include "rancher-devops-operator.fullname" . }}-mcp`, and the chart enables a Traefik ingress by default for external access in the local Rancher cluster.

The default external host is `{{ include "rancher-devops-operator.fullname" . }}.oly.workside.win`, but you can override it with `mcp.ingress.host`.

## Authentication

MCP authentication is header-based:

- `Authorization: Bearer <RANCHER_TOKEN>` is required
- missing or malformed headers return `401 Unauthorized`
- the bearer token is validated against Rancher and then reused for MCP tool execution
- the server does **not** mint, store, or rotate a separate MCP bearer token for this transport path

In practice, the MCP server acts as a thin identity-preserving proxy: the caller's Rancher token becomes the authorization context for the request, and Rancher remains the final policy decision point.

## Example requests

### Direct HTTP call

```bash
curl -s http://operator:8080/mcp \
  -H 'content-type: application/json' \
  -H "authorization: Bearer ${RANCHER_TOKEN}" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

### Claude Desktop

Add the MCP server to `claude_desktop_config.json` (or your equivalent Desktop config file):

```json
{
  "mcpServers": {
    "rancher-devops-operator": {
      "url": "http://operator:8080/mcp",
      "headers": {
        "Authorization": "Bearer ${RANCHER_TOKEN}"
      }
    }
  }
}
```

### Claude CLI / Claude Code

Use the same server definition in the Claude CLI MCP config:

```json
{
  "mcpServers": {
    "rancher-devops-operator": {
      "url": "http://operator:8080/mcp",
      "headers": {
        "Authorization": "Bearer ${RANCHER_TOKEN}"
      }
    }
  }
}
```

If your CLI uses a command-style registration flow instead of a JSON file, the important parts are still the same: the MCP server URL and an `Authorization: Bearer <RANCHER_TOKEN>` header carrying the Rancher token.

### VS Code

Add the same MCP server entry to your VS Code MCP settings file or workspace config:

```json
{
  "mcp": {
    "servers": {
      "rancher-devops-operator": {
        "url": "http://operator:8080/mcp",
        "headers": {
          "Authorization": "Bearer ${RANCHER_TOKEN}"
        }
      }
    }
  }
}
```

If your VS Code extension stores MCP servers under a slightly different key, keep the same `url` and `Authorization` header values.

## Security notes

- Treat the Rancher token as a secret; never commit it to source control.
- Prefer environment variable interpolation or secret managers over embedding the token directly in the config file.
- Keep the operator's Rancher credential and the MCP caller's token logically separate.

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

Legacy aliases such as `cluster_list` and `project_list` are still accepted for compatibility, but they are no longer shown by `tools/list`.

## Example client request

Authenticated HTTP call:

curl -s http://operator:8080/mcp \
  -H 'content-type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'

Authenticated token call:

curl -s http://operator:8080/mcp \
  -H 'content-type: application/json' \
  -H "authorization: Bearer ${RANCHER_TOKEN}" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"list_rancher_clusters","arguments":{}}}'

## Deployment notes

The operator still needs its own Rancher credential for background reconciliation. MCP passthrough auth does not replace that operator credential; it only changes how MCP requests authenticate.

The chart also exposes a dedicated MCP service port and an ingress route so clusters can route MCP traffic independently from the operator's other endpoints.
