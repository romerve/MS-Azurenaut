#!/usr/bin/env zsh
set -euo pipefail

readonly SCRIPT_DIR="${0:A:h}"
source "${SCRIPT_DIR}/compose.zsh"

require_docker
compose down --volumes --remove-orphans
"${SCRIPT_DIR}/start.zsh" "$@"
