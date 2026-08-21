## Linked issue

Closes #

## Behavior

Describe the observable change and its boundaries.

## Acceptance evidence

| Criterion | Test or artifact | Result |
|---|---|---|
| Given / When / Then | `exact command` | Observed result |

## Review checklist

- [ ] Diff is scoped to the linked issue.
- [ ] Validation and error semantics are explicit.
- [ ] Time-dependent behavior uses the test clock; no sleeps.
- [ ] `429` responses include `Retry-After`.
- [ ] Money uses decimal arithmetic and includes large-cart coverage.
- [ ] Focused and full Release tests were run.
- [ ] No secrets, identifiers, infrastructure, or unrelated refactors were added.
