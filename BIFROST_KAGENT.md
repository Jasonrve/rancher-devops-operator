# Bifrost + kagent Deployment Guide

This guide shows a practical way to host *agents* and *MCP servers* in `kagent`, front them with `Bifrost`, and expose them to consumers such as *VS Code* and *Claude* through a custom model endpoint and MCP integration.

## What this pattern is for

Use this setup when you want to:

- centralize agent hosting in Kubernetes
- publish one stable gateway endpoint instead of many backend services
- expose the same backend to multiple clients
- keep model access and tool access separate, but governed through the same edge
- make VS Code and Claude consume the same internal agent/tooling surface

## High-level architecture

```text
VS Code / Claude
   |
   | 1) Custom model requests
   v
Bifrost (public gateway)
   |
   | 2) Route model traffic to kagent
   | 3) Route MCP traffic to kagent-hosted MCP servers
   v
kagent (inside Kubernetes)
   |
   |-- Agent runtime / AGENT hosting
   |-- MCP servers
   |-- Internal services and cluster APIs
```

### Responsibilities

- **kagent**
  - hosts the agent runtime
  - runs one or more agent definitions
  - runs MCP servers that expose tools
  - talks to internal services, APIs, and cluster resources

- **Bifrost**
  - acts as the gateway and stable public entry point
  - presents a custom model endpoint for clients that speak an OpenAI-compatible or provider-specific model API
  - forwards MCP traffic to the correct kagent-hosted server
  - handles edge concerns such as auth, routing, TLS, and policy

- **Consumers**
  - **VS Code** uses the custom model endpoint for chat and the MCP endpoint for tools
  - **Claude** uses the same exposed services through its model and MCP configuration path

## Deployment flow

### 1. Deploy kagent

Start by deploying kagent in the cluster with the agents and MCP servers you want to host.

A typical kagent deployment surface looks like this:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: kagent-config
  namespace: kagent
  labels:
    app: kagent
data:
  agents.yaml: |
    agents:
      - name: rancher-ops-agent
        description: "Operates Rancher projects and namespaces"
        model: "bifrost/custom-model"
        tools:
          - rancher-projects
          - kubernetes-events
          - docs-search

      - name: platform-helper
        description: "General platform operations assistant"
        model: "bifrost/custom-model"
        tools:
          - cluster-info
          - namespace-audit

  mcp-servers.yaml: |
    mcpServers:
      - name: rancher-mcp
        url: http://rancher-mcp.kagent.svc.cluster.local:8080/mcp
        auth:
          type: bearer
          tokenFromSecret: rancher-mcp-token

      - name: docs-mcp
        url: http://docs-mcp.kagent.svc.cluster.local:8080/mcp
        auth:
          type: none
```

Notes:

- Keep agent definitions small and focused.
- Prefer separate MCP servers for separate tool domains.
- Store secrets in Kubernetes secrets, not in the ConfigMap.
- Treat the `model` value as the model name that Bifrost exposes to clients.

### 2. Expose kagent through Bifrost

Bifrost should publish two logical surfaces:

- a *model surface* for chat/completions
- an *MCP surface* for tools and agent actions

A simple gateway layout looks like this:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: bifrost-config
  namespace: bifrost
data:
  routes.yaml: |
    models:
      - name: custom-model
        upstream: http://kagent.kagent.svc.cluster.local:8080/v1
        type: openai-compatible

    mcp:
      - name: rancher-mcp
        upstream: http://rancher-mcp.kagent.svc.cluster.local:8080/mcp
      - name: docs-mcp
        upstream: http://docs-mcp.kagent.svc.cluster.local:8080/mcp
```

Recommended gateway behavior:

- require authentication at Bifrost
- keep internal kagent services private to the cluster
- map stable external names to internal services
- use one route for the model endpoint and separate routes for MCP servers

### 3. Bind agents to the exposed model

Agents should reference the model name published by Bifrost, not the internal service address.

This gives you a stable abstraction:

- **clients** talk to Bifrost
- **Bifrost** talks to kagent
- **kagent** talks to the backing model or tool servers

