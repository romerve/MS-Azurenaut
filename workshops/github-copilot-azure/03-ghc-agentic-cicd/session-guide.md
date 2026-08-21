# Month 3 Session Guide — Agentic CI/CD: Actions Authoring, Failure Remediation & Release Assurance

**Duration:** 60 minutes · **Format:** Live demo-driven session, single presenter or presenter + co-driver · **Audience:** see [README.md](./README.md)

> **Note on demos:** All commands, workflow YAML, and "Copilot says…" excerpts in this guide are **illustrative facilitator scripts** to run live against a real repository and Azure subscription during the session. They are not captured transcripts of an actual run, and outputs will vary by repository, model, Copilot version, and Azure environment — rehearse against your own demo subscription before presenting, and never commit real secrets or subscription IDs into slides or recordings.

## Timing overview

| Time | Duration | Segment |
|---|---|---|
| 0:00–0:05 | 5 min | Welcome, series framing, session objectives |
| 0:05–0:10 | 5 min | Primary topic — Challenge |
| 0:10–0:14 | 4 min | Primary topic — Challenge Demo |
| 0:14–0:21 | 7 min | Primary topic — Solution |
| 0:21–0:31 | 10 min | Primary topic — Solution Demo |
| 0:31–0:34 | 3 min | Primary topic — Outcome |
| 0:34–0:35 | 1 min | Primary topic — Closing |
| 0:35–0:38 | 3 min | Secondary topic — Challenge |
| 0:38–0:41 | 3 min | Secondary topic — Challenge Demo |
| 0:41–0:46 | 5 min | Secondary topic — Solution |
| 0:46–0:53 | 7 min | Secondary topic — Solution Demo |
| 0:53–0:55 | 2 min | Secondary topic — Outcome |
| 0:55–0:56 | 1 min | Secondary topic — Closing |
| 0:56–1:00 | 4 min | Session closing, Q&A, artifact recap |

---

## Primary topic: Agent-assisted GitHub Actions authoring and failure remediation for containerized Azure workloads (OIDC, least privilege)

**Scope:** authoring a deploy-to-Azure workflow with Copilot, migrating from long-lived secrets to OIDC federated credentials scoped to least privilege, and remediating a failing workflow run with agent assistance.

### Challenge

Hand-authoring GitHub Actions workflows for containerized Azure deployments is error-prone and often insecure by default: teams frequently start from a copy-pasted example that stores an `AZURE_CREDENTIALS` service-principal secret with subscription-level (or even Owner) scope, because scoping a role tightly and wiring up OIDC federation correctly takes extra, easy-to-skip steps. When the workflow later fails — a bad image tag, a permissions error, a YAML syntax mistake — engineers lose time bouncing between the Actions log, the Azure portal, and documentation to figure out which of the many possible causes applies, often re-running the same red job repeatedly while guessing.

### Challenge Demo

Facilitator shows a workflow authored the "quick way": a long-lived `AZURE_CREDENTIALS` secret created with a broad, subscription-scoped role assignment, and a deploy step that has a subtle misconfiguration (e.g., wrong container registry login server, or a missing `permissions:` block). Trigger the workflow, let it fail, and narrate the manual triage process: scrolling through raw logs, cross-referencing Azure RBAC docs, and the temptation to "just widen the role" to make the error go away rather than diagnosing the actual cause.

### Solution

Two changes address both the security and the remediation-speed problem:

1. **Author with OIDC and least privilege from the start.** Use GitHub's OIDC provider and Azure's workload identity federation so the workflow authenticates with a short-lived token instead of a stored secret, via the [`azure/login`](https://github.com/Azure/login) action. Scope the Microsoft Entra application's role assignment to the specific resource group (or resource) the workflow needs — not the subscription — and scope the federated credential's subject (`sub`) claim to the specific repository, branch, or environment that should be trusted. Copilot agent mode/coding agent can draft this workflow and the accompanying `az` CLI steps for the federated credential and role assignment from a plain-language prompt, which also makes the least-privilege scope an explicit, reviewable line in the diff instead of an invisible portal click.
2. **Remediate failures with agent assistance, but verify the diagnosis.** When a workflow run fails, use Copilot's Actions-integrated remediation (or drive the equivalent in agent mode by pointing it at the failed run's logs) to get a proposed root cause and fix. The agent reads the actual failure logs, the workflow YAML, and relevant code — the same evidence a human would use — and proposes a targeted change (not a blanket permission increase). The facilitator discipline: read the proposed diagnosis against the actual log line it cites before accepting the fix.

