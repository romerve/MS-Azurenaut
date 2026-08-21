# Month 2 Session Guide — Agentic Development: Context-Engineered Delivery & Grounded Debugging

**Duration:** 60 minutes · **Format:** Live demo-driven session, single presenter or presenter + co-driver · **Audience:** see [README.md](./README.md)

> **Note on demos:** All commands, file contents, and "Copilot says…" excerpts in this guide are **illustrative facilitator scripts** to run live against a real repository during the session. They are not captured transcripts of an actual run, and outputs will vary by repository, model, and Copilot version — rehearse against your own demo repo before presenting.

## Timing overview

| Time | Duration | Segment |
|---|---|---|
| 0:00–0:05 | 5 min | Welcome, series framing, session objectives |
| 0:05–0:10 | 5 min | Primary topic — Challenge |
| 0:10–0:14 | 4 min | Primary topic — Challenge Demo |
| 0:14–0:20 | 6 min | Primary topic — Solution |
| 0:20–0:30 | 10 min | Primary topic — Solution Demo |
| 0:30–0:33 | 3 min | Primary topic — Outcome |
| 0:33–0:35 | 2 min | Primary topic — Closing |
| 0:35–0:38 | 3 min | Secondary topic — Challenge |
| 0:38–0:41 | 3 min | Secondary topic — Challenge Demo |
| 0:41–0:45 | 4 min | Secondary topic — Solution |
| 0:45–0:52 | 7 min | Secondary topic — Solution Demo |
| 0:52–0:54 | 2 min | Secondary topic — Outcome |
| 0:54–0:55 | 1 min | Secondary topic — Closing |
| 0:55–1:00 | 5 min | Session closing, Q&A, artifact recap |

---

## Primary topic: Context-engineered issue-to-feature delivery

**Scope:** GitHub issues → custom instructions → custom agents → prompt files → acceptance tests → reviewable pull requests.

### Challenge

Teams adopting Copilot's agentic features (coding agent, agent mode) frequently see the same failure mode: the agent produces code that compiles but doesn't match the team's conventions, misses acceptance criteria that were only "in someone's head," or opens a pull request so broad that no reviewer can meaningfully approve it in a reasonable time. The root cause is almost always **missing or implicit context** — the issue doesn't state what "done" means in a testable way, the repository has no persisted conventions for Copilot to read, and there's no scoped, reusable way to hand the agent a well-formed task. The result is either agents that are avoided after one bad experience, or PRs that get rubber-stamped without real review.

### Challenge Demo

Facilitator opens a plain GitHub issue with only a one-line title (e.g., "Add rate limiting to the checkout API") and no acceptance criteria, then assigns it to Copilot coding agent (or drives it live in agent mode) with no repository custom instructions present. Narrate what happens:

- The agent has to guess at the rate-limiting strategy, the library/pattern to use, and where configuration should live, because none of that is written down anywhere it can read.
- The resulting diff touches more files than necessary and invents a testing approach that doesn't match the repo's existing test style.
- There is no acceptance test to check the PR against, so "is this done?" is a subjective call.

### Solution

Three complementary pieces of persisted, reusable context turn a vague ask into a scoped, checkable task:

1. **Custom instructions** — a repository-wide `.github/copilot-instructions.md` (plus optional path-scoped `.github/instructions/*.instructions.md` files using `applyTo` globs) that persist conventions, architecture notes, and "always/never" rules so every Copilot surface — coding agent, agent mode, chat, code review — reads the same ground truth automatically.
2. **Custom agents** (`.github/agents/*.agent.md`) — a named persona/toolset for a recurring job (e.g., "feature-delivery agent" that always writes a test first, keeps diffs scoped to the issue, and stops to ask before touching CI config).
3. **Prompt files** (`.github/prompts/*.prompt.md`) — reusable, parameterized `/command` templates for well-scoped, repeatable tasks (e.g., `/implement-issue` that takes an issue reference and acceptance criteria as input variables).

Layered on top, writing **acceptance criteria as testable statements directly in the issue** (Given/When/Then or an explicit list of test cases) gives both the agent and the human reviewer the same objective definition of done — and keeps the resulting PR **reviewable**: one linked issue, a diff scoped to that issue's acceptance criteria, and test evidence attached.

### Solution Demo

Facilitator repeats the same scenario with context engineered in:

