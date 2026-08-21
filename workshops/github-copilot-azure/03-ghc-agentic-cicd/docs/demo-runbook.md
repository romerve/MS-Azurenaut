# Deterministic demo runbook

Run from the Month 3 directory. Rehearse once without network beyond NuGet restore.

## 1. Preflight

```zsh
dotnet --version
ruby --version
for script in scripts/*.zsh; do zsh -n "$script"; done
./scripts/validate-local.zsh
./scripts/smoke.zsh
```

Checkpoint: .NET reports a 10.x SDK; six tests and all three validators pass.

## 2. Reproduce the challenge safely

```zsh
dotnet build src/Release.API/Release.API.csproj --configuration Release
```

Checkpoint: MSB1009 reports that the project file does not exist. This expected red
command has no Azure or registry side effect.

Show `fixtures/logs/challenge-run.log`, then use the exact prompt in
[`remediation-worksheet.md`](../fixtures/remediation-worksheet.md). If Copilot is
unavailable, attendees perform the same evidence review manually; do not substitute
a fabricated transcript.

## 3. Prove the repair and security structure

```zsh
./scripts/validate-workflows.zsh
dotnet build src/Release.Api/Release.Api.csproj --configuration Release
dotnet test Release.slnx --configuration Release --no-build
```

Checkpoint: the validator confirms the challenge defect remains intentionally
present while corrected CI, OIDC, action references, and supply-chain fixtures pass.

## 4. Prove grounded claims

```zsh
./scripts/validate-evidence.zsh
```

Checkpoint: seven sample evidence files match their hashes and seven claims each
have one known citation.

To demonstrate stale evidence without editing the owned workshop, copy the complete
Month 3 folder into a disposable teaching directory, alter one sample artifact there,
and run that copy's `./scripts/validate-evidence.zsh`. The validator intentionally
anchors evidence to its own workshop root. If no disposable copy is available, show
the stable failure shape in [`expected-outputs.md`](./expected-outputs.md).

## 5. Optional Docker smoke

```zsh
docker build --tag release-api:workshop .
docker run --rm --detach --name release-api-workshop --publish 8080:8080 release-api:workshop
curl --fail http://127.0.0.1:8080/health
curl --fail http://127.0.0.1:8080/version
docker stop release-api-workshop
```

Checkpoint: port 8080 returns only health and safe version metadata. If Docker is
unavailable, `./scripts/smoke.zsh` is the required fallback.

## 6. Reset

```zsh
./scripts/reset.zsh
```

This removes Month 3 `bin`, `obj`, `.artifacts`, and generated evidence only. It
does not touch sample fixtures, other months, Git state, Azure, or containers.
