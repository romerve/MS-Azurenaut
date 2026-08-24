# Month 2 — Agentic Development

A complete, standalone 60-minute workshop for turning an underspecified issue into
a reviewable .NET 10 feature, then debugging a credible large-cart defect with
reproducible red-green-refactor evidence. No prior month or Azure subscription is
required.

## Outcomes

Attendees will:

1. Convert a vague request into Given/When/Then criteria and persistent repository
   context.
2. Deliver deterministic per-client checkout rate limiting with configurable permit
   and window settings, `429`, and `Retry-After`.
3. Use an abstract clock to prove time-window behavior without sleeps.
4. Reproduce, fix, and refactor a large-cart integer-overflow defect in an isolated
   generated workspace while the checked-in solution stays green.
5. Review claims against executable tests instead of a model transcript.

## Prerequisites

- .NET SDK 10
- zsh, curl, and Git
- Optional: Docker for the image build exercise
- Optional: GitHub Copilot in VS Code for the live agent interaction

No Azure resources, secrets, prior workshop, or live model response is required.

## 60-minute agenda

| Time | Topic | Narrative |
|---|---|---|
| 00–05 | Welcome and baseline | Independence, goals, local health check |
| 05–35 | Primary: issue-to-feature delivery | Challenge → Challenge Demo → Solution → Solution Demo → Outcome → Closing |
| 35–55 | Secondary: grounded debugging | Challenge → Challenge Demo → Solution → Solution Demo → Outcome → Closing |
| 55–60 | Workshop closing | Evidence recap and take-home path |

See [talk-track.md](./talk-track.md) for every minute boundary and
[session-guide.md](./session-guide.md) for facilitator narration.

## Quick start

```zsh
cd workshops/github-copilot-azure/02-ghc-agentic-development
dotnet build src/Checkout.Api/Checkout.Api.csproj -c Release
dotnet build GitHubCopilotAzure.Development.slnx -c Release
dotnet test GitHubCopilotAzure.Development.slnx -c Release --no-build
./scripts/context-demo.zsh all
./scripts/bug-lab.zsh all
./scripts/smoke-test.zsh
```

Or run the complete local path:

```zsh
./scripts/validate.zsh
```

## Runnable application

`POST /checkout` requires `X-Client-Id` and JSON:

```json
{
  "items": [{ "quantity": 2, "unitPrice": 12.50 }],
  "discountPercent": 10
}
```

The response is:

```json
{"subtotal":25.00,"discount":2.50,"total":22.50}
```

The default fixed window permits three valid requests per client every 60 seconds.
Settings live in
[`src/Checkout.Api/appsettings.json`](./src/Checkout.Api/appsettings.json).
Invalid requests return `400` and do not consume a permit. An exhausted client
receives `429`, problem details, and integer `Retry-After` seconds.
Cart count, quantity, unit price, and discount bounds reject unsafe arithmetic as
validation problems rather than allowing an overflow to become a server error.

`X-Client-Id` is intentionally a visible workshop seam. A production gateway must
derive or overwrite it from authenticated identity; it must not trust arbitrary
caller input. The in-memory lab limiter bounds active tracked clients and fails
closed with `429` when that capacity is full, preventing unbounded key retention.

## Workshop assets

| Asset | Purpose |
|---|---|
| [`issues/vague-rate-limit.md`](./issues/vague-rate-limit.md) | Safe challenge input |
| [`issues/improved-rate-limit.md`](./issues/improved-rate-limit.md) | Testable issue example |
| [`.github/`](./.github/) | Inactive nested instructions, agent, prompts, issue and PR templates |
| [`scripts/context-demo.zsh`](./scripts/context-demo.zsh) | Deterministic challenge/solution contrast |
| [`scripts/bug-lab.zsh`](./scripts/bug-lab.zsh) | Isolated red → green → refactor lab |
| [`expected-outputs/`](./expected-outputs/) | Stable markers, not fabricated transcripts |
| [`architecture/`](./architecture/) | Context-flow and app/test Mermaid sources |
| [`facilitator/review-checklist.md`](./facilitator/review-checklist.md) | Live review gate |
| [`demo-runbook.md`](./demo-runbook.md) | Exact commands, fallback, and reset |

Because `.github` is nested under this month, its customization files are examples
and do not activate for the parent repository.

## Test evidence

Focused rate-limit acceptance tests:

```zsh
dotnet test GitHubCopilotAzure.Development.slnx -c Release \
  --filter "FullyQualifiedName~CheckoutAcceptanceTests"
```

Large-cart regression:

```zsh
dotnet test GitHubCopilotAzure.Development.slnx -c Release \
  --filter "FullyQualifiedName~Large_cart_uses_decimal_arithmetic_without_overflow"
```

The bug lab intentionally runs a failing fixture in a disposable
`.demo-workspaces/bug-lab` directory, verifies that failure, then replaces it with
green and refactored fixtures. It never makes the checked-in application red.

## Architecture

- [Context to evidence flow](./architecture/context-flow.mmd)
- [Checkout app and test seams](./architecture/app-test-evidence.mmd)

## Troubleshooting and reset

- SDK mismatch: `dotnet --version` must report `10.x`.
- Port in use: run `PORT=5182 ./scripts/smoke-test.zsh`.
- Stale demo files: run `./scripts/reset.zsh`.
- Package restore unavailable: restore once on a connected network, then rerun with
  the local NuGet cache.
- If the red fixture passes, confirm the script copied `buggy.cs.fixture`; the script
  treats an unexpected pass as a failure.

## Official references

- [Repository custom instructions](https://docs.github.com/en/copilot/how-tos/configure-custom-instructions/add-repository-instructions)
- [Custom agents](https://docs.github.com/en/copilot/customizing-copilot/custom-agents/configuring-custom-agents)
- [Prompt files in VS Code](https://code.visualstudio.com/docs/copilot/customization/prompt-files)
- [ASP.NET Core integration tests](https://learn.microsoft.com/aspnet/core/test/integration-tests)
- [TimeProvider overview](https://learn.microsoft.com/dotnet/standard/datetime/timeprovider-overview)
- [ASP.NET Core error handling](https://learn.microsoft.com/aspnet/core/fundamentals/error-handling)
- [HTTP 429 status](https://www.rfc-editor.org/rfc/rfc6585#section-4)
