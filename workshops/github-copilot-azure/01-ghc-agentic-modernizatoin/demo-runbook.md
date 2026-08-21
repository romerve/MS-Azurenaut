# Month 1 Demo Runbook - Agentic Modernization

Run commands from this folder. Expected results describe checked-in behavior,
not fabricated GitHub Copilot output. Rehearse after dependency or image updates.

## 1. Preflight

```zsh
dotnet --version
docker version
zsh --version
jq --version
az bicep version
```

Required for the full local path: .NET 10 SDK, Docker, zsh, curl, and jq. Azure
CLI is optional.

## 2. Reset

```zsh
./scripts/cleanup.zsh
```

Expected:

```text
Removed workshop containers, networks, and local data volumes.
```

The command is idempotent. It resolves the workshop path from the script
location, so it can be called from any current directory.

## 3. Challenge demo

### 3.1 Build the baseline and target

```zsh
dotnet build GitHubCopilotAzure.Modernization.sln --configuration Release
```

Expected: zero errors. The .NET 6 legacy project emits `NETSDK1138` because its
framework is out of support.

### 3.2 Characterize observed behavior

```zsh
dotnet test tests/Legacy.Characterization.Tests \
  --configuration Release --no-restore
```

Expected:

```text
Passed: 2, Failed: 0
```

Open `LegacyOrderService.cs` and point to order creation, inventory mutation,
and fulfillment in one class. Do not show the credential-shaped placeholder as
a security exploit; it is intentionally non-secret.

### 3.3 Start the clean before/after environment

```zsh
./scripts/reset.zsh
```

Expected after image build and startup:

```text
Smoke test passed: seeded contract parity, order creation, cross-service fulfillment,
and inventory parity were verified. BLUE-LAMP available: 11.
Workshop environment is ready:
  Legacy API: http://localhost:5001
  Microservices edge: http://localhost:5002
```

The script recreates volumes after its proof and leaves the seeded state. If a
containerized restore fails with a NuGet TLS EOF, use the tested .NET commands
for the presentation and move the container proof to a machine whose Docker VM
can reach `api.nuget.org`.

### 3.4 Show the unchanged public shape

```zsh
curl --fail --silent http://localhost:5001/api/orders | jq
curl --fail --silent http://localhost:5002/api/orders | jq
curl --fail --silent http://localhost:5002/api/inventory/BLUE-LAMP | jq
```

Expected seeded values include order
`11111111-1111-1111-1111-111111111111`, customer `CUST-100`, SKU
`RED-CHAIR`, and `BLUE-LAMP` availability `12`.

## 4. Guided Copilot solution demo

Open this folder as the editor workspace. Select `modernization-guide`.

1. Run `.github/prompts/assess-modernization.prompt.md`.
   - Expect evidence tied to the .NET 6 project, coupled service, configuration,
     data model, observability, and test gaps.
   - Reject a response that claims commands ran without output.
2. Run `.github/prompts/plan-modernization.prompt.md`.
   - Expect an Edge seam, separate databases, internal APIs, failure semantics,
     and reversible dependency-ordered slices.
3. Run `.github/prompts/execute-modernization-slice.prompt.md`.
   - Use one slice only, such as Inventory reservation idempotency.
   - Expect a named public invariant and targeted test before broad validation.
4. Run `.github/prompts/validate-modernization.prompt.md`.
   - Expect a command/result table and explicit environment limitations.

Do not paste a canned response into the demo. Agent wording varies; the
repository and commands are the stable evidence.

## 5. Solution proof

### 5.1 Independently build all deployables

```zsh
for project in Edge Orders Inventory Fulfillment; do
  dotnet build "src/${project}.Api/${project}.Api.csproj" \
    --configuration Release --no-restore
done
```

Expected: four successful builds, each producing its own assembly.

### 5.2 Run all tests

```zsh
dotnet test GitHubCopilotAzure.Modernization.sln \
  --configuration Release --no-restore
```

Expected totals:

| Project | Passed |
|---|---:|
| Legacy.Characterization.Tests | 2 |
| Api.Contract.Tests | 5 |
| Edge.Integration.Tests | 3 |
| Orders.Tests | 2 |
| Inventory.Tests | 2 |
| Fulfillment.Tests | 2 |
| **Total** | **16** |

### 5.3 Prove public contract and inter-service flow

```zsh
dotnet test tests/Api.Contract.Tests \
  --configuration Release --no-restore
```

Expected: five tests pass. These tests do not replace HTTP with direct class
calls. Four separate TestServer hosts are connected by HTTP bridges.

### 5.4 Prove backend-unavailable behavior

```zsh
dotnet test tests/Edge.Integration.Tests \
  --configuration Release --no-restore \
  --filter FullyQualifiedName~Unavailable
```

Expected: the test passes after Edge returns HTTP 503 ProblemDetails within the
resilience time budget.

### 5.5 Prove idempotent partial-failure recovery

```zsh
dotnet test tests/Inventory.Tests \
  --configuration Release --no-restore
dotnet test tests/Fulfillment.Tests \
  --configuration Release --no-restore
```

Expected: two tests pass in each project. Reusing an order ID does not deduct
inventory twice, and downstream unavailability is propagated.

### 5.6 Validate dependencies, scripts, Compose, and Bicep

```zsh
dotnet list GitHubCopilotAzure.Modernization.sln package \
  --vulnerable --include-transitive
zsh -n scripts/*.zsh
docker run --rm \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v "$PWD:$PWD" -w "$PWD" \
  docker:cli compose config --quiet
az bicep build --file infra/main.bicep
az bicep lint --file infra/main.bicep
```

Expected: no vulnerable packages, no zsh parse errors, a valid Compose model,
and no Bicep diagnostics.

## 6. Optional SQL Server legacy demo

```zsh
cp .env.example .env
# Replace the placeholder SQL_PASSWORD.
./scripts/reset.zsh --sql
```

Legacy moves to `http://localhost:5011`; Edge remains on port 5002. SQL Server
2022 requires amd64. Use the default path on ARM64 if emulation is unreliable.

## 7. Optional Azure reference-IaC inspection

```zsh
az bicep build --file infra/main.bicep
az bicep lint --file infra/main.bicep
```

Expected: both commands complete without diagnostics. This is static template
validation only. Do not provision Azure resources during this workshop; use
`infra/README.md` to discuss the modeled topology and deliberate operational
gaps.

## 8. Local reset and cleanup

Local reset:

```zsh
./scripts/reset.zsh
```

Local cleanup:

```zsh
./scripts/cleanup.zsh
```

The workshop does not create Azure resources and includes no Azure lifecycle or
cleanup scripts.
