# Repository instructions

## Scope

- This workshop contains one ASP.NET Core .NET 10 API and one xUnit test project.
- Keep feature changes inside `src/Checkout.Api` and matching evidence inside
  `tests/Checkout.Api.Tests`.
- Do not add Azure infrastructure, deployment automation, secrets, or identifiers.

## Delivery rules

- Start from explicit acceptance criteria and identify the smallest affected surface.
- Preserve the `TimeProvider` seam so time-sensitive tests remain deterministic.
- Keep rate-limit state bounded and treat the workshop header as a trusted-gateway seam,
  not a production authentication mechanism.
- Validate input before consuming a rate-limit permit.
- Use decimal arithmetic for money; never convert currency to `int` during totals.
- Return RFC-compatible problem details for validation and error responses.
- Add or update a focused test for every observable behavior change.
- Run the filtered acceptance test first, then the full solution in Release.
- Do not claim a test passed unless the command was executed and its output observed.
- Keep the change scoped to one issue; call out any unrelated finding instead of fixing it.