### Solution Demo

1. Show (or have Copilot draft) the `az ad app create` / federated-credential / role-assignment sequence scoped to a single resource group, and the corresponding workflow `permissions:` block (`id-token: write`, `contents: read`) plus the `azure/login` step using `client-id`/`tenant-id`/`subscription-id` — no `AZURE_CREDENTIALS` secret required.
2. Re-run the deploy step from the Challenge Demo, now targeting a containerized Azure workload (Container Apps or AKS), and show it succeed with the scoped identity.
3. Reintroduce (or reuse) a failing run — e.g., an image tag mismatch — and use Copilot's Actions failure remediation entry point (or agent mode pointed at the run's logs) to show the proposed root cause and fix.
4. Narrate the verification step: confirm the proposed fix's cited log line actually matches the real failure, then re-run the workflow to confirm green.

### Outcome

- The deploy workflow authenticates to Azure with short-lived OIDC tokens instead of a stored long-lived secret, and its Azure role assignment is scoped to exactly the resource group it needs.
- Workflow failures are triaged from actual log evidence in minutes instead of guesswork across multiple tools, whether via Copilot's Actions-integrated remediation or agent mode reading the same logs.
- The least-privilege scope and the OIDC trust conditions are visible, reviewable lines in version control, not a one-time manual portal configuration nobody remembers the reasoning for.

### Closing

The takeaway: let Copilot draft the workflow and the identity-scoping steps together, so least privilege is a reviewable line in a diff rather than a manual step that gets skipped under time pressure — and when something goes red, read the agent's cited evidence before accepting its fix, the same discipline from Month 2's red-green-refactor loop applied to CI failures. Next: once the pipeline runs safely, what evidence do you actually have before you ship?

---

## Secondary topic: Release safety and supply-chain evidence

**Scope:** dependency review, code scanning, SBOM/attestation, protected environments, and grounded release summaries.

### Challenge

Teams under release pressure often ship a container image with only an implicit sense that "the tests passed," with no compiled record of what dependencies changed, what static analysis found, what's actually inside the image, or who approved the production deployment. When someone later asks "did we ship anything with a known-vulnerable dependency, and can we prove what we approved and why," the honest answer is frequently "we'd have to go dig through several systems and hope the logs are still there" — or worse, an AI-generated release summary is trusted at face value with no way to verify its claims against real scan or approval data.

### Challenge Demo

Facilitator shows a pull request that bumps a dependency and modifies a Dockerfile, merged and deployed with no dependency review gate, no code scanning results surfaced in the PR, no SBOM or attestation for the resulting image, and no environment protection rule on the production deployment job — it just runs. Ask: if this dependency turned out to have a known CVE, or the image needed to be recalled, how would you find out what's in it and who approved shipping it? Narrate the gap.

### Solution

Wire supply-chain evidence into the same pipeline as first-class, reviewable gates instead of an afterthought:

1. **Dependency review** on pull requests — the `dependency-review-action` reports newly introduced dependencies and can block the merge on a configured severity threshold or disallowed license, surfaced directly in the PR diff.
2. **Code scanning (CodeQL)** — static analysis results surfaced as PR-level alerts before merge, not discovered after release.
3. **SBOM and build provenance attestations** — generate an SPDX or CycloneDX SBOM, attest it with `actions/attest-sbom`, and separately use `actions/attest-build-provenance` for the image digest. Grant only `contents: read`, `id-token: write`, and `attestations: write` (plus `packages: write` when publishing to GHCR).
4. **Protected environments** — gate the production deployment job behind a `production` environment with required reviewers, so the deployment itself carries a recorded, auditable approval.
5. **A grounded release summary** — a short summary whose every claim links to one of the above artifacts (the dependency review result, the code scanning status, the attestation, the environment approval) rather than a free-form AI narrative asserting "this release is safe." Copilot can draft the summary, but the facilitator discipline is the same as Month 2's: every sentence should be traceable to a real, checkable artifact.

