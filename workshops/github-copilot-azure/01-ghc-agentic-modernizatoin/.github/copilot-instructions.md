# Workshop repository instructions

This workspace is a guided decomposition lab for a retail order-management API.
Treat the .NET 6 application as observed legacy behavior and the .NET 10 Edge,
Orders, Inventory, and Fulfillment projects as independently deployable targets.

## Non-negotiable outcomes

- Preserve status codes, content types, headers, and JSON under `/api/orders`
  and `/api/inventory` through `Edge.Api`.
- Keep backend endpoints internal and communicate through typed HTTP clients.
- Orders, Inventory, and Fulfillment must own separate EF DbContexts and
  databases. Never query another service's tables.
- Use bounded timeouts/retries, correlation propagation, explicit 503 behavior,
  and idempotency where partial completion is possible.
- Prove behavior with characterization, API parity, per-service, inter-service,
  and unavailable-service tests. Compilation alone is not success.
- Use environment configuration, one managed identity per Azure app, Entra-only
  Azure SQL, structured logs, health checks, and OpenTelemetry.
- Never add credentials, weaken TLS/network defaults, or fabricate agent output.

## Build and validate

```zsh
dotnet build GitHubCopilotAzure.Modernization.sln --configuration Release
dotnet test GitHubCopilotAzure.Modernization.sln \
  --configuration Release --no-restore
dotnet list GitHubCopilotAzure.Modernization.sln package \
  --vulnerable --include-transitive
zsh -n scripts/*.zsh
az bicep build --file infra/main.bicep
```

The Bicep files are static reference architecture. Do not add or invoke Azure
provisioning, deployment, database-bootstrap, smoke, or cleanup workflows.

Report observed evidence separately from recommendations and name the command or
test supporting every completion claim.
