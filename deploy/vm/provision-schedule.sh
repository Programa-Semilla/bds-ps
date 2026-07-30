#!/usr/bin/env bash
# Power schedule + manual control for the VM. Run from a machine with `az`.
#
# Saves money by deallocating the VM off-hours (compute billing stops while
# stopped; disk + static IP still bill ~$8/mo). Auto-start each weekday morning
# via an Azure Automation runbook; auto-stop every evening via DevTest schedule.
#
# Usage:
#   ./provision-schedule.sh provision   # create/refresh the auto start+stop schedules (idempotent)
#   ./provision-schedule.sh start       # start the VM now (manual; e.g. after-hours testing)
#   ./provision-schedule.sh stop        # deallocate the VM now (stops compute billing)
#   ./provision-schedule.sh status      # show power state
#   ./provision-schedule.sh disable     # turn both schedules off (stays whatever it is now)
#   ./provision-schedule.sh enable      # turn both schedules back on
#
# Configurable via env (defaults match the live dev VM):
#   SUBSCRIPTION RESOURCE_GROUP VM_NAME LOCATION AUTOMATION_ACCOUNT RUNBOOK_NAME SCHEDULE_NAME
#   START_TIME(0645) STOP_TIME(1900) TIMEZONE_IANA(America/Costa_Rica)
#   TIMEZONE_WINDOWS("Central America Standard Time") UTC_OFFSET(-06:00)
#   WEEKDAYS(Monday,Tuesday,Wednesday,Thursday,Friday)
set -euo pipefail

SUB="${SUBSCRIPTION:-d428f98f-a3c4-49c3-ae24-06ec3de08477}"  # LinaSys-DevEnv
RG="${RESOURCE_GROUP:-rg-CapitalSemilla-D}"
VM="${VM_NAME:-vm-capitalsemilla-dev}"
LOC="${LOCATION:-centralus}"
AA="${AUTOMATION_ACCOUNT:-aa-capitalsemilla}"
RB="${RUNBOOK_NAME:-StartCapitalSemillaVM}"
SCHED="${SCHEDULE_NAME:-weekday-0645-cr}"
START_HHMM="${START_TIME:-0645}"
STOP_HHMM="${STOP_TIME:-1900}"
TZ_IANA="${TIMEZONE_IANA:-America/Costa_Rica}"
TZ_WIN="${TIMEZONE_WINDOWS:-Central America Standard Time}"
UTC_OFFSET="${UTC_OFFSET:--06:00}"
WEEKDAYS="${WEEKDAYS:-Monday,Tuesday,Wednesday,Thursday,Friday}"
API="2023-11-01"