### Solution Demo

1. Add (or show) a `dependency-review-action` step on the pull request from the Challenge Demo and show its result annotated directly in the PR.
2. Show code scanning (CodeQL) alerts surfaced on the same PR, and how they block or flag before merge.
3. Generate an SPDX SBOM after the image build, attest it with `actions/attest-sbom` using the image name, digest, and SBOM path, then add `actions/attest-build-provenance` for the same image digest. Show both attestations in the repository's artifact attestations view.
4. Show the production deploy job scoped to a `production` GitHub environment with a required reviewer, and walk through the approval gate pausing the run.
5. Draft a release summary (with Copilot's help) that explicitly cites: the dependency review outcome, the code scanning status, the attestation reference, and who approved the environment gate — contrasted with a generic, unsourced "this release looks good" summary from the Challenge Demo.

### Outcome

- Every production deployment carries a verifiable trail: what dependencies changed, what static analysis found, a signed record of what was built and how, and who approved the deployment.
- The release summary is a set of claims a reviewer can independently re-check against real artifacts, not an assertion to take on faith.
- Recalling or auditing a specific release becomes a lookup against existing evidence instead of an investigation.

### Closing

The takeaway: treat release safety the same way Month 2 treated a bug fix — insist on reproducible, checkable evidence for every claim, and let Copilot help produce and summarize that evidence rather than replace it. A release summary is only as trustworthy as the artifacts it points to.

---

## Demo environment setup notes

- Use a disposable or sandbox Azure subscription and resource group for the OIDC/least-privilege demo; do not run this against a production tenant.
- Pre-create the Microsoft Entra application registration shell (but leave the federated credential and role assignment steps to demo live) so you can show the "before" state quickly.
- Have Copilot's Actions-integrated failure remediation entry point enabled on the demo repository ahead of time and confirm it appears on a red run in a dry run before presenting; if unavailable in your plan, substitute agent mode pointed at the same failed run's logs.
- Enable code scanning (CodeQL) and the dependency review action on the demo repository in advance so results are ready to show, and confirm `id-token: write` / `attestations: write` permissions are configured for attestation (plus `packages: write` only if publishing to GHCR).
- Pre-configure a `production` GitHub environment with a required reviewer (a second facilitator account works well for a live approval demo).

## Outcomes checklist (for facilitators to confirm before closing)

- [ ] Attendees saw a deploy workflow authenticate via OIDC with no long-lived secret, scoped to a single resource group.
- [ ] Attendees saw a red run triaged from actual log evidence, with the proposed fix checked against that evidence before acceptance.
- [ ] Attendees saw dependency review and code scanning results surfaced in a PR, and an SBOM/build provenance attestation generated for a container image.
- [ ] Attendees saw a protected environment approval gate a production deployment, and a release summary whose claims trace to real artifacts.

## References

- [Configuring OpenID Connect in Azure](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure) — GitHub Docs
- [About security hardening with OpenID Connect](https://docs.github.com/en/actions/concepts/security/openid-connect) — GitHub Docs
- [Authenticate to Azure from GitHub Actions by OpenID Connect](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect) — Microsoft Learn
- [`azure/login` GitHub Action](https://github.com/Azure/login) — GitHub (Azure)
- [Publish revisions with GitHub Actions in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/github-actions) — Microsoft Learn
- [Fixing alerts in a security campaign (Copilot cloud agent assignment)](https://docs.github.com/en/code-security/how-tos/manage-security-alerts/remediate-alerts-at-scale/fixing-alerts-in-security-campaign) — GitHub Docs
- [Managing environments for deployment](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments) — GitHub Docs
- [Configuring the dependency review action](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/manage-your-dependency-security/configure-dependency-review-action) — GitHub Docs
- [Code scanning](https://docs.github.com/en/code-security/code-scanning/automatically-scanning-your-code-for-vulnerabilities-and-errors/about-code-scanning) — GitHub Docs
- [Using artifact attestations to establish provenance for builds](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations) — GitHub Docs
- [`actions/attest` action](https://github.com/actions/attest) — GitHub

*References were verified as live official GitHub Docs / Microsoft Learn pages at the time this guide was written. Re-verify links periodically, as Actions security and Copilot documentation paths are updated frequently.*
