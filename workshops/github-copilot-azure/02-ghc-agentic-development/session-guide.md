# Month 2 facilitator session guide

**Duration:** 60 minutes

**Primary topic:** Context-engineered issue-to-feature delivery

**Secondary topic:** Grounded red-green-refactor debugging

This month is self-contained. Use the checked-in fixtures and commands; do not depend
on Month 1, Azure, a live coding-agent result, or a fixed model transcript.

## Before the room

1. Run `./scripts/validate.zsh`.
2. Open the vague and improved issue fixtures side by side.
3. Open the context-flow Mermaid source and test file.
4. Run `./scripts/reset.zsh`, then leave the terminal at this folder.
5. If using a live agent, treat its response as variable narration and use the
   deterministic scripts for the scored outcome.

## Timing overview

| Time | Duration | Segment |
|---|---:|---|
| 00–05 | 5 min | Welcome, independence, health check |
| 05–10 | 5 min | Primary — Challenge |
| 10–14 | 4 min | Primary — Challenge Demo |
| 14–20 | 6 min | Primary — Solution |
| 20–30 | 10 min | Primary — Solution Demo |
| 30–33 | 3 min | Primary — Outcome |
| 33–35 | 2 min | Primary — Closing |
| 35–38 | 3 min | Secondary — Challenge |
| 38–41 | 3 min | Secondary — Challenge Demo |
| 41–45 | 4 min | Secondary — Solution |
| 45–52 | 7 min | Secondary — Solution Demo |
| 52–54 | 2 min | Secondary — Outcome |
| 54–55 | 1 min | Secondary — Closing |
| 55–60 | 5 min | Workshop closing |

## Primary topic: Context-engineered feature delivery

### Challenge

A one-line issue such as “Add rate limiting” omits policy, identity, response
semantics, test seams, and non-goals. An agent can produce plausible code while
reviewers have no objective definition of done.

Ask attendees which decisions are product decisions rather than implementation
details: client identity, permit count, window behavior, invalid-request accounting,
and `Retry-After`.

### Challenge Demo

Run:

```zsh
./scripts/context-demo.zsh challenge
```

Show [`issues/vague-rate-limit.md`](./issues/vague-rate-limit.md). The script reports
zero acceptance scenarios, zero persistent context layers, and no rerunnable evidence
command. This is deterministic input analysis, not a claim about what a model would do.

### Solution

Introduce four persistent context layers:

1. repository-wide invariants;
2. path-scoped API and test guidance;
3. a constrained feature-delivery custom agent;
4. reusable assess, plan, implement, and review prompts.

Pair them with an issue whose Given/When/Then criteria define normal, boundary,
isolation, reset, and validation behavior. The implementation uses `TimeProvider` so
the window can be advanced in a test without sleeping.

### Solution Demo

Run:

```zsh
./scripts/context-demo.zsh solution
dotnet test GitHubCopilotAzure.Development.slnx -c Release \
  --filter "FullyQualifiedName~CheckoutAcceptanceTests"
```

Walk the evidence chain:

1. compare the improved issue with the vague fixture;
2. open `.github/copilot-instructions.md` and the path-scoped files;
3. show the feature-delivery agent's stop conditions;
4. trace `X-Client-Id` through validation and `FixedWindowClientRateLimiter`;
5. identify it as a workshop seam that a production gateway must derive from
   authenticated identity, then show the bounded fail-closed tracking capacity;
6. show `ManualTimeProvider.Advance` proving the reset boundary;
7. map each criterion to `CheckoutAcceptanceTests`;
8. use the nested PR template to record the exact command.

Optionally ask a live agent to assess the improved issue. Do not promise its wording.
Score the demonstration using the checked-in tests and context script.

### Outcome

- The issue defines five observable scenarios and explicit non-goals.
- Per-client limits are isolated and configurable.
- The third request under the test configuration returns `429` and `Retry-After: 60`.
- Invalid requests return validation problem details without consuming permits.
- Reviewers can rerun one exact filtered command.

### Closing

Persist context once, then require executable evidence for each acceptance claim.
The take-home assets are the issue template, instructions, custom agent, prompts,
PR checklist, and acceptance-test pattern.

## Secondary topic: Grounded red-green-refactor debugging

### Challenge

“Checkout sometimes fails for large carts” invites a speculative fix. A hidden
integer-cents conversion can overflow while ordinary totals pass. A narrated “fixed”
claim does not prove the reported boundary.

### Challenge Demo

Run:

```zsh
./scripts/bug-lab.zsh red
```

The script creates a temporary test project, copies the buggy calculator fixture, and
requires the large-cart assertion to fail. If it unexpectedly passes, the script
itself fails.

### Solution

Use a three-artifact loop:

- **Red:** one focused failing assertion proves the defect is understood.
- **Green:** decimal multiplication removes the integer overflow with the smallest
  change.
- **Refactor:** named helpers improve clarity while the same test remains green.

Each stage is a separate fixture copied into a generated workspace. The production
solution is green before, during, and after the demonstration.

### Solution Demo

Run:

```zsh
./scripts/bug-lab.zsh all
dotnet test GitHubCopilotAzure.Development.slnx -c Release \
  --filter "FullyQualifiedName~Large_cart_uses_decimal_arithmetic_without_overflow"
```

Point out the stage markers and actual xUnit result. Compare the buggy integer-cents
line with the decimal green fixture, then the helper-based refactor. Avoid pasting a
model response into evidence.

### Outcome

- The same boundary test is observed red, green, and green-after-refactor.
- The checked-in API calculates a 2,500,000,000 subtotal and 2,250,000,000 discounted
  total using decimal arithmetic.
- The exact reproduction is independent of model availability.

### Closing

Require the command and observed result, not a confident summary. The take-home asset
is an isolated bug-lab pattern that can demonstrate failure safely.

## Workshop closing

Reopen [the facilitator checklist](./facilitator/review-checklist.md). Ask attendees
to identify one claim and its evidence. End with the throughline: explicit context
constrains the work; reproducible tests ground the result.

## Fallbacks

- Live agent unavailable: use `context-demo.zsh` and the static customization files.
- NuGet unavailable: use the pre-restored cache from rehearsal and avoid clearing it.
- Port conflict: skip HTTP smoke or set another `PORT`.
- Time running short: run `bug-lab.zsh all` without opening every fixture.

## Reset

```zsh
./scripts/reset.zsh
git status --short
```

Only generated `.demo-workspaces` content is removed.

## References

The maintained official reference list is in
[README.md](./README.md#official-references).