command -v az >/dev/null || { echo "az CLI missing." >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Not logged in. Run: az login" >&2; exit 1; }

# Pin every az call to $SUB instead of inheriting the CLI's default subscription.
# The VM lives in LinaSys-DevEnv, which is often not the active default; without
# this, every command fails as a misleading "ResourceGroupNotFound", and
# `provision` could otherwise create resources in the wrong subscription.
az() { command az "$@" --subscription "$SUB"; }
az account show -o none 2>/dev/null || {
  echo "Subscription '$SUB' not accessible. Run 'az account list -o table' and set SUBSCRIPTION=<id>." >&2
  exit 1
}

vm_id() { az vm show -g "$RG" -n "$VM" --query id -o tsv; }
aa_id() { az automation account show -g "$RG" -n "$AA" --query id -o tsv; }

power_status() {
  az vm get-instance-view -g "$RG" -n "$VM" \
    --query "instanceView.statuses[?starts_with(code,'PowerState')].displayStatus | [0]" -o tsv 2>/dev/null
}

set_schedule_status() { # $1 = Enabled|Disabled
  local state="$1"
  # DevTest auto-shutdown (stop)
  az resource update -g "$RG" --resource-type "Microsoft.DevTestLab/schedules" \
    -n "shutdown-computevm-${VM}" --set "properties.status=${state}" -o none 2>/dev/null || true
  # Automation start schedule
  local AAID; AAID="$(aa_id)"
  az rest --method patch \
    --uri "https://management.azure.com${AAID}/schedules/${SCHED}?api-version=${API}" \
    --body "{\"properties\":{\"isEnabled\":$([ "$state" = Enabled ] && echo true || echo false)}}" -o none 2>/dev/null || true
}

provision() {
  local VMID AAID PID
  VMID="$(vm_id)"
  echo "== [1/6] Auto-stop: DevTest deallocate @ ${STOP_HHMM} ${TZ_WIN} =="
  az resource create -g "$RG" -n "shutdown-computevm-${VM}" \
    --resource-type "Microsoft.DevTestLab/schedules" --location "$LOC" \
    --properties "{\"status\":\"Enabled\",\"taskType\":\"ComputeVmShutdownTask\",\"dailyRecurrence\":{\"time\":\"${STOP_HHMM}\"},\"timeZoneId\":\"${TZ_WIN}\",\"targetResourceId\":\"${VMID}\",\"notificationSettings\":{\"status\":\"Disabled\",\"timeInMinutes\":30}}" \
    -o none

  echo "== [2/6] Automation account + system identity =="
  az automation account show -g "$RG" -n "$AA" -o none 2>/dev/null \
    || az automation account create -g "$RG" -n "$AA" --location "$LOC" --sku Free -o none
  AAID="$(aa_id)"
  az resource update --ids "$AAID" --set identity.type=SystemAssigned -o none
  PID="$(az resource show --ids "$AAID" --query identity.principalId -o tsv)"

  echo "== [3/6] Grant the identity rights to start the VM =="
  az role assignment create --assignee-object-id "$PID" --assignee-principal-type ServicePrincipal \
    --role "Virtual Machine Contributor" --scope "$VMID" -o none 2>/dev/null || true

  echo "== [4/6] Runbook (dependency-free REST start) =="
  local TMP; TMP="$(mktemp)"
  cat > "$TMP" <<PSEOF
\$ErrorActionPreference = "Stop"
\$resourceURI = "https://management.azure.com/"
\$tokenUri = "\$(\$env:IDENTITY_ENDPOINT)?resource=\$resourceURI&api-version=2019-08-01"
\$token = (Invoke-RestMethod -Method GET -Uri \$tokenUri -Headers @{ "X-IDENTITY-HEADER"=\$env:IDENTITY_HEADER; "Metadata"="true" }).access_token
\$vmId = "${VMID}"
\$startUri = "https://management.azure.com\$vmId/start?api-version=2023-07-01"
Invoke-RestMethod -Method POST -Uri \$startUri -Headers @{ Authorization = "Bearer \$token" } | Out-Null
Write-Output "Start requested for \$vmId at \$(Get-Date -Format o)"
PSEOF
  az automation runbook show -g "$RG" --automation-account-name "$AA" -n "$RB" -o none 2>/dev/null \
    || az automation runbook create -g "$RG" --automation-account-name "$AA" -n "$RB" --type PowerShell --location "$LOC" -o none
  az automation runbook replace-content -g "$RG" --automation-account-name "$AA" -n "$RB" --content @"$TMP" -o none
  az automation runbook publish -g "$RG" --automation-account-name "$AA" -n "$RB" -o none
  rm -f "$TMP"

  echo "== [5/6] Weekly schedule @ ${START_HHMM} ${TZ_IANA} (${WEEKDAYS}) =="
  local HH="${START_HHMM:0:2}" MM="${START_HHMM:2:2}"
  local D; D="$(TZ="$TZ_IANA" date -d "tomorrow" +%Y-%m-%d)"
  local START_ISO="${D}T${HH}:${MM}:00${UTC_OFFSET}"
  local DAYS_JSON; DAYS_JSON="$(python3 -c "import sys,json;print(json.dumps([d.strip() for d in sys.argv[1].split(',')]))" "$WEEKDAYS")"
  # Recreate so re-runs are idempotent (Automation schedules are largely immutable).
  az rest --method delete --uri "https://management.azure.com${AAID}/schedules/${SCHED}?api-version=${API}" -o none 2>/dev/null || true
  az rest --method put --uri "https://management.azure.com${AAID}/schedules/${SCHED}?api-version=${API}" \
    --body "{\"properties\":{\"description\":\"Start VM weekday mornings\",\"startTime\":\"${START_ISO}\",\"frequency\":\"Week\",\"interval\":1,\"timeZone\":\"${TZ_IANA}\",\"advancedSchedule\":{\"weekDays\":${DAYS_JSON}}}}" -o none

  echo "== [6/6] Link runbook -> schedule =="
  local JS; JS="$(python3 -c "import uuid;print(uuid.uuid5(uuid.NAMESPACE_DNS,'${AA}/${RB}/${SCHED}'))")"
  az rest --method put --uri "https://management.azure.com${AAID}/jobSchedules/${JS}?api-version=${API}" \
    --body "{\"properties\":{\"schedule\":{\"name\":\"${SCHED}\"},\"runbook\":{\"name\":\"${RB}\"}}}" -o none

  echo
  echo "Done. Auto-start ${START_HHMM} (${WEEKDAYS}), auto-stop ${STOP_HHMM} daily, ${TZ_IANA}."
  echo "Manual: ./provision-schedule.sh start|stop|status"
}

case "${1:-}" in
  provision) provision ;;
  start)  echo "Starting $VM..."; az vm start -g "$RG" -n "$VM" -o none && echo "started: $(power_status)" ;;
  stop)   echo "Deallocating $VM..."; az vm deallocate -g "$RG" -n "$VM" -o none && echo "stopped: $(power_status)" ;;
  status) echo "$VM: $(power_status)" ;;
  disable) set_schedule_status Disabled; echo "schedules disabled" ;;
  enable)  set_schedule_status Enabled;  echo "schedules enabled" ;;
  *) echo "Usage: $0 {provision|start|stop|status|disable|enable}" >&2; exit 2 ;;
esac
