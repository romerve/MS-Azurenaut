#!/usr/bin/env zsh
set -euo pipefail

ROOT=${0:A:h:h}
MODE=${1:-all}
WORK="$ROOT/.demo-workspaces/context"

reset_workspace() {
  rm -rf "$WORK"
  mkdir -p "$WORK"
}

run_challenge() {
  reset_workspace
  cp "$ROOT/issues/vague-rate-limit.md" "$WORK/issue.md"

  print "=== challenge context ==="
  print "Issue: vague-rate-limit.md"
  print "Acceptance scenarios: 0"
  print "Persistent context layers: 0"
  print "Reviewable evidence command: absent"
  print "Readiness: insufficient"
}

run_solution() {
  reset_workspace
  cp "$ROOT/issues/improved-rate-limit.md" "$WORK/issue.md"
  mkdir -p "$WORK/.github"
  cp "$ROOT/.github/copilot-instructions.md" "$WORK/.github/"
  cp -R "$ROOT/.github/instructions" "$WORK/.github/"
  cp -R "$ROOT/.github/agents" "$WORK/.github/"
  cp -R "$ROOT/.github/prompts" "$WORK/.github/"

  local scenarios
  scenarios=$(grep -c -- '\*\*Given\*\*' "$WORK/issue.md")
  local evidence="absent"
  grep -q -- 'dotnet test' "$WORK/issue.md" && evidence="present"

  print "=== solution context ==="
  print "Issue: improved-rate-limit.md"
  print "Acceptance scenarios: $scenarios"
  print "Persistent context layers: 4"
  print "Reviewable evidence command: $evidence"
  print "Readiness: scoped"
}

case "$MODE" in
  challenge) run_challenge ;;
  solution) run_solution ;;
  all)
    run_challenge
    print
    run_solution
    ;;
  *)
    print -u2 "usage: $0 [challenge|solution|all]"
    exit 64
    ;;
esac
