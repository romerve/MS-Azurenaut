---
applyTo: "tests/**"
---

# Test evidence requirements

- Characterization tests lock down the legacy baseline.
- Compare public HTTP status, headers, and normalized JSON through Legacy and Edge.
- Inter-service tests must cross HTTP/TestServer boundaries, not shared classes.
- Use isolated SQLite stores per service and never share a DbContext.
- Include success, conflict, idempotent retry, and unavailable-service evidence.
- Any bug fix requires a regression test that fails before the fix.
- A passing build without passing tests is not modernization evidence.
