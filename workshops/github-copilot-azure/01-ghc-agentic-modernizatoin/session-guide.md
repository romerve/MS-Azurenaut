# Month 1 Session Guide - Agentic Modernization

**Scenario:** Retail order management and fulfillment
**Duration:** 60 minutes
**Audience:** Developers, architects, and DevOps engineers

All demonstrations use checked-in code and commands. Any GitHub Copilot wording
is generated live; this guide describes the evidence to request and inspect, not
a prerecorded agent answer.

## Timing

| Time | Segment |
|---|---|
| 0:00-0:05 | Framing and objectives |
| 0:05-0:39 | Primary topic: agentic decomposition and .NET 10 upgrade |
| 0:39-1:00 | Secondary topic: proof, identity, and observability |

## Primary topic: Agent-led assessment, .NET 10 upgrade, and microservices decomposition

### Challenge

The .NET 6 baseline is functional but unsupported. `LegacyOrderService` owns
Orders, Inventory, and Fulfillment in one process and one data model.
Fulfillment is synchronous, configuration includes a credential-shaped
placeholder, and the application offers little operational evidence.

The dangerous shortcut is to retarget the framework, split files or projects,
and call the result modern. That does not establish service ownership, protect
callers, or define what happens when one service is unavailable.

### Challenge Demo

Open:

- `src/Legacy.Ordering.Api/Legacy.Ordering.Api.csproj`
- `src/Legacy.Ordering.Api/LegacyOrderService.cs`
- `src/Legacy.Ordering.Api/appsettings.json`
- `tests/Legacy.Characterization.Tests`

Show that one class reads and mutates all three business concerns. Run:

```zsh
dotnet build GitHubCopilotAzure.Modernization.sln --configuration Release
dotnet test tests/Legacy.Characterization.Tests \
  --configuration Release --no-restore
```

The expected build evidence is zero errors plus the deliberate .NET 6
end-of-support warning. Ask: **What evidence would let us split this without
forcing every caller to migrate at once?**

### Solution

Open this folder as the workspace and use the `modernization-guide` custom
agent. Its workflow is:

1. **Assess** target frameworks, coupling, data access, configuration,
   observability, containers, and missing proof.
2. **Plan** reversible slices with API invariants and rollback points.
3. **Execute** one approved service boundary at a time.
4. **Validate** contracts, service behavior, failure paths, dependencies,
   containers, scripts, and infrastructure.

The target keeps the public contract behind `Edge.Api` while introducing:

- `Orders.Api`: owns order state and checks inventory by typed HTTP client;
- `Inventory.Api`: owns stock and idempotent reservation records;
- `Fulfillment.Api`: owns orchestration progress and resumes safely;
- separate database ownership, independently buildable projects, and
  independently deployable containers;
- five-second total timeout, two-second attempt timeout, bounded retries,
  correlation propagation, and explicit HTTP 503 semantics.

No broker is introduced. The order workflow is small and synchronous, and
idempotent reservations make the demonstrated partial-failure retry safe.

### Solution Demo

1. Run `.github/prompts/assess-modernization.prompt.md` and inspect whether the
   response cites actual files and distinguishes observed evidence from
   inference.
2. Run `.github/prompts/plan-modernization.prompt.md`. Verify that it includes
   an Edge compatibility seam, separate data ownership, dependency ordering,
   service-unavailable behavior, and a test for every slice.
3. Open the four `Program.cs` files and the typed clients in
   `Orders.Api/InventoryClient.cs`, `Fulfillment.Api/ServiceClients.cs`, and
   `Edge.Api/ServiceClients.cs`.
4. Show `InventoryReservation.ReservationId` and the Fulfillment state machine.
5. Build each deployable independently:

```zsh
for project in Edge Orders Inventory Fulfillment; do
  dotnet build "src/${project}.Api/${project}.Api.csproj" \
    --configuration Release --no-restore
done
```

6. Run `Api.Contract.Tests` to exercise Legacy -> Inventory -> Orders ->
   Fulfillment -> Edge through separate in-memory HTTP servers.

### Outcome

The result is a real service decomposition:

- public routes remain stable;
- backend APIs are internal implementation details;
- each service owns its logic and schema;
- failures are bounded and visible;
- retried fulfillment does not double-reserve inventory;
- every service can be built, containerized, deployed, and scaled separately.

### Closing

Compilation is necessary, but architecture changes become credible only when
ownership and failure semantics are executable. Transition with: **Now that the
shape is better, how do we prove it is still the same system to its callers?**

## Secondary topic: Proving modernization with tests, managed identity, and observability

### Challenge

Distributed boundaries create new failure modes. A request can succeed at
Inventory and fail at Orders, a backend can be unavailable, or a caller can see
a changed ProblemDetails payload. Shared credentials and missing traces would
make those failures difficult to diagnose and unsafe to operate.

### Challenge Demo

Open `tests/Api.Contract.Tests/ApiParityTests.cs` and
`tests/Edge.Integration.Tests/EdgeFailureTests.cs`.

Ask the audience to predict what should happen if Orders cannot be reached.
The required answer is not an unhandled 500 or indefinite wait: Edge returns a
503 ProblemDetails response after the configured resilience policy is exhausted.

Run:

```zsh
dotnet test tests/Edge.Integration.Tests \
  --configuration Release --no-restore
```

Expected: three tests pass, including the unavailable-service path and proof
that non-idempotent order creation is attempted only once.

### Solution

Proof is layered:

- characterization tests preserve observed legacy behavior;
- API contract tests compare status, location, content type, and normalized JSON;
- per-service tests verify order rules, reservation idempotency, conflicts, and
  fulfillment state;
- inter-service tests use HTTP bridges rather than shared service classes or
  DbContexts;
- the smoke script executes real containers and public endpoints when the local
  container network is available.

Operational hardening is also layered:

- JSON logs and `X-Correlation-ID` scopes across typed clients;
- dependency-free liveness and database-aware readiness;
- OpenTelemetry exported through Azure Monitor when
  `APPLICATIONINSIGHTS_CONNECTION_STRING` is set;
- one managed identity per Container App;
- three identity-specific Azure SQL users, each in only its owned database;
- external ingress only at Edge and private SQL networking by default.

### Solution Demo

Run:

```zsh
dotnet test GitHubCopilotAzure.Modernization.sln \
  --configuration Release --no-restore
dotnet list GitHubCopilotAzure.Modernization.sln package \
  --vulnerable --include-transitive
zsh -n scripts/*.zsh
az bicep build --file infra/main.bicep
```

If the container runtime can reach NuGet:

```zsh
./scripts/reset.zsh
```

Show the reference Bicep topology:

- four Container Apps;
- only Edge has external ingress;
- Orders, Inventory, and Fulfillment use internal HTTPS service FQDNs;
- four scoped ACR pull assignments;
- three Azure SQL databases and three application database identities;
- readiness/liveness probes on port 8080;
- workspace-based Application Insights.

State explicitly that this is a static architecture discussion. The checked-in
workshop does not automate Azure provisioning, database initialization,
runtime smoke validation, or cleanup.

### Outcome

The same public API is backed by independently owned services, and the proof
covers both success and failure. Identity, network boundaries, and telemetry
are part of the architecture rather than deployment-time afterthoughts.

### Closing

End with: **Copilot accelerates the change; executable evidence earns the right
to ship it.** Direct attendees to the runbook for deterministic commands and to
Month 2 for issue-to-feature development. Month 2 is independent and does not
require this session.
