# Azure Reference Architecture Plan

> **Status:** Reference complete

Generated: 2026-08-20

## 1. Project Overview

**Goal:** Implement an independent three-month GitHub Copilot for Azure
technical series. Month 1 is a runnable retail order modernization workshop;
Months 2 and 3 are complete standalone session artifacts.

**Path:** Modernize Existing

**Scope:** Workshop code plus optional reference IaC. The runnable path is local
Docker Compose. No checked-in workflow provisions, initializes, validates
against, smoke-tests, or deletes live Azure resources.

## 2. Requirements

| Attribute | Value |
| --- | --- |
| Classification | Development / workshop |
| Scale | Small |
| Budget | Cost-optimized, with explicit SKU parameters |
| Subscription | Not required for this workshop |
| Location | Parameterized for architecture review; no live region is selected |

## 3. Components

| Component | Type | Technology | Path |
| --- | --- | --- | --- |
| Legacy API | API / monolith | ASP.NET Core .NET 6 | `workshops/github-copilot-azure/01-ghc-agentic-modernizatoin/src/Legacy.Ordering.Api` |
| Edge API | External contract-preserving facade | ASP.NET Core .NET 10 | `workshops/github-copilot-azure/01-ghc-agentic-modernizatoin/src/Edge.Api` |
| Orders service | Internal API + owned order schema | ASP.NET Core .NET 10 + EF Core | `workshops/github-copilot-azure/01-ghc-agentic-modernizatoin/src/Orders.Api` |
| Inventory service | Internal API + owned inventory/reservation schema | ASP.NET Core .NET 10 + EF Core | `workshops/github-copilot-azure/01-ghc-agentic-modernizatoin/src/Inventory.Api` |
| Fulfillment service | Internal orchestration API + owned process schema | ASP.NET Core .NET 10 + EF Core | `workshops/github-copilot-azure/01-ghc-agentic-modernizatoin/src/Fulfillment.Api` |
| Validation suites | Tests | xUnit + ASP.NET Core test host | `workshops/github-copilot-azure/01-ghc-agentic-modernizatoin/tests` |
| Local environment | Containers | Docker Compose + separate SQLite stores; optional legacy SQL Server profile | `workshops/github-copilot-azure/01-ghc-agentic-modernizatoin/docker-compose.yml` |
| Azure infrastructure | IaC | Bicep | `workshops/github-copilot-azure/01-ghc-agentic-modernizatoin/infra` |

## 4. Recipe Selection

**Selected:** Bicep

**Rationale:** Bicep is explicitly required and keeps the reference architecture
small and reviewable. The template is resource-group scoped and parameterized,
but this repository intentionally provides no Azure lifecycle automation.

## 5. Architecture

**Stack:** One externally accessible .NET 10 edge Container App plus internal
Orders, Inventory, and Fulfillment Container Apps. Each backend owns a
separate Azure SQL database and communicates only through explicit HTTP APIs.
All services emit OpenTelemetry to workspace-based Application Insights.

| Component | Azure Service | Default SKU |
| --- | --- | --- |
| Edge API | Azure Container Apps, external ingress | Consumption workload profile, 1 warm replica |
| Orders, Inventory, Fulfillment | Azure Container Apps, internal ingress | Consumption workload profile, 1 warm replica each |
| Container image | Azure Container Registry | Basic |
| Relational stores | Three Azure SQL databases | Basic |
| Runtime identities | Four user-assigned managed identities | No charge |
| Logs and traces | Log Analytics + Application Insights | 30-day retention |

Security decisions:

- Microsoft Entra-only Azure SQL authentication; no SQL administrator password.
- Per-service user-assigned identities for ACR pull and database authentication.
- Each `AcrPull` assignment is scoped only to the workshop registry.
- Backend Container Apps expose internal ingress only; only Edge is public.
- Orders, Inventory, and Fulfillment database users are confined to their
  service-owned database and schema.
- Azure SQL data-plane grants require an organization-owned database migration
  process because ARM RBAC does not create contained database users.
- TLS, HTTPS ingress, non-root containers, and no embedded credentials.

## 6. Reference Resource Inventory

These are the resources represented by the Bicep. Quota, availability, policy,
and operational readiness remain responsibilities of any organization that
adapts the reference:

| Resource type | Quantity | Capacity note |
| --- | ---: | --- |
| `Microsoft.App/managedEnvironments` | 1 | Regional Container Apps availability required |
| `Microsoft.App/containerApps` | 4 | One edge and three internal services |
| `Microsoft.ContainerRegistry/registries` | 1 | Basic SKU |
| `Microsoft.Sql/servers` | 1 | Entra-only logical server |
| `Microsoft.Sql/servers/databases` | 3 | Cost-sensitive SKU parameter |
| `Microsoft.ManagedIdentity/userAssignedIdentities` | 4 | No regional quota expected |
| `Microsoft.OperationalInsights/workspaces` | 1 | 30-day retention |
| `Microsoft.Insights/components` | 1 | Workspace-based |

## 7. Execution Checklist

