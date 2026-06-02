#!/usr/bin/env bash
# Idempotent deploy/update for the single-VM stack. Run from your DEV MACHINE.
#
# Handles BOTH the first deploy and every subsequent update:
#   - rsyncs the repo to the VM (diffs only; never touches the VM's .env)
#   - ensures SQL is up
#   - optionally publishes the dacpac schema (--schema)
#   - rebuilds the webapp image ON the VM and recreates only what changed
#
# Safe to run repeatedly. `docker compose up -d --build` is a no-op when nothing
# changed, and recreates a container only when its image/config actually differs.
#
# Usage:
#   ./deploy.sh <vm-ip-or-host> [--schema] [--no-build] [--logs]
#
# Flags:
#   --schema    Also publish the dacpac (needs MSSQL_SA_PASSWORD; runs publish-dacpac-vm.sh)
#   --no-build  Recreate containers without rebuilding the image
#   --logs      Tail webapp+caddy logs after deploying
#
# Env:
#   ADMIN_USER  SSH user (default: azureuser)
#   APP_DIR     Remote repo path (default: ~/app)
set -euo pipefail

VM_HOST="${1:?Usage: deploy.sh <vm-ip-or-host> [--schema] [--no-build] [--logs]}"
shift || true

DO_SCHEMA="false"; DO_BUILD="true"; DO_LOGS="false"
for arg in "$@"; do
  case "$arg" in
    --schema)   DO_SCHEMA="true" ;;
    --no-build) DO_BUILD="false" ;;
    --logs)     DO_LOGS="true" ;;
    *) echo "Unknown flag: $arg" >&2; exit 2 ;;
  esac
done

ADMIN="${ADMIN_USER:-azureuser}"
APP_DIR="${APP_DIR:-/home/${ADMIN}/app}"
REMOTE="${ADMIN}@${VM_HOST}"
COMPOSE_DIR="${APP_DIR}/deploy/vm"

command -v rsync >/dev/null || { echo "rsync missing." >&2; exit 1; }
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel)"
cd "$REPO_ROOT"

echo "== [1/5] Preflight: SSH to $REMOTE =="
ssh -o BatchMode=yes -o ConnectTimeout=10 "$REMOTE" 'echo ok >/dev/null' \
  || { echo "Cannot SSH to $REMOTE (key/NSG/host?)." >&2; exit 1; }

echo "== [2/5] Sync source -> $REMOTE:$APP_DIR =="
# --delete keeps the VM tree clean of removed files; excludes protect VM-local
# state (.env, backups) and skip build junk the image rebuilds anyway.
rsync -az --delete \
  --exclude '.git' \
  --exclude '**/bin' \
  --exclude '**/obj' \
  --exclude '**/node_modules' \
  --exclude '.localstorage' \
  --exclude 'TestResults' \
  --exclude 'deploy/vm/.env' \
  --exclude 'deploy/vm/backups' \
  ./ "${REMOTE}:${APP_DIR}/"

echo "== [3/5] Ensure .env + SQL are up =="
ssh "$REMOTE" "test -f ${COMPOSE_DIR}/.env" || {
  echo "ERROR: no .env on the VM at ${COMPOSE_DIR}/.env" >&2
  echo "First-time setup: ssh in, 'cp ${COMPOSE_DIR}/.env.example ${COMPOSE_DIR}/.env' and fill it." >&2
  exit 1
}
ssh "$REMOTE" "cd ${COMPOSE_DIR} && docker compose up -d mssql"

if [[ "$DO_SCHEMA" == "true" ]]; then
  echo "== [3b] Publishing dacpac schema =="
  : "${MSSQL_SA_PASSWORD:?--schema needs MSSQL_SA_PASSWORD (same as the VM .env)}"
  ADMIN_USER="$ADMIN" MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" "${SCRIPT_DIR}/publish-dacpac-vm.sh" "$VM_HOST"
fi

echo "== [4/5] Deploy app =="
if [[ "$DO_BUILD" == "true" ]]; then
  # Build happens ON the VM (the Dockerfile COPYs from the synced repo root).
  ssh "$REMOTE" "cd ${COMPOSE_DIR} && docker compose up -d --build webapp caddy"
else
  ssh "$REMOTE" "cd ${COMPOSE_DIR} && docker compose up -d webapp caddy"
fi
# Drop dangling images from previous builds so the VM disk doesn't creep.
ssh "$REMOTE" "docker image prune -f >/dev/null 2>&1 || true"

echo "== [5/5] Status =="
ssh "$REMOTE" "cd ${COMPOSE_DIR} && docker compose ps"

if [[ "$DO_LOGS" == "true" ]]; then
  echo "== Tailing logs (Ctrl-C to stop) =="
  ssh -t "$REMOTE" "cd ${COMPOSE_DIR} && docker compose logs -f --tail 80 webapp caddy"
fi

echo "== Done. https://capitalsemilla-dev.programasemilla.com =="
