# Month 3 — Agentic CI/CD: Actions Authoring, Failure Remediation & Release Assurance

A standalone 60-minute session on using GitHub Copilot's agentic features to **author and fix GitHub Actions workflows** that deploy containerized workloads to Azure securely (OIDC, least privilege), and to produce **grounded, evidence-backed release assurance** before those workloads ship. No prior session in this series is required.

## Audience

- DevOps/platform engineers and application developers who own or maintain GitHub Actions workflows deploying to Azure.
- Security-conscious teams looking to replace long-lived Azure credentials with OIDC-based federated identity and tighten role scope.
- Release managers and tech leads who need a defensible, evidence-backed release summary before promoting a build to production.

## Prerequisites

- A GitHub repository with **GitHub Actions** enabled and at least one existing workflow (or willingness to author one live).
- An **Azure subscription** with permission to create a resource group and register a Microsoft Entra ID application (for the OIDC federated credential demo).
- A container registry target (Azure Container Registry or GitHub Container Registry) and a containerized workload target — Azure Container Apps or Azure Kubernetes Service both work for the demo.
- **GitHub Copilot** enabled, with access to Copilot agent mode/coding agent and, ideally, Copilot code review and code scanning (CodeQL) enabled on the demo repository.
- Familiarity with YAML workflow syntax and basic Azure RBAC concepts (roles, scopes).

## Learning objectives

By the end of this session, attendees will be able to:

1. Use Copilot agent mode/coding agent to **author and remediate GitHub Actions workflows** for a containerized Azure workload, including migrating from long-lived secrets to **OpenID Connect (OIDC)** federated credentials scoped to **least privilege**, and using agent-assisted or one-click remediation for a failing workflow run.
2. Read and act on **dependency review** and **code scanning (CodeQL)** results in a pull request as part of the normal development loop, not as an afterthought.
3. Generate a **SBOM and build provenance attestation** for a container image, gate production deployment behind a **protected environment**, and produce a **grounded release summary** whose claims trace back to actual scan/attestation/test evidence rather than a generic AI-written narrative.

## Session agenda at a glance (60 minutes)

| Time | Segment | Topic |
|---|---|---|
| 0:00 – 0:05 | Welcome & framing | Series context, session goals |
| 0:05 – 0:35 | **Primary topic** | Agent-assisted GitHub Actions authoring & failure remediation with OIDC/least privilege (Challenge → Closing) |
| 0:35 – 0:55 | **Secondary topic** | Release safety and supply-chain evidence (Challenge → Closing) |
| 0:55 – 1:00 | Session closing | Recap, artifacts, next steps |

Full minute-by-minute timing, narration notes, and demo scripts are in **[session-guide.md](./session-guide.md)**.

This month is a complete facilitator outline, not a bundled runnable demo
repository. Prepare the workflow examples below in a disposable repository
before the session, following the setup notes in the guide.

## Patterns attendees can reproduce

- An OIDC-authenticated GitHub Actions deployment with a least-privilege,
  resource-group-scoped Azure role assignment and no long-lived secret.
- A checklist for evidence-based Copilot remediation of a failed workflow run.
- A supply-chain workflow pattern covering dependency review, code scanning,
  SBOM and build-provenance attestations.
- A grounded release-summary pattern whose claims map to verifiable evidence.

## Related session

The evidence discipline in this session's secondary topic (traceable release claims) is a direct extension of **Month 2's** grounded red-green-refactor discipline. See [Month 2](../02-ghc-agentic-development/).
