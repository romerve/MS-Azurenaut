#!/bin/zsh
emulate -LR zsh
setopt errexit nounset pipefail

readonly ROOT="${0:A:h:h}"

print "==> Checking zsh syntax"
for script in "$ROOT"/scripts/*.zsh; do
  zsh -n "$script"
done

print "==> Building API"
dotnet build "$ROOT/src/Release.Api/Release.Api.csproj" --configuration Release

print "==> Building solution"
dotnet build "$ROOT/Release.slnx" --configuration Release

print "==> Running tests"
dotnet test "$ROOT/Release.slnx" --configuration Release --no-build

print "==> Checking vulnerable packages"
dotnet list "$ROOT/Release.slnx" package --vulnerable --include-transitive

print "==> Validating workflows, evidence, and links"
"$ROOT/scripts/validate-workflows.zsh"
"$ROOT/scripts/validate-evidence.zsh"
"$ROOT/scripts/validate-links.zsh"

print "PASS: Month 3 local validation completed"
