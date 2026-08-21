#!/usr/bin/env zsh
set -euo pipefail

ROOT=${0:A:h:h}

rm -rf "$ROOT/.demo-workspaces/context"
rm -rf "$ROOT/.demo-workspaces/bug-lab"
rm -f "$ROOT/.demo-workspaces/checkout-api.log"
rmdir "$ROOT/.demo-workspaces" 2>/dev/null || true

print "Month 2 demo workspaces reset."
