# Month 2 deterministic demo runbook

## Rehearsal

```zsh
./scripts/reset.zsh
./scripts/validate.zsh
```

Expected: API and solution builds succeed, six production tests pass, the bug lab
observes one expected red stage followed by two green stages, and HTTP smoke returns
healthy and checkout JSON.

## Primary challenge

```zsh
./scripts/context-demo.zsh challenge
```

Expected markers are in
[`expected-outputs/context-demo.txt`](./expected-outputs/context-demo.txt).

## Primary solution

```zsh
./scripts/context-demo.zsh solution
dotnet test GitHubCopilotAzure.Development.slnx -c Release \
  --filter "FullyQualifiedName~CheckoutAcceptanceTests"
```

Open in order:

1. `issues/improved-rate-limit.md`
2. `.github/copilot-instructions.md`
3. `.github/agents/feature-delivery.agent.md`
4. `src/Checkout.Api/ClientRateLimiter.cs`
5. `tests/Checkout.Api.Tests/CheckoutAcceptanceTests.cs`

## Secondary challenge and solution

```zsh
./scripts/bug-lab.zsh red
./scripts/bug-lab.zsh green
./scripts/bug-lab.zsh refactor
```

Or use `./scripts/bug-lab.zsh all`. The red failure is expected and converted into a
successful stage only when the test genuinely fails. Stable markers are documented
in [`expected-outputs/bug-lab.txt`](./expected-outputs/bug-lab.txt).

## Application smoke

```zsh
./scripts/smoke-test.zsh
```

Expected bodies include `"status":"healthy"` and a checkout total. Exact JSON
whitespace is not instructional evidence.

## Safe reset

```zsh
./scripts/reset.zsh
```

The reset removes only named generated demo paths. It does not modify source,
fixtures, Git state, or package caches.
