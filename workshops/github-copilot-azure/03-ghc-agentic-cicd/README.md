# Month 3 — Agentic CI/CD and grounded release assurance

A self-contained, runnable **60-minute workshop** for using GitHub Copilot to
diagnose and repair CI safely, then make release claims from verifiable
supply-chain evidence. No earlier workshop or Azure subscription is required
for the local experience.

## Topics and outcomes

- **Primary:** evidence-first agentic workflow remediation. Diagnose a realistic,
  safe project-path failure; ask Copilot for a minimal patch with exact citations;
  and validate the result deterministically.
- **Complementary secondary:** release assurance. Connect PR checks, tests, a
  digest-addressed image, SBOM, provenance, approval, and an existing Azure
  Container App without turning an AI summary into unsupported fact.

At the end, participants can:

1. Distinguish diagnosis evidence from a plausible but unsupported explanation.
2. Review least-privilege OIDC workflow structure without storing Azure credentials.
3. Explain dependency review, CodeQL, GHCR, SBOM, build provenance, and protected
   environment gates.
4. Reject a green release claim when its cited evidence is missing or changed.

## Local prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- `zsh`, `ruby`, and `curl` (standard on current macOS; Ruby uses only its standard
  library)
- Optional: Docker for the container smoke test
- Optional: GitHub Copilot for the open-ended remediation prompt

**No Azure access is needed locally.** The optional deployment workflow only updates
resources that an operator has already prepared.

## Quick start

From this directory:

```zsh
./scripts/validate-local.zsh
./scripts/smoke.zsh
```

Optional container run:

```zsh
docker build --tag release-api:workshop .
docker run --rm --detach --name release-api-workshop --publish 8080:8080 release-api:workshop
curl --fail http://127.0.0.1:8080/health
curl --fail http://127.0.0.1:8080/version
docker stop release-api-workshop
```

Clean local artifacts:

```zsh
./scripts/reset.zsh
```

The API exposes:

- `GET /health` → `{"status":"healthy"}`
- `GET /version` → service, semantic version, and a bounded revision only

The version response never reflects arbitrary environment variables. Its build
revision is a deterministic MSBuild value (`local` by default, `container` in the
Dockerfile, or an explicitly supplied safe revision in CI).

## Workshop map

| Artifact | Purpose |
|---|---|
| [Session guide](./session-guide.md) | Explicit Challenge → Closing flow for both topics |
| [Minute-by-minute talk track](./docs/talk-track.md) | Presenter narration and handoffs |
| [Deterministic demo runbook](./docs/demo-runbook.md) | Commands, checkpoints, and fallback paths |
| [Expected outputs](./docs/expected-outputs.md) | Stable output shapes, not model transcripts |
| [Facilitator checklist](./docs/facilitator-checklist.md) | Setup and evidence inventory |
| [Troubleshooting](./docs/troubleshooting.md) | Recovery and reset |
| [Official references](./docs/references.md) | GitHub, .NET, OCI, and Microsoft Learn |
| [Architecture source](./docs/architecture.mmd) | Mermaid release path |
| [Remediation worksheet](./fixtures/remediation-worksheet.md) | Citation-constrained Copilot prompt |
| [Release-summary template](./templates/release-summary.md) | Generated-evidence claims |
| [Sample manifest](./fixtures/evidence/sample/evidence-manifest.json) | Deterministic sample evidence and hashes |

## Repository layout

```text
.
├── .github/workflows/       # intentionally inactive reference workflows
├── docs/                    # facilitator assets
├── fixtures/                # sanitized log and sample/generated evidence areas
├── scripts/                 # executable zsh helpers
├── src/Release.Api/         # ASP.NET Core .NET 10 API
├── tests/Release.Api.Tests/ # endpoint and metadata tests
├── templates/               # generated evidence templates
├── Dockerfile
└── Release.slnx
```

### Why the nested workflows do not run

GitHub only activates workflows from the repository-root `.github/workflows`.
These files are nested inside the Month 3 folder on purpose, so reading and local
validation cannot trigger Actions. Copy a fixture to the root only in a disposable
teaching repository after review.

- [`challenge-broken.yml`](./.github/workflows/challenge-broken.yml) contains the
  intentional case-sensitive project-path defect.
- [`ci.yml`](./.github/workflows/ci.yml) is the corrected CI reference.
- [`dependency-review.yml`](./.github/workflows/dependency-review.yml) and
  [`codeql.yml`](./.github/workflows/codeql.yml) are PR safety checks.
- [`publish-supply-chain.yml`](./.github/workflows/publish-supply-chain.yml)
  publishes to GHCR and records SPDX SBOM plus provenance attestations.
- [`deploy-existing-container-app.yml`](./.github/workflows/deploy-existing-container-app.yml)
  uses OIDC and a protected `production` environment to update an existing app.

## Optional existing-Azure reference

This workshop does **not** provision Azure resources. Before activating the
deployment fixture, an administrator must already have:

- an existing Container App configured to pull
  `ghcr.io/<owner>/<repository>/release-api`;
- an existing Microsoft Entra workload identity and GitHub federated credential;
- least-privilege authorization scoped to update only the intended existing target;
- GitHub repository variables named in the workflow;
- a protected GitHub `production` environment with required reviewers.

The workflow contains no resource-group creation, app registration, federated
credential creation, role assignment, infrastructure-as-code, or
`AZURE_CREDENTIALS`. Before Azure login, it verifies both provenance and SPDX SBOM
attestations for the repository's exact GHCR subject, signer repository, signer
workflow, and approved digest. It then runs only `az containerapp update`.

## Deterministic guardrails

`validate-workflows.zsh` parses every YAML fixture and asserts the intended
challenge defect, corrected paths, action references, OIDC permissions, production
gate, pinned immutable action commits, and absence of credential/provisioning
commands.

`validate-evidence.zsh` requires every release-summary claim to cite exactly one
manifest entry and verifies each evidence file's SHA-256. Try changing a sample
artifact: validation must fail until the manifest and claim set are deliberately
regenerated.

Files under [`fixtures/evidence/sample`](./fixtures/evidence/sample/) are visibly
**SAMPLE EVIDENCE**. Files from real runs belong under
[`fixtures/evidence/generated`](./fixtures/evidence/generated/) and must remain
marked **GENERATED EVIDENCE**. Never present samples as live results.
