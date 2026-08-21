#!/usr/bin/env zsh
set -euo pipefail

readonly SCRIPT_DIR="${0:A:h}"
source "${SCRIPT_DIR}/compose.zsh"

require_docker
compose --profile sql down --volumes --remove-orphans
print "Removed workshop containers, networks, and local data volumes."
