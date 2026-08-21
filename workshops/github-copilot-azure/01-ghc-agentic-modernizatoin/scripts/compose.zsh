#!/usr/bin/env zsh
set -euo pipefail

readonly WORKSHOP_ROOT="${0:A:h:h}"

require_docker() {
  command -v docker >/dev/null 2>&1 || {
    print -u2 "ERROR: Docker is required. Install Docker Desktop, Rancher Desktop, or Colima."
    return 1
  }
  docker info >/dev/null 2>&1 || {
    print -u2 "ERROR: The Docker daemon is not reachable. Start your container runtime."
    return 1
  }
}

compose() {
  if docker compose version >/dev/null 2>&1; then
    docker compose --project-directory "${WORKSHOP_ROOT}" "$@"
    return
  fi

  print -u2 "Docker Compose v2 plugin not found; using the official docker:cli image."
  docker run --rm \
    --volume /var/run/docker.sock:/var/run/docker.sock \
    --volume "${WORKSHOP_ROOT}:${WORKSHOP_ROOT}" \
    --workdir "${WORKSHOP_ROOT}" \
    docker:cli compose "$@"
}
