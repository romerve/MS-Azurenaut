---
description: Plan a contract-preserving .NET 10 microservices decomposition.
agent: modernization-guide
---

Using the assessment and existing tests, create an incremental plan from the
.NET 6 monolith to Edge, Orders, Inventory, and Fulfillment services. Do not edit.

For each slice include:

- affected files and service/data owner;
- public behavior that remains stable;
- internal API and failure semantics;
- test to add or run before and after;
- identity, observability, container, and reference-IaC impact;
- rollback point and completion evidence.

Order dependencies so Inventory is available before Orders, and Orders plus
Inventory before Fulfillment. Include an Edge compatibility seam, idempotent
reservation, a backend-unavailable path, and database migration/cutover concerns.
Reject shared DbContexts, direct cross-service table access, unbounded retries,
and plans that treat compilation as proof.

Do not plan or add checked-in Azure lifecycle automation. Bicep is optional
reference architecture; the runnable workshop path is local Docker Compose.
