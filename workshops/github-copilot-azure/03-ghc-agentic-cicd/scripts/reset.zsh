#!/bin/zsh
emulate -LR zsh
setopt errexit nounset pipefail

readonly ROOT="${0:A:h:h}"
find "$ROOT/src" "$ROOT/tests" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
rm -rf "$ROOT/.artifacts"
find "$ROOT/fixtures/evidence/generated" -type f ! -name README.md -delete
print "PASS: removed local build, smoke, and generated-evidence artifacts\n"
