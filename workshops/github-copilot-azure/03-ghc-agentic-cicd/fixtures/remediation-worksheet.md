# Failure remediation worksheet

Use only the deterministic [sanitized failure log](./logs/challenge-run.log), the
[broken workflow](../.github/workflows/challenge-broken.yml), and repository files.
Do not infer Azure state and do not request broader permissions.

## Prompt for Copilot

> Diagnose this workflow failure from the supplied files. Return: (1) one-sentence
> root cause, (2) the smallest patch, (3) a verification command, and (4) residual
> risk. Every factual statement must cite exact evidence as
> `relative/path:Lx-Ly`. If evidence is absent, say `not established`. Do not
> propose resource creation, role assignment, credential creation, or permission
> expansion.

## Reviewer worksheet

| Check | Exact citation | Pass? |
|---|---|---|
| The diagnosis quotes the failing command | `fixtures/logs/challenge-run.log:L2` | ☐ |
| The diagnosis quotes MSB1009 | `fixtures/logs/challenge-run.log:L3-L4` | ☐ |
| The patch identifies the incorrect path in YAML | `.github/workflows/challenge-broken.yml:L23` | ☐ |
| The corrected path exists | `src/Release.Api/Release.Api.csproj:L1` | ☐ |
| Verification is deterministic | `scripts/validate-workflows.zsh:L1-L127` | ☐ |
| No unsupported “Azure permission” claim appears | `not established` is used | ☐ |

## Expected human conclusion

The run fails before any deployment because the project path uses `Release.API`
instead of the checked-in `Release.Api`. The smallest change is the path correction
shown in [`ci.yml`](../.github/workflows/ci.yml). Validate with
`./scripts/validate-workflows.zsh`; do not widen permissions.
