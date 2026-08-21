---
description: Execute one approved modernization slice with tests and a reviewable diff.
agent: modernization-guide
---

Implement only the next approved plan slice. Before editing, restate its public
API invariant, service/data owner, failure semantics, and expected proof. Reuse
repository patterns without sharing DbContexts or bypassing HTTP boundaries.
Add or update the smallest test that fails before the change and passes after it.

After editing:

1. run the targeted test;
2. run the full solution tests if targeted validation passes;
3. summarize the diff by behavior, not file count;
4. report any remaining risk or unexecuted validation explicitly.
