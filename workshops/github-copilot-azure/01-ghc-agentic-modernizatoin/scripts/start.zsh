#!/usr/bin/env zsh
set -euo pipefail

readonly SCRIPT_DIR="${0:A:h}"
source "${SCRIPT_DIR}/compose.zsh"

require_docker

legacy_url="http://localhost:5001"
services=(legacy edge)

if [[ "${1:-}" == "--sql" ]]; then
  legacy_url="http://localhost:5011"
  services=(sql legacy-sql edge)
elif [[ -n "${1:-}" ]]; then
  print -u2 "ERROR: Unknown option '${1}'. Use --sql for the optional SQL Server legacy workload."
  exit 2
fi

compose up --build --detach --wait "${services[@]}"
"${SCRIPT_DIR}/smoke-test.zsh" "${legacy_url}" "http://localhost:5002"
compose --profile sql down --volumes --remove-orphans
compose up --detach --wait "${services[@]}"

print "Workshop environment is ready:"
print "  Legacy API: ${legacy_url}"
print "  Microservices edge: http://localhost:5002"
