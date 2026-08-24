# Add deterministic per-client checkout rate limiting

## Problem

One client can saturate checkout processing. We need a configurable fixed-window limit
without affecting other clients or making tests depend on wall-clock sleeps.

## Acceptance criteria

- **Given** a valid `X-Client-Id` below its configured permit limit, **when** checkout is
  submitted, **then** the existing successful checkout response is unchanged.
- **Given** one client has used every permit in the active window, **when** it submits
  another valid checkout, **then** the API returns `429` with integer `Retry-After`
  seconds and RFC-compatible problem details.
- **Given** client A is limited, **when** client B submits a valid checkout, **then**
  client B is not limited by A's usage.
- **Given** a client's window has elapsed, **when** it submits again, **then** a new
  window starts without sleeping in tests.
- **Given** an invalid request, **when** validation fails, **then** it returns `400`
  and does not consume a permit.

## Constraints and non-goals

- Configure permit count and window seconds through `appsettings.json`.
- Use `TimeProvider` as the clock seam and preserve deterministic tests.
- Do not add distributed storage, Azure resources, authentication, or deployment work.

## Required evidence

```zsh
dotnet test GitHubCopilotAzure.Development.slnx -c Release \
  --filter "FullyQualifiedName~CheckoutAcceptanceTests"
```