1. Show a `.github/copilot-instructions.md` excerpt for the demo repo, e.g.:
   ```markdown
   # Copilot instructions

   ## Conventions
   - Rate limiting uses the existing `RateLimiter` middleware in `src/middleware/`.
   - Every new endpoint behavior change requires a corresponding test in `tests/`.
   - Keep pull requests scoped to a single linked issue; do not refactor unrelated code.
   ```
2. Show a path-scoped instructions file limiting API-specific guidance to `src/api/**` via `applyTo`.
3. Show a minimal custom agent definition (`.github/agents/feature-delivery.agent.md`) that names the persona, the tools it may use, and the "write a failing test first" rule.
4. Rewrite the same issue with explicit acceptance criteria (e.g., "Given more than 100 requests/minute from one client, When the limit is exceeded, Then the API returns HTTP 429 with a `Retry-After` header").
5. Assign the issue to Copilot coding agent (or drive the equivalent in agent mode using a `/implement-issue` prompt file), and walk through the resulting pull request: scoped diff, a new test that encodes the acceptance criteria, and a description that links back to the issue.
6. Open the PR in Copilot code review (or point out where a human reviewer would look first) and note how much less time review takes when the diff is scoped and self-evidenced.

### Outcome

- Pull requests generated by agents are diff-scoped to a single issue, reference explicit acceptance criteria, and include the test that proves them — cutting reviewer time and rework cycles.
- Conventions live in version-controlled files (`copilot-instructions.md`, `.instructions.md`, `.agent.md`, `.prompt.md`) instead of tribal knowledge, so they compound across every future issue instead of being re-explained each time.
- Teams gain a repeatable, auditable pattern: issue with acceptance criteria → custom-instruction-aware agent → reviewable PR — that scales to more contributors without a proportional increase in review overhead.

### Closing

The throughline: **an agent is only as good as the context you persist for it.** Write conventions once into custom instructions, package recurring jobs into custom agents and prompt files, and state acceptance criteria as testable statements in the issue itself — and the review bottleneck moves from "does this even make sense" to "does this meet the bar," which is a much faster review. Artifact to take away: the `copilot-instructions.md` / `.instructions.md` / `.agent.md` / `.prompt.md` starter set referenced above. Next: what happens when the agent's own test claims don't hold up — that's the secondary topic.

---

## Secondary topic: Agentic red-green-refactor loops grounded in reproducible evidence

**Scope:** Using Copilot agent mode/coding agent to drive test-driven debugging (red → green → refactor) with evidence that can be independently reproduced, not just narrated.

### Challenge

When an agent is asked to "fix the bug," a common failure mode is that it reports success — "I fixed the issue and the tests pass" — without ever having actually reproduced the failure first, or without the reviewer being able to reproduce the claimed fix independently. This produces confident-sounding but ungrounded claims: a test that was already passing, a fix that only works in the agent's transcript, or a refactor applied on top of a fix that was never actually verified red-to-green. Debugging sessions like this erode trust in agentic workflows faster than almost anything else.

### Challenge Demo

Facilitator shows a bug report with a vague repro ("checkout sometimes fails for large carts") and asks an agent to fix it directly, without first asking for a reproduction. Narrate what typically goes wrong: the agent proposes a code change and a chat message asserting the bug is fixed, but there is no new failing test that was observed to fail before the change and pass after — so there is no evidence beyond the agent's own claim, and a reviewer has no fast way to confirm the fix actually addresses the reported symptom.

### Solution

Ground the loop in the classic **red-green-refactor** discipline, but make each step produce an artifact a human can independently check:

- **Red:** require the agent (or the facilitator, working with the agent) to first write or identify a test that reproduces the reported failure, run it, and show the failure output. This is the evidence that the bug is real and understood.
- **Green:** have the agent make the smallest change that turns that specific test green, then re-run the full relevant test suite to confirm no regressions — the before/after test run is the evidence the fix works.
- **Refactor:** only after green, ask the agent to clean up the implementation with the now-passing test suite as a safety net, re-running tests after each refactor step.

The key discipline for the facilitator/reviewer: **ask for the command and its output at every step**, and re-run the failing/passing test yourself rather than accepting a narrated claim. Copilot agent mode's ability to execute terminal commands and read their actual output (rather than only generating code) is what makes this loop groundable in evidence instead of assertion.

### Solution Demo

