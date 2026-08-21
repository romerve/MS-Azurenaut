# Generated evidence goes here

This directory is intentionally empty except for this note. Live workflow artifacts
must be downloaded here, marked `GENERATED EVIDENCE`, hashed in a
`generated-evidence-manifest`, and validated with:

```zsh
./scripts/validate-evidence.zsh path/to/evidence-manifest.json path/to/release-summary.md
```

Never relabel the deterministic files in `../sample/` as generated evidence.
