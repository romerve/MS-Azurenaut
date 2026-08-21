# Facilitator checklist

## Before the workshop

- [ ] Run `./scripts/validate-local.zsh`.
- [ ] Run `./scripts/smoke.zsh`.
- [ ] If Docker is available, build and smoke the image on port 8080.
- [ ] Open the sanitized log, broken workflow, corrected CI, worksheet, sample
      manifest, and architecture source in editor tabs.
- [ ] Confirm all sample artifacts visibly say `SAMPLE EVIDENCE`.
- [ ] Decide whether Copilot is available; rehearse the manual evidence-review
      fallback without preserving a model transcript.
- [ ] Keep the nested workflows inactive in this repository.

## Optional live GitHub/Azure reference

- [ ] Use a disposable teaching repository.
- [ ] Copy only the reviewed reference workflow(s) to root `.github/workflows`.
- [ ] Confirm required repository variables exist without displaying their values.
- [ ] Confirm the Entra workload identity/federation and least-privilege access
      already exist.
- [ ] Confirm the Container App already exists and can pull
      `ghcr.io/<owner>/<repository>/release-api`, the exact image subject attested
      during publication.
- [ ] Confirm `production` has required reviewers.
- [ ] Use an immutable digest; never activate with a floating production tag.

Do not create resources, identities, credentials, role assignments, or federated
credentials during this workshop.

## After the workshop

- [ ] Stop the named Docker container if it was used.
- [ ] Run `./scripts/reset.zsh`.
- [ ] Confirm no generated evidence is mistaken for sample evidence or vice versa.
