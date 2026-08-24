---
applyTo: "src/Checkout.Api/**/*.cs"
---

# Checkout API instructions

- Keep endpoints thin: validation and HTTP mapping belong in `Program.cs`; calculations
  and stateful behavior belong in testable services.
- Resolve clock access through `TimeProvider`; do not call `DateTime.UtcNow`.
- A rejected invalid request must not consume a client's permit.
- A `429` response must include an integer `Retry-After` header in seconds.
- Keep client tracking bounded and fail closed when active tracking capacity is full.
- `X-Client-Id` is a workshop seam. In production, derive or overwrite it from a
  trusted authenticated identity at the gateway; never trust a caller-selected value.
- Do not log request bodies or client identifiers.
