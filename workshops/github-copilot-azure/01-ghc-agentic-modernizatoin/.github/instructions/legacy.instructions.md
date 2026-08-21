---
applyTo: "src/Legacy.Ordering.Api/**"
---

# Legacy application guardrails

- Target .NET 6. This EOL runtime is intentional evidence for the assessment.
- Preserve existing routes, status codes, JSON property names, seed values, and
  fulfillment idempotency.
- Do not silently refactor away the coupled `LegacyOrderService` before
  characterization and parity tests capture its behavior.
- The placeholder legacy configuration key demonstrates an obsolete pattern;
  it must never contain a usable credential.
- Fix defects required to keep the baseline functional, but explain whether a
  change is characterization, remediation, or modernization.
