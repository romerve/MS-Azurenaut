# GitHub Copilot for Azure: A Three-Month Technical Series

A three-session, independently deliverable technical series for engineering teams building and operating Azure workloads with GitHub Copilot. Each session runs in **60 minutes**, stands alone (no session depends on attendees having taken the others), and pairs one **primary** topic with one **complementary secondary** topic so a single hour still covers a full problem-to-outcome arc twice.

The series intentionally moves left-to-right across the delivery lifecycle: modernize the code → engineer context to ship features and fix bugs with agents → operate the pipeline that deploys and safeguards that code on Azure.

## Series map

| Month | Theme | Primary topic | Secondary topic | Status |
|---|---|---|---|---|
| [Month 1 — Agentic Modernization](./01-ghc-agentic-modernizatoin/) | Decomposing an existing Azure workload with Copilot | Agent-led assessment and .NET 10 decomposition into Edge, Orders, Inventory, and Fulfillment services | Contract/failure proof, managed identity, and observability | Complete runnable before/after workshop |
| [Month 2 — Agentic Development](./02-ghc-agentic-development/) | Context-engineered delivery from issue to reviewable PR | Context-engineered issue-to-feature delivery (issues, custom instructions, custom agents, prompt files, acceptance tests, reviewable changes) | Agentic test/debug red-green-refactor loops grounded in reproducible evidence | Complete standalone outline |
| [Month 3 — Agentic CI/CD](./03-ghc-agentic-cicd/) | Operating and securing the pipeline that ships to Azure | Agent-assisted GitHub Actions authoring and failure remediation for containerized Azure workloads (OIDC, least privilege) | Release safety and supply-chain evidence (dependency review, code scanning, SBOM/attestation, protected environments, grounded release summaries) | Complete standalone outline |

## Who this series is for

Application developers, DevOps/platform engineers, and technical leads who already write code against Azure services and want to adopt GitHub Copilot's agentic features (coding agent, agent mode, custom agents, Copilot code review, Actions-integrated remediation) as part of their day-to-day delivery and operations workflow — not as a one-off demo.

## How the series is structured

Every session in this series follows the same six-part narrative for **each** topic it covers, so attendees always know where they are:

1. **Challenge** — the real, specific problem teams hit today (with concrete symptoms, not generalities).
2. **Challenge Demo** — a short, narrated walkthrough of the problem as it manifests, without Copilot's help.
3. **Solution** — the GitHub Copilot / Azure capability (or capability combination) that addresses the challenge, and why it works.
4. **Solution Demo** — the same scenario solved with the capability, narrated step by step.
5. **Outcome** — what changed for the team (measurable or observable), and what "good" looks like going forward.
6. **Closing** — the key takeaway, the artifact(s) attendees leave with, and the bridge to the next topic or session.

Each month folder contains:

- **`README.md`** — audience, prerequisites, learning objectives, and a quick-reference agenda.
- **`session-guide.md`** — the full facilitator guide: minute-by-minute timing, the Challenge/Challenge Demo/Solution/Solution Demo/Outcome/Closing narrative for both topics, demo flow and setup notes, outcomes checklist, and current official references.

Month 1 includes the runnable workshop and take-home assets. Per the series
scope, Months 2 and 3 are complete standalone facilitator outlines rather than
bundled demo repositories; their setup notes identify the examples a presenter
should prepare in a disposable demo repository.

## Prerequisites common to Month 2 and Month 3

- A GitHub.com organization or repository with **GitHub Copilot** enabled (Business or Enterprise recommended for coding agent, Copilot code review, and org-level custom instructions).
- Access to **GitHub Copilot in Visual Studio Code** (or another Copilot-enabled IDE) with agent mode available.
- An **Azure subscription** with permission to create a resource group and a Microsoft Entra app registration (for Month 3's OIDC demo).
- Familiarity with Git, pull requests, and at least one containerized Azure compute target (Azure Container Apps or Azure Kubernetes Service).
- The specific prerequisites for each month are restated in that month's `README.md` — no assumption is made that attendees sat through a prior month.

## Suggested delivery cadence

Run each session as a standalone 60-minute lunch-and-learn, brown bag, or internal enablement session, roughly one month apart, in any order — though the modernize → develop → operate arc above is the recommended default sequence for a cohort taking the full series.

## Reference index

Each month's `session-guide.md` carries its own curated, current reference list (GitHub Docs and Microsoft Learn) close to the content it supports. See:

- [Month 1 workshop references](./01-ghc-agentic-modernizatoin/README.md#official-references)
- [Month 2 session guide references](./02-ghc-agentic-development/session-guide.md#references)
- [Month 3 session guide references](./03-ghc-agentic-cicd/session-guide.md#references)
