---
applyTo: "tests/**/*.cs"
---

# Test instructions

- Tests must control time with `ManualTimeProvider`; never sleep.
- Assert externally observable HTTP status, headers, problem details, and response bodies.
- Give each test its own `WebApplicationFactory` and client identifier.
- Include the exact filtered test command in review evidence.
- A regression test must fail against the buggy fixture before the implementation changes.
