#!/usr/bin/env zsh
set -euo pipefail

ROOT=${0:A:h:h}
MODE=${1:-all}
WORK="$ROOT/.demo-workspaces/bug-lab"

prepare() {
  local stage=$1
  rm -rf "$WORK"
  mkdir -p "$WORK"
  cp "$ROOT/fixtures/bug-lab/BugLab.csproj" "$WORK/"
  cp "$ROOT/fixtures/bug-lab/LargeCartTests.cs.fixture" "$WORK/LargeCartTests.cs"
  cp "$ROOT/fixtures/bug-lab/$stage.cs.fixture" "$WORK/CheckoutCalculator.cs"
}

run_expected_red() {
  prepare buggy
  print "=== RED: reproduce integer overflow ==="
  set +e
  dotnet test "$WORK/BugLab.csproj" -c Release --nologo
  local exit_code=$?
  set -e
  if (( exit_code == 0 )); then
    print -u2 "Expected the buggy fixture to fail, but it passed."
    return 1
  fi
  print "RED confirmed: large-cart regression test failed as expected."
}

run_green() {
  prepare green
  print "=== GREEN: decimal arithmetic fix ==="
  dotnet test "$WORK/BugLab.csproj" -c Release --nologo
  print "GREEN confirmed: focused regression test passed."
}

run_refactor() {
  prepare refactor
  print "=== REFACTOR: extracted currency helpers ==="
  dotnet test "$WORK/BugLab.csproj" -c Release --nologo
  print "REFACTOR confirmed: focused regression test remained green."
}

case "$MODE" in
  red) run_expected_red ;;
  green) run_green ;;
  refactor) run_refactor ;;
  all)
    run_expected_red
    run_green
    run_refactor
    ;;
  *)
    print -u2 "usage: $0 [red|green|refactor|all]"
    exit 64
    ;;
esac
