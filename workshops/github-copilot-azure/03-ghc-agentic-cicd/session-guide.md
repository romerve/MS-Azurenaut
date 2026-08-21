# Month 3 session guide — Agentic CI/CD and grounded releases

**Duration:** 60 minutes

**Primary topic:** evidence-first workflow remediation (minutes 5–35)

**Complementary secondary topic:** supply-chain evidence and approval (minutes 35–55)

**Closing:** minutes 55–60

This workshop is independent. Use the checked-in deterministic fixtures; do not
invent a fixed Copilot transcript or imply that sample evidence came from a live run.

## Agenda

| Time | Segment |
|---|---|
| 00:00–05:00 | Welcome, architecture, objectives |
| 05:00–35:00 | Primary topic: Challenge → Closing |
| 35:00–55:00 | Secondary topic: Challenge → Closing |
| 55:00–60:00 | Outcome recap, Q&A, reset |

## Primary topic — evidence-first agentic workflow remediation

### Challenge (05:00–10:00)

A copied workflow fails. A plausible assistant may recommend more cloud permission,
a different SDK, or a retry, but none is grounded in the run. The safe challenge
fixture instead fails before cloud access: its case-sensitive project path is wrong.
Participants must separate observed evidence from inference.

### Challenge Demo (10:00–14:00)

1. Open [`challenge-broken.yml`](./.github/workflows/challenge-broken.yml).
2. Open the [sanitized log](./fixtures/logs/challenge-run.log).
3. Run the exact broken `dotnet build` command from the log and observe MSB1009.
4. Ask the room which statements the log establishes. Azure authorization, image
   publishing, and deployment are all **not established**.

The failure is deterministic, local, sanitized, and cannot alter infrastructure.

### Solution (14:00–21:00)

Use a constrained agent loop:

1. **Ground:** provide the log, workflow, and repository tree.
2. **Constrain:** demand exact `relative/path:Lx-Ly` citations, a smallest patch,
   one verification command, and `not established` for missing evidence.
3. **Review:** independently open every citation.
4. **Validate:** run the deterministic validator; do not equate persuasive prose
   with a passing check.

Use the exact prompt and review table in the
[remediation worksheet](./fixtures/remediation-worksheet.md).

### Solution Demo (21:00–31:00)

1. Submit the worksheet prompt to Copilot against the named files. Output varies;
   evaluate it with the worksheet rather than displaying a fabricated transcript.
2. Compare the minimal path repair with [`ci.yml`](./.github/workflows/ci.yml).
3. Run `./scripts/validate-workflows.zsh`.
4. Show the corrected CI's .NET 10 restore/build/test stages and bounded build
   revision.
5. Inspect [`deploy-existing-container-app.yml`](./.github/workflows/deploy-existing-container-app.yml):
   `contents: read`, `id-token: write`, `azure/login`, GitHub variables, timeout,
   concurrency, protected `production`, exact signer-workflow attestation checks,
   the repository-derived image subject, immutable digest, and update-only command.
6. Point out what is absent: credentials JSON, provisioning, role assignment, app
   registration, or broad permission.

### Outcome (31:00–34:00)

Participants can produce a minimal, evidence-cited remediation and prove structural
properties independently. They can also review an OIDC deployment reference without
needing Azure locally or confusing identity prerequisites with workflow provisioning.

### Closing (34:00–35:00)

An agent accelerates diagnosis; exact citations and executable validators establish
trust. A green CI run now answers “did it build and test?”—not yet “is this release
defensible?”

## Complementary secondary topic — supply-chain evidence

### Challenge (35:00–38:00)

“Tests passed, the image is secure, and production was approved” is three claims,
not evidence. Results may belong to another commit, an image tag may move, and a
summary can remain green after an artifact changes.

### Challenge Demo (38:00–41:00)

Open the [sample release summary](./fixtures/evidence/sample/release-summary.md) and
[sample manifest](./fixtures/evidence/sample/evidence-manifest.json). Emphasize the
sample label. In a disposable complete copy of the Month 3 folder, alter one sample
artifact and run that copy's `./scripts/validate-evidence.zsh`; the hash mismatch
makes stale evidence visible. Otherwise show the documented failure in
[expected outputs](./docs/expected-outputs.md).

### Solution (41:00–46:00)

Build a chain where each control emits reviewable evidence:

1. dependency review gates newly introduced high-severity dependencies;
2. CodeQL analyzes C# changes;
3. tests bind behavior to the build;
4. GHCR stores the image by digest;
5. SPDX SBOM and build provenance attest the digest;
6. the protected `production` environment records human approval;
7. the summary cites immutable, hashed evidence—or says `not established`.

The [Mermaid source](./docs/architecture.mmd) shows this path.

### Solution Demo (46:00–53:00)

1. Inspect the dependency-review and CodeQL fixtures.
2. Inspect [`publish-supply-chain.yml`](./.github/workflows/publish-supply-chain.yml)
   for GHCR publication, SBOM, attestations, minimal permissions, concurrency, and
   timeout.
3. Inspect the production environment gate in the existing-target deployment.
4. Run `./scripts/validate-evidence.zsh`.
5. Map each summary citation to its manifest entry and SHA-256 file.
6. Open the [generated template](./templates/release-summary.md) and explain that a
   missing deployment smoke artifact prohibits a runtime-health claim.

### Outcome (53:00–54:00)

Participants can explain which artifact supports each release claim, distinguish
sample from generated evidence, and reject a stale or uncited green claim.

### Closing (54:00–55:00)

Copilot may draft the summary, but the manifest, immutable subject, approval record,
and validator determine whether the words are publishable.

## Session Closing (55:00–60:00)

- Re-run `./scripts/validate-local.zsh` or show the rehearsed output.
- Ask one attendee to name a claim that remains `not established`.
- Recap: diagnose from evidence, patch minimally, validate structurally, publish by
  digest, attest, approve, and cite.
- Point to the [demo runbook](./docs/demo-runbook.md) and
  [troubleshooting/reset path](./docs/troubleshooting.md).
- Run `./scripts/reset.zsh` after the session.