- [x] Create plan before implementation
- [x] Analyze the minimal repository and available toolchain
- [x] Record explicit user approval
- [x] Select Bicep and architecture
- [x] Research current Microsoft and GitHub guidance
- [x] Implement legacy, Edge, Orders, Inventory, and Fulfillment applications
- [x] Implement characterization, parity, per-service, inter-service, and failure tests
- [x] Implement local container environment and scripts
- [x] Implement secure Azure infrastructure
- [x] Implement Copilot customization assets
- [x] Complete all presenter and series documentation
- [x] Compile and lint reference Bicep statically
- [x] Record local and static validation proof
- [x] Replace the previous target with four independently deployable services
- [x] Replace local workshop workflows with executable zsh
- [x] Replace local and Azure topology with service-isolated resources
- [x] Update all tests, diagrams, presenter content, and Copilot assets
- [x] Remove Azure lifecycle scripts and retain local workflows only
- [x] Re-run local and static validation

## 8. Validation Proof

| Check | Command | Result | Timestamp |
| --- | --- | --- | --- |
| Release build | `dotnet build .../GitHubCopilotAzure.Modernization.sln --configuration Release` | Pass: 11 projects, 0 errors, 2 intentional .NET 6 EOL warnings | 2026-08-21T13:48:02Z |
| Behavior evidence | `dotnet test .../GitHubCopilotAzure.Modernization.sln --configuration Release --no-build` | Pass: 16 tests across characterization, public parity, per-service, inter-service, idempotency, unavailable-service, and unsafe-retry suites | 2026-08-21T13:48:02Z |
| Live process boundaries | Run Inventory, Orders, Fulfillment, and two Edge processes on isolated loopback ports; create and fulfill an order, then call an Edge instance with unavailable Orders | Pass: `flow=Fulfilled`; unavailable backend returned HTTP 503 | 2026-08-20T17:24:06Z |
| Dependency audit | `dotnet list .../GitHubCopilotAzure.Modernization.sln package --vulnerable --include-transitive` | Pass: no vulnerable packages in 11 projects | 2026-08-21T13:48:02Z |
| zsh syntax | `zsh -n scripts/*.zsh`; executable-bit and infrastructure-script absence checks | Pass: five executable local scripts parse and no infrastructure scripts remain | 2026-08-21T13:48:02Z |
| Compose model | `docker:cli compose config --quiet` | Pass using the official containerized Compose plugin | 2026-08-21T13:48:02Z |
| Bicep build/lint | `az bicep build --file infra/main.bicep --stdout`; `az bicep lint --file infra/main.bicep` | Pass with no diagnostics | 2026-08-21T13:48:02Z |
| Static consistency | `git diff --check`; deleted-filename, Azure-lifecycle-command, stale-term, and workflow-file scans | Pass | 2026-08-21T13:48:02Z |

The live process-boundary proof predates the reference-only scope change; no
runtime code changed in this revision. The current revision revalidated the
build, tests, audit, Compose model, zsh syntax, Bicep build/lint, and static
consistency. Azure control-plane operations are intentionally excluded.

## 9. Access Model Review

- **Modeled identities:** Edge, Orders, Inventory, and Fulfillment user-assigned
  managed identities.
- **Modeled Azure RBAC:** each identity receives only `AcrPull`
  (`7f951dda-4ed3-4680-a7ca-43fe172d538d`) scoped to the workshop registry.
- **Required SQL data plane:** an adopting organization must create contained
  users for Orders, Inventory, and Fulfillment in only their owned databases.
  Edge requires no database user.
- **Boundary:** database-principal creation is deliberately not automated by
  this workshop.

Environment limitations:

- The host Docker CLI has no native Compose plugin; the Compose model was
  validated with the official `docker:cli` image.
- Multi-stage Docker builds reached `dotnet restore`, but the Colima VM could
  not establish TLS to NuGet (`NU1301`, unexpected EOF). Host restore/build,
  all HTTP boundary tests, Dockerfile inspection, and Compose validation passed;
  the revised container runtime smoke could not run in this environment.
- SQL Server 2022 is amd64-only and exited under this Apple-silicon Colima/QEMU
  backend with an address-space mapping error. The portable SQLite path remains
  the documented default; the SQL image caveat is explicit in the workshop.
- No Azure subscription, control-plane, quota, policy, or runtime checks are
  part of the reference-only workshop path.

## 10. Files to Generate

| File or area | Purpose | Status |
| --- | --- | --- |
| `.azure/deployment-plan.md` | Reference architecture decisions and static proof | Complete |
| `workshops/github-copilot-azure/` | Three-month series | Complete |
| Month 1 `src/` and `tests/` | Runnable modernization scenario | Complete |
| Month 1 `infra/` | Optional Azure reference IaC | Complete |
| Month 1 `.github/` | Workspace-scoped Copilot customization | Complete |

## 11. Next Step

Use the local scripts for the runnable workshop. Treat the Bicep only as an
architecture review artifact and adapt it through an organization's approved
platform process before any real Azure use.