That separation makes it easier to swap models, add policies, or move the backend later without changing VS Code or Claude configuration.

### 4. Configure consumers

## VS Code integration

In VS Code, point your AI/chat extension at the Bifrost model endpoint and, separately, point its MCP configuration at the Bifrost MCP endpoint.

An example configuration pattern is:

```json
{
  "customModel": {
    "name": "custom-model",
    "baseUrl": "https://bifrost.example.com/v1",
    "apiKey": "${env:BIFROST_API_KEY}"
  },
  "mcp": {
    "servers": {
      "rancher-mcp": {
        "url": "https://bifrost.example.com/mcp/rancher-mcp",
        "headers": {
          "Authorization": "Bearer ${env:BIFROST_API_KEY}"
        }
      }
    }
  }
}
```

Practical guidance:

- use the Bifrost URL as the single public endpoint
- keep API keys in VS Code secrets or environment variables
- expose only the MCP servers the developer actually needs
- if your extension supports multiple model providers, name the Bifrost-backed model clearly, such as `custom-model` or `platform-assistant`

## Claude integration

Claude can use the same Bifrost-hosted capabilities through a model provider entry and MCP configuration.

Two common patterns are:

### Pattern A: Remote MCP supported by the client

If your Claude client supports remote MCP endpoints, point it directly at Bifrost:

```json
{
  "mcpServers": {
    "rancher-mcp": {
      "url": "https://bifrost.example.com/mcp/rancher-mcp",
      "headers": {
        "Authorization": "Bearer ${BIFROST_API_KEY}"
      }
    }
  }
}
```

### Pattern B: Local Claude bridge

If your Claude client expects a local MCP process, run a lightweight local bridge that forwards MCP calls to the Bifrost endpoint.

This keeps the operator-managed tools centralized while still letting Claude use them.

## Example operating model

A useful split for a platform team is:

- **kagent** hosts the execution layer
  - agents for Rancher operations
  - agents for platform assistance
  - MCP servers for specific tools

- **Bifrost** hosts the access layer
  - one custom model name for chat
  - one set of MCP routes for tools
  - one auth boundary for users and IDEs

- **VS Code** and **Claude** consume the same entry points
  - consistent prompts and tool access
  - fewer per-client differences
  - easier auditing and support

## Recommended configuration checklist

- [ ] Deploy kagent in a dedicated namespace
- [ ] Store model and MCP credentials in Kubernetes secrets
- [ ] Expose model and MCP traffic through Bifrost only
- [ ] Use TLS on all external endpoints
- [ ] Enable auth and audit logging at Bifrost
- [ ] Keep agent names and MCP server names stable
- [ ] Use a clear, documented custom model name for IDE consumers
- [ ] Test the same Bifrost endpoint from VS Code and Claude before rolling out broadly

## Example rollout sequence

1. Deploy the MCP servers in kagent.
2. Deploy the agent runtime in kagent.
3. Configure Bifrost routes for the custom model and MCP servers.
4. Verify the model endpoint from a simple client.
5. Verify one MCP server from VS Code.
6. Verify the same MCP server from Claude.
7. Expand to additional agents and tools once the gateway path is stable.

## Troubleshooting

### VS Code cannot see the custom model

- confirm the Bifrost base URL is correct
- confirm the model name matches the Bifrost route name
- check authentication headers or API keys
- verify Bifrost can reach the kagent upstream

### MCP tools do not appear

- confirm the MCP server is running in kagent
- confirm the MCP URL is published through Bifrost
- check that the client supports the transport you exposed
- verify auth headers and CORS/proxy settings if applicable

### Claude connects to the model but not MCP

- verify the Claude client supports remote MCP or use a local bridge
- make sure the MCP server route is exposed by Bifrost
- check that the tool server returns the expected MCP handshake

## Summary

The main idea is simple:

- host agents and MCP servers in **kagent**
- publish them through **Bifrost**
- consume them from **VS Code** and **Claude** using a stable custom model and MCP endpoints

This gives you a single platform-friendly entry point while keeping agent execution and tool hosting inside Kubernetes.
