#!/usr/bin/env bash
# Stream / tail container logs for the VM deployment, from your DEV MACHINE.
# Wraps `docker compose logs` over SSH (no agent, no Log Analytics cost).
#
# Usage:
#   ./vm-logs.sh follow [service...]      # live -f stream (Ctrl-C to stop)
#   ./vm-logs.sh tail [N] [service...]    # last N lines (default 200), then exit
#
# Services default to "webapp" when none are given. Valid: webapp caddy mssql
# (whatever docker-compose.yml defines). Examples:
#   ./vm-logs.sh follow                   # live webapp logs
#   ./vm-logs.sh follow caddy             # live caddy logs
#   ./vm-logs.sh follow webapp caddy      # both, multiplexed + prefixed
#   ./vm-logs.sh tail 500 mssql           # last 500 lines of SQL Server
#
# Env:
#   RESOURCE_GROUP   Azure RG   (default: rg-CapitalSemilla-D)
#   VM_NAME          VM name    (default: vm-capitalsemilla-dev)
#   ADMIN_USER       SSH user   (default: azureuser)
#   APP_DIR          Repo dir on the VM (default: /home/<admin>/app)
set -euo pipefail

MODE="${1:-}"
shift || true
case "$MODE" in
  follow) ;;
  tail)
    N=200
    if [[ "${1:-}" =~ ^[1-9][0-9]*$ ]]; then N="$1"; shift; fi
    ;;
  *) echo "Usage: vm-logs.sh {follow [service...] | tail [N] [service...]}" >&2; exit 1 ;;
esac

# Remaining args are service names. Validate to keep the remote command injection-safe.
SERVICES=("$@")
[[ ${#SERVICES[@]} -eq 0 ]] && SERVICES=("webapp")
for s in "${SERVICES[@]}"; do
  [[ "$s" =~ ^[a-zA-Z0-9_-]+$ ]] || { echo "invalid service name: $s" >&2; exit 1; }
done
SVC="${SERVICES[*]}"

RG="${RESOURCE_GROUP:-rg-CapitalSemilla-D}"
VM="${VM_NAME:-vm-capitalsemilla-dev}"
ADMIN="${ADMIN_USER:-azureuser}"
APP_DIR="${APP_DIR:-/home/${ADMIN}/app}"
COMPOSE_DIR="${APP_DIR}/deploy/vm"

command -v az >/dev/null || { echo "az CLI missing." >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Not logged in. Run: az login" >&2; exit 1; }

IP="$(az vm show -d -g "$RG" -n "$VM" --query publicIps -o tsv)"
[[ -n "$IP" ]] || { echo "Could not resolve VM public IP." >&2; exit 1; }
REMOTE="${ADMIN}@${IP}"

if [[ "$MODE" == "follow" ]]; then
  echo "== Following logs [$SVC] on $IP — Ctrl-C to stop ==" >&2
  # -tt: a pty so colors render, a typed Ctrl-C reaches docker, AND a dropped
  # connection hangs up the remote `logs -f` instead of orphaning it.
  ssh -tt "$REMOTE" "cd ${COMPOSE_DIR} && docker compose logs -f --tail 50 ${SVC}"
else
  ssh "$REMOTE" "cd ${COMPOSE_DIR} && docker compose logs --tail ${N} ${SVC}"
fi