Facilitator drives the same "checkout fails for large carts" scenario end to end:

1. **Red:** ask the agent to first write a test that reproduces the reported symptom (e.g., a checkout with 500 line items), run it, and show the failing output/stack trace live in the terminal — this is the reproducible evidence step, not a claim.
2. **Green:** ask the agent to make the minimal fix, re-run the same test and the surrounding test file/suite, and show the test suite go from red to green in the terminal output.
3. **Refactor:** ask the agent to simplify or clean up the fix (e.g., extract a helper, remove duplication) and re-run the full suite again after the refactor to confirm it's still green.
4. Close the loop by showing the PR description: it links the original bug report, includes the specific test added, and shows the red→green test run as the evidence trail — something a reviewer can re-run themselves rather than take on faith.

### Outcome

- Every debugging fix carries reproducible evidence (a failing test observed before the fix, a passing suite observed after) instead of an unverified narrative claim.
- Reviewers can independently re-run the same test commands the agent ran, cutting "trust but don't verify" risk out of agent-assisted debugging.
- The red-green-refactor discipline keeps fixes minimal and scoped, and the subsequent refactor step is protected by the same tests — so cleanup doesn't silently reintroduce the bug.

### Closing

The takeaway: treat an agent's claim of "fixed" the same way you'd treat a colleague's — ask to see it fail, then ask to see it pass, then let it refactor with the safety net in place. That discipline is what turns an agent from a fast typist into a debugging partner you can actually trust. This grounded-evidence habit — reproduce, fix, verify, refactor — is the same discipline Month 3 applies to release summaries and CI failure remediation: evidence over assertion, everywhere agents touch the delivery pipeline.

---

## Demo environment setup notes

- Use a demo repository with an existing test suite and CI (any stack) so the red-green-refactor demo has a real command to run (`npm test`, `dotnet test`, `pytest`, etc.).
- Pre-stage (but do not commit until the demo) the `.github/copilot-instructions.md`, an `.instructions.md` example, a `.github/agents/*.agent.md` file, and a `.github/prompts/*.prompt.md` file so you can reveal them incrementally rather than typing them live.
- Have Copilot coding agent enabled on the demo repository ahead of time, and confirm agent mode is active in your IDE before presenting — verify both in a dry run.
- Prepare one issue with only a title (for the Challenge Demo) and one duplicate with full acceptance criteria (for the Solution Demo) so the contrast is visible without live-editing under time pressure.

## Outcomes checklist (for facilitators to confirm before closing)

- [ ] Attendees can name the four context artifacts: custom instructions, path-scoped instructions, custom agents, prompt files.
- [ ] Attendees saw a before/after contrast: vague issue → broad diff, vs. scoped issue + context → reviewable diff.
- [ ] Attendees saw a real red→green→refactor terminal sequence, not a narrated claim.
- [ ] Attendees leave with (or know where to find) the starter template files referenced in this guide.

## References

- [About customizing GitHub Copilot responses (custom instructions)](https://docs.github.com/en/copilot/concepts/prompting/response-customization) — GitHub Docs
- [Adding repository custom instructions for GitHub Copilot](https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/add-custom-instructions/add-repository-instructions) — GitHub Docs
- [Prompt files](https://docs.github.com/en/copilot/tutorials/customization-library/prompt-files) — GitHub Docs
- [Use prompt files in VS Code](https://code.visualstudio.com/docs/agent-customization/prompt-files) — Visual Studio Code Docs
- [About GitHub Copilot cloud agent](https://docs.github.com/en/copilot/concepts/agents/cloud-agent/about-cloud-agent) — GitHub Docs
- [Research, plan, and iterate on code changes with Copilot cloud agent](https://docs.github.com/en/copilot/how-tos/copilot-on-github/use-copilot-agents/research-plan-iterate) — GitHub Docs
- [Using GitHub Copilot code review](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/request-a-code-review/use-code-review) — GitHub Docs
- [Pull requests](https://docs.github.com/en/pull-requests/reference/pull-requests) — GitHub Docs
- [Build with agents in VS Code](https://code.visualstudio.com/docs/agents/overview) — Visual Studio Code Docs

*References were verified as live official GitHub Docs / Microsoft Learn / Visual Studio Code Docs pages at the time this guide was written. Re-verify links periodically, as Copilot documentation paths are updated frequently.*
