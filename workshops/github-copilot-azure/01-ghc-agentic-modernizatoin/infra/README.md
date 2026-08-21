# Optional Azure reference architecture

This folder contains **reference IaC only**. It models how the .NET 10 target
could map to Azure Container Apps, but this workshop does not provide scripts to
provision, update, initialize, smoke-test, or delete Azure resources.

The runnable workshop path is local Docker Compose through `../scripts/*.zsh`.

## Reference topology

| App | Ingress | Identity/data ownership |
|---|---|---|
| Edge | External HTTPS | Own identity for ACR pull; no database |
| Orders | Internal HTTPS | Own identity and `orders` database |
| Inventory | Internal HTTPS | Own identity and `inventory` database |
| Fulfillment | Internal HTTPS | Own identity and `fulfillment` database |

[`main.bicep`](./main.bicep) and
[`container-app.bicep`](./container-app.bicep) model:

- four Azure Container Apps in one managed environment;
- external ingress only for Edge and internal ingress for all backends;
- one user-assigned identity per app with registry-scoped `AcrPull`;
- three separate Azure SQL databases with Entra-only authentication;
- private SQL networking by default;
- Log Analytics and workspace-based Application Insights;
- health probes on ASP.NET Core port 8080;
- explicit cost parameters for ACR, SQL, retention, and replica limits.

No credentials are embedded in the templates.

## Static validation

If Azure CLI and Bicep are installed, compile and lint the reference without
contacting an Azure subscription:

```zsh
az bicep build --file main.bicep
az bicep lint --file main.bicep
```

These commands validate template syntax and linter rules only. They do not
prove regional availability, quota, policy compliance, database initialization,
or runtime behavior.

## Parameters worth discussing

| Parameter | Design purpose |
|---|---|
| `edgeImage`, `ordersImage`, `inventoryImage`, `fulfillmentImage` | Independent image/revision ownership |
| `registrySku` | Cost versus registry capabilities |
| `sqlDatabaseSku` | Cost control across three owned databases |
| `sqlNetworkMode` | `Private` secure default versus an explicit demo-only broader mode |
| `minReplicas`, `maxReplicas` | Warm-demo reliability versus idle cost |
| `logRetentionDays` | Observability retention cost |

The default of one warm replica per app avoids backend cold starts exceeding the
bounded HTTP timeout during a live architecture discussion.

## Deliberate operational gaps

A production implementation still needs an organization-owned delivery process
for:

- image build, signing, promotion, and revision rollout;
- Azure SQL contained-user creation and schema migrations;
- subscription/region selection, quota, and policy validation;
- private-network execution of database administration;
- runtime smoke tests, rollback, monitoring alerts, and cleanup governance.

Those lifecycle concerns are intentionally outside this workshop. Use the Bicep
only as a reviewable architecture example, adapt it to organizational standards,
and validate it through the organization's approved platform pipeline before
any real deployment.

## Cost and security review

The reference uses Basic ACR, three Basic SQL databases, 30-day log retention,
one region, and no zone redundancy. It favors bounded workshop cost over
production availability.

Reviewers should verify:

- only Edge is externally reachable;
- every identity has registry-scoped rather than resource-group-scoped pull
  permission;
- each data-owning service receives access only to its own database;
- SQL remains Entra-only and private by default;
- no registry admin credential or SQL password is introduced;
- scale and retention settings match the intended environment.
