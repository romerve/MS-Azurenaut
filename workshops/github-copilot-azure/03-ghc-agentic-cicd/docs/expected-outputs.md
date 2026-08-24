# Expected deterministic outputs

Output timing and SDK patch versions may differ. These are stable shapes, not
captured model transcripts.

## Broken challenge

```text
MSBUILD : error MSB1009: Project file does not exist.
Switch: src/Release.API/Release.API.csproj
```

Exit code is nonzero by design.

## Workflow validator

```text
PASS: parsed 6 workflow fixtures
PASS: challenge defect is present and corrected CI properties are enforced
PASS: attestations, OIDC, least permissions, production gate, and update-only Azure scope are enforced
PASS: dependency review, CodeQL, GHCR, SBOM, and attestations are referenced
PASS: 20 action references use approved immutable commits
```

## Evidence validator

```text
PASS: 7 claims each cite one manifest entry
PASS: 7 evidence files exist and match SHA-256
PASS: sample/generated evidence markers agree
```

A changed artifact deterministically produces:

```text
ERROR: hash mismatch for <evidence-id>
```

## Tests and endpoint smoke

```text
Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6
PASS: /health and /version returned safe expected JSON
```

Response shapes:

```json
{"status":"healthy"}
```

```json
{"service":"Release.Api","version":"1.0.0","revision":"local"}
```
