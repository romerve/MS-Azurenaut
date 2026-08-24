# Troubleshooting and reset

| Symptom | Deterministic recovery |
|---|---|
| `dotnet` is not 10.x | Install the .NET 10 SDK, then rerun `dotnet --version`. |
| NuGet restore fails | Verify normal package-source/network access and rerun `dotnet restore Release.slnx`; no extra tool is required. |
| Address already in use on 5183 | Stop the process you started or change the `URL` constant in your disposable copy; do not use name-based process killing. |
| `smoke.zsh` fails | Read `.artifacts/smoke-api.log`, run `dotnet build Release.slnx -c Release`, then retry. |
| Docker daemon unavailable | Skip Docker and use `./scripts/smoke.zsh`; Docker is optional. |
| Docker port 8080 is busy | Map another host port, for example `--publish 8081:8080`, and curl port 8081. |
| Workflow validator reports YAML | Fix the named nested fixture; Ruby's standard YAML parser is the syntax authority used locally. |
| Workflow validator reports provisioning | Remove the provisioning command. The deployment reference may only update an existing Container App. |
| Evidence hash mismatch | Treat the claim as stale. Regenerate the artifact and manifest deliberately; never merely change the expected hash to make it green. |
| Evidence citation unknown | Add a real manifest entry and hashed file, or change the claim to `not established`. |
| OIDC login fails in an activated copy | Verify existing repository variables, federated subject/environment, audience, and existing Azure authorization with an administrator. Do not add `AZURE_CREDENTIALS` or widen scope as a diagnostic shortcut. |
| Container App cannot pull | Verify the repository-derived GHCR subject, approved digest, and the app's pre-existing registry authorization. The workflow intentionally does not create or repair infrastructure. |

## Reset

```zsh
./scripts/reset.zsh
```

If the optional Docker demo was interrupted:

```zsh
docker stop release-api-workshop
```

The helper is Month 3-scoped and does not modify Git, Azure, or other workshop
folders.
