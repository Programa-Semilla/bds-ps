#!/usr/bin/env bash
# Show CPU / memory / per-container metrics for the VM deployment, with a verdict
# column. Run from your DEV MACHINE. Reads are pulled over SSH (no Azure Monitor
# agent, no Log Analytics cost — fits the VM path's zero-observability-cost design).
#
# Usage:
#   ./vm-metrics.sh snapshot          # one-shot table, then exit
#   ./vm-metrics.sh stream [secs]     # live, redraws every <secs> (default 3); Ctrl-C to stop
#
# Env:
#   RESOURCE_GROUP   Azure RG   (default: rg-CapitalSemilla-D)
#   VM_NAME          VM name    (default: vm-capitalsemilla-dev)
#   ADMIN_USER       SSH user   (default: azureuser)
set -euo pipefail

MODE="${1:-}"
case "$MODE" in
  snapshot) ;;
  stream)
    SECS="${2:-3}"
    [[ "$SECS" =~ ^[1-9][0-9]*$ ]] || { echo "stream interval must be a positive integer (seconds)" >&2; exit 1; }
    ;;
  *) echo "Usage: vm-metrics.sh {snapshot | stream [secs]}" >&2; exit 1 ;;
esac
SECS="${SECS:-3}"

RG="${RESOURCE_GROUP:-rg-CapitalSemilla-D}"
VM="${VM_NAME:-vm-capitalsemilla-dev}"
ADMIN="${ADMIN_USER:-azureuser}"

command -v az >/dev/null || { echo "az CLI missing." >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Not logged in. Run: az login" >&2; exit 1; }

IP="$(az vm show -d -g "$RG" -n "$VM" --query publicIps -o tsv)"
[[ -n "$IP" ]] || { echo "Could not resolve VM public IP." >&2; exit 1; }
REMOTE="${ADMIN}@${IP}"

# ---- Remote collector + renderer (runs on the VM). Sent base64-encoded so a
# ---- pseudo-tty (-tt, needed for clear/Ctrl-C in stream mode) is free to use,
# ---- and so we never fight shell quoting.
read -r -d '' PROG <<'REMOTE' || true
set -u
MODE="${1:-snapshot}"
SECS="${2:-3}"

row() { printf '%-26s %-36s %s\n' "$1" "$2" "$3"; }
bar() { printf '%.0s-' $(seq 1 76); printf '\n'; }

render() {
  local cores l1 l5 l15 cpu_v
  cores="$(nproc)"
  read -r l1 l5 l15 _ < /proc/loadavg
  cpu_v="$(awk -v l="$l1" -v c="$cores" 'BEGIN{r=l/c; if(r<0.7)print"idle";else if(r<=1.0)print"moderate";else print"PRESSURE"}')"

  local m_total m_used m_avail mem_v
  read -r m_total m_used m_avail < <(free -m | awk '/^Mem:/{print $2, $3, $7}')
  mem_v="$(awk -v a="$m_avail" -v t="$m_total" 'BEGIN{p=a/t*100; if(p>40)print"healthy";else if(p>=20)print"tight";else print"CRITICAL"}')"

  local s_total s_used swap_v
  read -r s_total s_used < <(free -m | awk '/^Swap:/{print $2, $3}')
  if   [ "$s_used" -eq 0 ];   then swap_v="untouched"
  elif [ "$s_used" -le 256 ]; then swap_v="in-use"
  else swap_v="HEAVY"; fi

  row "METRIC" "READING" "VERDICT"
  bar
  row "CPU" "load ${l1} / ${l5} / ${l15} (${cores} cores)" "$cpu_v"
  row "RAM" "${m_used}/${m_total}Mi used (${m_avail}Mi avail)" "$mem_v"
  row "Swap" "${s_used}/${s_total}Mi used" "$swap_v"
  bar

  docker stats --no-stream --format '{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}|{{.MemPerc}}' 2>/dev/null \
  | while IFS='|' read -r name cpu memu memp; do
      local cv pnum mem_used
      pnum="${memp%\%}"
      mem_used="${memu%% / *}"
      cv="$(awk -v p="$pnum" 'BEGIN{if(p+0<70)print"ok";else print"HIGH"}')"
      row "$name" "${cpu} cpu / ${mem_used} (${memp})" "$cv"
    done
}

if [ "$MODE" = "stream" ]; then
  # INT = typed Ctrl-C via the pty; TERM/HUP = ssh connection dropped. The
  # [ -t 1 ] guard is a backstop: the pty vanishes on disconnect, ending the
  # loop even if no signal lands — so the loop can never orphan on the VM.
  trap 'exit 0' INT TERM HUP
  while [ -t 1 ]; do
    clear
    # Actual cadence ≈ SECS + ~2-3s, since `docker stats` samples CPU over an interval.
    printf 'VM metrics — %s   (every ~%ss + sampling · Ctrl-C to stop)\n\n' "$(date '+%H:%M:%S')" "$SECS"
    render
    sleep "$SECS" & wait $!
  done
else
  render
fi
REMOTE

B64="$(printf '%s' "$PROG" | base64 | tr -d '\n')"

if [[ "$MODE" == "stream" ]]; then
  ssh -tt "$REMOTE" "echo $B64 | base64 -d | bash -s -- stream $SECS"
else
  ssh "$REMOTE" "echo $B64 | base64 -d | bash -s -- snapshot"
fi
