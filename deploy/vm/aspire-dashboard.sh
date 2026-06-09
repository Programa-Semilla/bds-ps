#!/usr/bin/env bash
# Turn the Aspire Dashboard on/off for the VM deployment, from your DEV MACHINE.
#
# The dashboard is in-memory telemetry (logs/traces/metrics), off by default to
# save RAM. It binds to the VM's loopback only and runs AUTHMODE=Unsecured, so
# the only safe way to reach it is over an SSH tunnel — which this script opens
# for you and leaves running in the background.
#
# Usage:
#   ./aspire-dashboard.sh on        # toggle export on, start it, open the tunnel, print URL
#   ./aspire-dashboard.sh off       # close the tunnel, stop it, toggle export off
#   ./aspire-dashboard.sh status    # report tunnel / container / export state
#
# Env:
#   RESOURCE_GROUP   Azure RG   (default: rg-CapitalSemilla-D)
#   VM_NAME          VM name    (default: vm-capitalsemilla-dev)
#   ADMIN_USER       SSH user   (default: azureuser)
#   APP_DIR          Repo dir on the VM (default: /home/<admin>/app)
#
# Note: `on`/`off` restart the webapp so it (re)reads OTEL_ENDPOINT — a few
# seconds of downtime on the live dev site each toggle.
set -euo pipefail

ACTION="${1:-}"
case "$ACTION" in
  on|off|status) ;;
  *) echo "Usage: aspire-dashboard.sh {on|off|status}" >&2; exit 1 ;;
esac

RG="${RESOURCE_GROUP:-rg-CapitalSemilla-D}"
VM="${VM_NAME:-vm-capitalsemilla-dev}"
ADMIN="${ADMIN_USER:-azureuser}"
APP_DIR="${APP_DIR:-/home/${ADMIN}/app}"
COMPOSE_DIR="${APP_DIR}/deploy/vm"
LOCAL_PORT=18888
OTEL_LINE='OTEL_ENDPOINT=http://aspire-dashboard:18889'
TUNNEL_PATTERN="ssh -f -N -L ${LOCAL_PORT}:localhost:${LOCAL_PORT}"

command -v az >/dev/null || { echo "az CLI missing." >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Not logged in. Run: az login" >&2; exit 1; }

echo "== Resolving public IP for $VM in $RG =="
IP="$(az vm show -d -g "$RG" -n "$VM" --query publicIps -o tsv)"
[[ -n "$IP" ]] || { echo "Could not resolve VM public IP." >&2; exit 1; }
REMOTE="${ADMIN}@${IP}"

tunnel_pid() { pgrep -f "$TUNNEL_PATTERN" || true; }

case "$ACTION" in
  on)
    echo "== Enabling OTEL export + starting dashboard on $IP =="
    # Toggle the comment marker only, preserving any customized endpoint value.
    # Append the default line if .env has no OTEL_ENDPOINT entry at all.
    ssh "$REMOTE" "cd ${COMPOSE_DIR} \
      && (grep -q '^# *OTEL_ENDPOINT=' .env && sed -i 's|^# *OTEL_ENDPOINT=|OTEL_ENDPOINT=|' .env || true) \
      && (grep -q '^OTEL_ENDPOINT=' .env || echo '${OTEL_LINE}' >> .env) \
      && docker compose up -d webapp \
      && docker compose --profile debug up -d aspire-dashboard"

    if [[ -n "$(tunnel_pid)" ]]; then
      echo "== Tunnel already open on localhost:${LOCAL_PORT} =="
    else
      echo "== Opening SSH tunnel localhost:${LOCAL_PORT} -> ${IP}:${LOCAL_PORT} =="
      ssh -f -N -L "${LOCAL_PORT}:localhost:${LOCAL_PORT}" "$REMOTE"
    fi

    for _ in $(seq 1 10); do (echo > "/dev/tcp/127.0.0.1/${LOCAL_PORT}") 2>/dev/null && break; sleep 1; done
    echo
    echo "Dashboard ready -> http://localhost:${LOCAL_PORT}"
    echo "Turn it off with: $(basename "$0") off"
    ;;

  off)
    PID="$(tunnel_pid)"
    if [[ -n "$PID" ]]; then
      echo "== Closing SSH tunnel (pid $PID) =="
      kill $PID 2>/dev/null || true
    else
      echo "== No local tunnel running =="
    fi
    echo "== Stopping dashboard + disabling OTEL export on $IP =="
    ssh "$REMOTE" "cd ${COMPOSE_DIR} \
      && docker compose stop aspire-dashboard \
      && sed -i 's|^OTEL_ENDPOINT=|# OTEL_ENDPOINT=|' .env \
      && docker compose up -d webapp"
    echo "Dashboard off."
    ;;

  status)
    PID="$(tunnel_pid)"
    [[ -n "$PID" ]] && echo "tunnel:    UP   (localhost:${LOCAL_PORT})" || echo "tunnel:    down"
    # One round-trip; tolerate SSH failure so status never hard-fails.
    REMOTE_STATE="$(ssh -o BatchMode=yes -o ConnectTimeout=10 "$REMOTE" "cd ${COMPOSE_DIR} \
      && { docker compose ps --status running --services 2>/dev/null | grep -qx aspire-dashboard && echo UP || echo down; } \
      && { grep -q '^OTEL_ENDPOINT=' .env && echo on || echo off; }" 2>/dev/null)" || REMOTE_STATE=""
    if [[ -z "$REMOTE_STATE" ]]; then
      echo "container: unreachable (SSH failed — check key/${REMOTE})"
      echo "export:    unreachable"
    else
      CONTAINER="$(printf '%s\n' "$REMOTE_STATE" | sed -n '1p')"
      EXPORT="$(printf '%s\n' "$REMOTE_STATE" | sed -n '2p')"
      echo "container: ${CONTAINER}"
      echo "export:    ${EXPORT}"
      if [[ -n "$PID" && "$CONTAINER" == "UP" ]]; then echo "open -> http://localhost:${LOCAL_PORT}"; fi
    fi
    ;;
esac
