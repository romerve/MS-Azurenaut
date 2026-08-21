# Month 2 — Agentic Development: Context-Engineered Delivery & Grounded Debugging

A standalone 60-minute session on using GitHub Copilot's agentic features to take work **from a GitHub issue to a reviewable pull request**, and to run **test/debug loops** that are grounded in reproducible evidence rather than guesswork. No prior session in this series is required.

## Audience

- Application developers and tech leads shipping features and fixing bugs in an Azure-hosted codebase.
- Engineering managers and platform teams evaluating how to standardize Copilot usage (custom instructions, custom agents, prompt files) across a repository or organization.
- Anyone who has used Copilot for autocomplete but not yet for structured, repository-aware agentic workflows.

## Prerequisites

- A GitHub repository with **Issues** enabled and at least write access.
- **GitHub Copilot** enabled on the account/organization, ideally Business or Enterprise (needed for Copilot coding agent and Copilot code review).
- **Visual Studio Code** with the GitHub Copilot and GitHub Copilot Chat extensions, and **agent mode** available.
- A local clone of the demo repository with a runnable test suite (any language — the patterns are language-agnostic).
- Comfort with basic Git/GitHub flow: branches, commits, pull requests, reviews.

No Azure subscription is required for this session — Month 2 focuses on the development loop, not deployment.

## Learning objectives

By the end of this session, attendees will be able to:

1. Turn a GitHub issue into Copilot-usable context using **custom instructions** (`.github/copilot-instructions.md` and path-scoped `.instructions.md` files), **custom agents**, and **prompt files**, and use Copilot coding agent (or agent mode) to implement the change on a branch.
2. Define **acceptance tests** up front so an agent's output has an objective pass/fail signal, and structure the resulting pull request to be efficiently **reviewable** (scoped diff, linked issue, test evidence).
3. Run an **agentic red-green-refactor loop**: reproduce a failing test as evidence, let the agent drive it to green, then refactor with the safety net still in place — and recognize when the "evidence" an agent presents is not actually reproducible.

## Session agenda at a glance (60 minutes)

| Time | Segment | Topic |
|---|---|---|
| 0:00 – 0:05 | Welcome & framing | Series context, session goals |
| 0:05 – 0:35 | **Primary topic** | Context-engineered issue-to-feature delivery (Challenge → Closing) |
| 0:35 – 0:55 | **Secondary topic** | Agentic red-green-refactor loops grounded in reproducible evidence (Challenge → Closing) |
| 0:55 – 1:00 | Session closing | Recap, artifacts, next steps |

Full minute-by-minute timing, narration notes, and demo scripts are in **[session-guide.md](./session-guide.md)**.

This month is a complete facilitator outline, not a bundled runnable demo
repository. Prepare the examples below in a disposable repository before the
session, following the setup notes in the guide.

## Patterns attendees can reproduce

- Repository and path-scoped custom-instruction patterns.
- A custom-agent and prompt-file pattern for issue-to-PR delivery.
- An issue-template pattern that captures Copilot-consumable acceptance criteria.
- A red-green-refactor checklist for verifying an agent's fix with reproducible evidence.

## Related session

The secondary topic in this session (grounded debugging) pairs naturally with **Month 3's** release-safety topic — grounded evidence for a fix here becomes grounded evidence for a release summary there. See [Month 3](../03-ghc-agentic-cicd/).
