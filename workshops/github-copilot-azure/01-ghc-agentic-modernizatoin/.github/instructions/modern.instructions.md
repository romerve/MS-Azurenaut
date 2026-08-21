---
applyTo: "src/{Edge,Orders,Inventory,Fulfillment}.Api/**"
---

# .NET 10 service guardrails

- Each project is independently buildable, containerized, and deployable.
- Edge owns public compatibility and no business database.
- Orders, Inventory, and Fulfillment own separate DbContexts and schemas.
- Cross-service work uses typed HTTP clients with timeouts, bounded resilience,
  correlation propagation, and explicit failure semantics.
- Keep `/health/live` dependency-free and `/health/ready` dependency-aware.
- Use structured log fields and optional Azure Monitor OpenTelemetry export.
- Azure SQL uses `Active Directory Managed Identity`; never embed credentials.
- Add the smallest focused test for every contract, ownership, or failure change.
