---
layout: home
hero:
  name: Rancher DevOps Operator
  text: Declarative Rancher project automation for Kubernetes
  tagline: Manage Rancher projects, namespaces, members, quotas, and discovery from a cluster-scoped Project CRD.
  image:
    src: /logo.png
    alt: Rancher DevOps Operator logo
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started
    - theme: alt
      text: Read the Project CRD
      link: /reference/project-crd
features:
  - title: Cluster-scoped CRD
    details: Define Rancher projects with spec.clusterName, spec.displayName, namespaces, members, resource quotas, and policy gates.
  - title: Safe namespace reconciliation
    details: Create, assign, move, or preserve namespaces with separate project policies and namespace management policies.
  - title: Observe mode
    details: Import existing namespaces and members, then follow namespace changes using Rancher watches or polling.
  - title: Auth flexibility
    details: Use a static Rancher API token or username/password with automatic 12-hour token creation.
  - title: Built-in visibility
    details: The operator emits Kubernetes events and Prometheus metrics so you can see every reconcile and API call.
  - title: Helm-first install
    details: The chart packages the operator, ServiceAccount, CRD, optional ServiceMonitor, and Rancher connection settings.
---

## What this operator actually does

The Rancher DevOps Operator reads a cluster-scoped `Project` resource and reconciles it against Rancher. It resolves the Rancher cluster by name, creates or takes over a Rancher project, manages project namespaces and members, and keeps the Kubernetes status up to date with what happened.

- If the Rancher project does not exist, the operator creates it when `managementPolicies` allows `Create`.
- If the project already exists, it can import existing namespaces and members when `Observe` is enabled.
- Namespace reconciliation is policy-driven: the operator can create namespaces, move existing namespaces into a project, or preserve them when they are removed from the CRD.
- When `CleanupNamespaces=true` and namespace delete is allowed, removed namespaces can be deleted instead of only being disassociated.
- The operator exposes Prometheus metrics on port 9090 and emits Kubernetes events for major lifecycle steps and failures.

## Fast path

1. Install the Helm chart from `helm/rancher-devops-operator`.
2. Provide Rancher credentials with a token or username/password.
3. Apply a `Project` resource like the examples in the getting started guide.
4. Check `status.phase`, `status.projectId`, `status.clusterId`, and the operator events for reconciliation results.

## Source-grounded documentation

This site is based on the repository itself:

- `rancher-devops-operator/Entities/V1RancherProject.cs` for the CRD schema defaults
- `helm/rancher-devops-operator/templates/crd.yaml` for the OpenAPI schema and printer columns
- `rancher-devops-operator/Controller/RancherProjectController.cs` for reconcile behavior
- `rancher-devops-operator/Services/RancherNamespaceWatchService.cs` for Observe mode and namespace watching
- `helm/rancher-devops-operator/values.yaml` for install-time configuration

## Documentation map

- [Getting Started](/guide/getting-started) — install, configure credentials, and apply your first Project
- [Installation](/reference/installation) — Helm values, chart settings, and environment variables
- [Project CRD](/reference/project-crd) — spec, defaults, and status fields
- [Troubleshooting](/guide/troubleshooting) — common failure modes and how to read operator output
