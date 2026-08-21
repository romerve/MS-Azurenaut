---
applyTo: "infra/**"
---

# Azure infrastructure guardrails

- Treat Bicep as optional reference architecture, not deployment automation.
- Model Edge, Orders, Inventory, and Fulfillment as separate Container Apps.
- Only Edge has external ingress; backend ingress is internal.
- Use one identity per app, scoped ACR pull, and a separate SQL database/user for
  each data-owning service.
- Never add SQL passwords or registry admin credentials.
- Keep SQL private by default; broader demo access must be explicit.
- Expose cost-sensitive SKUs and scale settings as parameters.
- Validate only with static Bicep build and lint after changes.
- Do not add provisioning, deployment, database-bootstrap, smoke, or cleanup
  scripts under `infra/`.
