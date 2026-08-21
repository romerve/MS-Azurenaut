# Month 1 Talk Track - Agentic Modernization

Minute-by-minute presenter script for a 60-minute session. Use
[`demo-runbook.md`](./demo-runbook.md) for copy/paste commands.

Conventions:

- **Run** means execute against the repository.
- **Expect** is the verified category of output, not a fabricated transcript.
- **Ask** is an audience prompt.
- **Fallback** is honest narration when a live dependency fails.

## 0:00-0:05 - Welcome

**0:00** Introduce the retail order-management scenario and state that the
session starts from a runnable .NET 6 monolith and ends with four runnable .NET
10 deployables.

**0:01** Set the audience contract: developers will see code and tests,
architects will see ownership/failure boundaries, and DevOps engineers will see
containers, identity, telemetry, and Bicep.

**0:02 - Ask:** "How many of you have seen a framework upgrade described as a
modernization even though the deployment and data model stayed unchanged?"

**0:03** Name the two topics: agent-led decomposition first; proof, identity,
and observability second.

**0:04** Explain guided mode: the agent pauses at Assess, Plan, Execute, and
Validate boundaries. Live wording can vary; repository evidence cannot.

**Transition:** "We begin with the system we actually have, not the architecture
we wish somebody had built."

## Primary topic: Agent-led assessment, .NET 10 upgrade, and decomposition

### Challenge - 0:05-0:10

**0:05** Open `Legacy.Ordering.Api.csproj`. Point to `net6.0` and describe the
support risk without shaming the team that owns it.

**0:06** Open `LegacyOrderService.cs`. Show order reads and creation in the same
class that owns inventory checks.

**0:07** Continue to fulfillment. Point out synchronous stock mutation and the
absence of an explicit failure boundary.

**0:08** Open legacy `appsettings.json`. Explain that the credential-shaped
value is deliberately fake and pedagogical, not a vulnerability demo.

**0:09 - Ask:** "If we changed the target framework and got a green build, what
would still be unknown?" Capture contract, data, latency, partial failure, and
operations answers.

### Challenge Demo - 0:10-0:15

**0:10 - Run:**

```zsh
dotnet build GitHubCopilotAzure.Modernization.sln --configuration Release
```

**Expect:** zero errors and a .NET 6 end-of-support warning.
**Fallback:** If restore is unavailable, show the project target and say the
warning is expected evidence; do not claim a build ran.

**0:11** Explain that the warning tells us *why* to change, not *how* to change.

**0:12 - Run:**

```zsh
dotnet test tests/Legacy.Characterization.Tests \
  --configuration Release --no-restore
```

**Expect:** two passing tests.
**Fallback:** Open the two tests and identify the seeded order and inventory
behavior they preserve.

**0:13** Show that characterization is intentionally incomplete at the start of
the story. It captures enough known behavior to begin, not every production risk.

**0:14 - Ask:** "Where would you put the first seam so mobile and partner callers
do not all migrate on the same day?" Land on a compatibility facade.

**Transition:** "Now we can ask Copilot for a constrained plan instead of an
unbounded rewrite."

### Solution - 0:15-0:22

**0:15** Open `.github/agents/modernization.agent.md`. Emphasize evidence,
invariants, one slice, and explicit validation.

**0:16** Open `assess-modernization.prompt.md`. Point out read-only assessment
and observed-versus-inferred labeling.

**0:17** Open `plan-modernization.prompt.md`. Show required service/data owners,
dependency order, failure semantics, rollback, and tests.

**0:18** Describe the Edge seam: same public routes, no business database, and
one place to map backend failures to stable HTTP responses.

**0:19** Describe data ownership: Orders, Inventory, and Fulfillment each have
their own EF DbContext and store. No service queries another service's tables.

**0:20** Describe call ownership: Orders asks Inventory for availability;
Fulfillment calls Orders and Inventory; Edge coordinates only the public facade.

**0:21** Explain why there is no broker: bounded synchronous interactions and
idempotent reservation satisfy this small demo. A broker would be justified by
different business durability or throughput requirements, not by fashion.

**Transition:** "Let's inspect the code that makes those claims executable."

### Solution Demo - 0:22-0:34

**0:22** Run the assessment prompt live. Check that its response cites files and
commands. If it does not, ask it to revise with evidence.

**0:23** Run the planning prompt. Highlight the compatibility seam and the
Inventory-before-Orders-before-Fulfillment dependency order.

**0:24** Open `Edge.Api/Program.cs`. Walk the unchanged public routes.

**0:25** Open `Edge.Api/ServiceClients.cs`. Show typed clients, correlation
handler, and the middleware that converts exhausted downstream failures to 503.

**0:26** Open `Orders.Api/OrderData.cs`. Show the Orders-only entity model.

**0:27** Open `Orders.Api/InventoryClient.cs`. Point out the five-second total
timeout, two-second attempt timeout, and two retries. Retries are bounded.

**0:28** Open `Inventory.Api/InventoryData.cs`. Show the separate Inventory and
Reservations tables.

