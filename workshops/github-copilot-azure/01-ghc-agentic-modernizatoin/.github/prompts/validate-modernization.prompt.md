---
description: Validate modernization with contract, runtime, container, security, and Bicep evidence.
agent: modernization-guide
---

Validate the completed workshop without modifying behavior. Run and report:

1. Release build for the solution and every deployable service;
2. characterization, contract, per-service, inter-service, and
   unavailable-service tests;
3. package vulnerability audit;
4. Docker Compose configuration and local smoke behavior when available;
5. `zsh -n` for every local workshop script in `scripts/`;
6. static Bicep build/lint without Azure control-plane operations;
7. searches for shared database ownership, credentials, stale architecture
   terminology, and non-zsh workflow references.

Treat `infra/*.bicep` as optional reference IaC. Do not run Azure control-plane
operations or add lifecycle automation.

Return a pass/fail table with exact commands. Distinguish an environment
limitation from a product failure. Do not claim a check ran unless it completed.
