#!/usr/bin/env zsh
set -euo pipefail

ROOT=${0:A:h:h}

dotnet build "$ROOT/src/Checkout.Api/Checkout.Api.csproj" -c Release --nologo
dotnet build "$ROOT/GitHubCopilotAzure.Development.slnx" -c Release --nologo
dotnet test "$ROOT/GitHubCopilotAzure.Development.slnx" -c Release --no-build --nologo
"$ROOT/scripts/context-demo.zsh" all
"$ROOT/scripts/bug-lab.zsh" all
"$ROOT/scripts/smoke-test.zsh"