**0:29** Point to `ReservationId = orderId`. Explain why replaying fulfillment
cannot decrement stock twice.

**0:30** Open `Fulfillment.Api/FulfillmentData.cs`. Show `Started`,
`InventoryReserved`, and `Completed`.

**0:31** Trace a partial failure: inventory succeeds, Orders is unavailable,
the caller sees 503, and retry resumes from stored progress.

**0:32 - Run:**

```zsh
for project in Edge Orders Inventory Fulfillment; do
  dotnet build "src/${project}.Api/${project}.Api.csproj" \
    --configuration Release --no-restore
done
```

**Expect:** four independently successful builds.
**Fallback:** Show the four project files and their four Dockerfiles; be clear
that the live build was blocked.

**0:33** Open `docker-compose.yml`. Show only Edge and Legacy mapped to host
ports; the three backends are network-internal and have separate volumes.

### Outcome - 0:34-0:37

**0:34** Summarize the deliverables: Edge, three business services, four
containers, and three data owners.

**0:35** Reiterate the public compatibility promise: clients still call
`/api/orders` and `/api/inventory`.

**0:36 - Ask:** "Which is more important here: number of services or clarity of
ownership and failure?" Expected answer: ownership/failure. More services alone
are not success.

### Closing - 0:37-0:39

**0:37** State the primary lesson: Copilot accelerates repository analysis and
slice execution when the constraints are explicit.

**0:38** State the remaining risk: distributed boundaries create more ways to
fail. A green compile cannot establish parity, recovery, identity, or diagnosis.

**Transition:** "The decomposition is plausible. Now it has to earn trust."

## Secondary topic: Contract proof, managed identity, and observability

### Challenge - 0:39-0:42

**0:39** Introduce distributed failure: a backend can be slow, unavailable, or
partially complete.

**0:40** Explain contract drift: callers observe status, headers, JSON property
shape, and error payloads, not internal architecture.

**0:41 - Ask:** "What should Edge return when Orders cannot be reached: 500,
503, stale success, or wait forever?" Use the answers to motivate explicit 503.

### Challenge Demo - 0:42-0:45

**0:42** Open `ApiParityTests.cs`. Show the same requests run against Legacy and
the four-server target topology.

**0:43** Open `ApiFactories.cs`. Show separate TestServer applications connected
through HTTP bridges; no shared DbContext makes the test pass.

**0:44** Open `EdgeFailureTests.cs`. Point out the unreachable endpoint and
bounded wait.

### Solution - 0:45-0:49

**0:45** Explain the proof stack: characterization, contract, service,
inter-service, unavailable-service, container, and infrastructure checks.

**0:46** Explain correlation: incoming `X-Correlation-ID` is preserved, otherwise
the trace identifier is used, and typed clients propagate it.

**0:47** Explain health: liveness has no dependency; readiness checks the owned
database. Application Insights receives OpenTelemetry only when configured.

**0:48** Explain Azure identity: four app identities, identity-scoped ACR pull,
three SQL users, and each user exists only in its owned database.

### Solution Demo - 0:49-0:57

**0:49 - Run:**

```zsh
dotnet test GitHubCopilotAzure.Modernization.sln \
  --configuration Release --no-restore
```

**Expect:** 16 tests pass across six projects.
**Fallback:** Use the test matrix in the runbook and open the most relevant
assertion. Do not report an unexecuted pass.

**0:50** Highlight five contract tests and their real four-application topology.

**0:51** Highlight Inventory idempotency and conflict tests.

**0:52** Highlight Fulfillment completion and unavailable-backend tests.

**0:53 - Run:**

```zsh
dotnet test tests/Edge.Integration.Tests \
  --configuration Release --no-restore \
  --filter FullyQualifiedName~Unavailable
```

**Expect:** pass with Edge returning HTTP 503.
**Fallback:** Show the test assertion and middleware mapping.

**0:54 - Run:**

```zsh
dotnet list GitHubCopilotAzure.Modernization.sln package \
  --vulnerable --include-transitive
```

**Expect:** no vulnerable packages.

**0:55 - Run:**

```zsh
zsh -n scripts/*.zsh
az bicep build --file infra/main.bicep
```

**Expect:** no diagnostics.
**Fallback:** If Azure CLI is absent, inspect the Bicep source and state that
static compilation was not run in the presentation environment.

**0:56** Open `infra/main.bicep` and `container-app.bicep`. Show external Edge,
internal backends, separate SQL databases, probes on 8080, and no passwords.
State that these files are reference IaC; the workshop provides no Azure
resource lifecycle automation.

### Outcome - 0:57-0:59

**0:57** Summarize proof: same public behavior, successful cross-service
fulfillment, idempotent retry, and an unavailable-service 503.

**0:58** Summarize operations: scoped identities, private data path, health
probes, structured logs, correlation, and OpenTelemetry.

### Closing - 0:59-1:00

**0:59** Close with: "Copilot accelerates the change; executable evidence earns
the right to ship it." Point to the runbook and cleanup command. Mention that
Month 2 is a separate issue-to-feature session and requires no Month 1 setup.
