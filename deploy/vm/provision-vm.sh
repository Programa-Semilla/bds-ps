#!/usr/bin/env bash
# Provision the lowest-cost fixed-price VM for the capitalsemilla-dev deployment.
#
# Cost target (centralus, approx/month, FIXED regardless of traffic):
#   Standard_B2als_v2 (2 vCPU, 4 GB)  ~$31-38
#   64 GB StandardSSD OS disk         ~$5
#   Standard static public IP         ~$3-4
#   -------------------------------------
#   ~$40/month, predictable. No per-request, no autoscale surprise.
#
# 4 GB runs webapp + SQL + Caddy but is tight once Syncfusion launches Chromium
# for a PDF. If you see OOM kills, bump SIZE to Standard_B2as_v2 (8 GB, ~$65).
#
# DO NOT put this back on Standard_B2s / B2ms. The v1-v4 series (which includes
# B/Bs) are under Azure capacity growth restrictions from 2026-07-31 -- new
# deployments, scale-out and quota increases for them are not approved -- and
# retire 2028-07-31. So a green-field re-provision on B2s can simply be refused,
# which is exactly the path this script exists to serve. The v2 B-series is on
# neither list, and the preflight below fails fast if that ever changes.
set -euo pipefail

SUB="${SUBSCRIPTION:-d428f98f-a3c4-49c3-ae24-06ec3de08477}"  # LinaSys-DevEnv
RG="${RESOURCE_GROUP:-rg-CapitalSemilla-D}"
LOC="${LOCATION:-centralus}"
VM="${VM_NAME:-vm-capitalsemilla-dev}"
SIZE="${VM_SIZE:-Standard_B2als_v2}"
ADMIN="${ADMIN_USER:-azureuser}"
DISK_GB="${OS_DISK_GB:-64}"

command -v az >/dev/null || { echo "az CLI missing." >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Not logged in. Run: az login" >&2; exit 1; }

# Pin every az call to $SUB instead of inheriting the CLI's default subscription.
# Without this, a wrong default silently provisions the whole stack into the
# wrong subscription (or fails as a misleading "ResourceGroupNotFound").
az() { command az "$@" --subscription "$SUB"; }
az account show -o none 2>/dev/null || {
  echo "Subscription '$SUB' not accessible. Run 'az account list -o table' and set SUBSCRIPTION=<id>." >&2
  exit 1
}

# Public IP to lock the SSH NSG rule to. Override with MYIP=... ; otherwise try
# several echo services (some networks block any single one).
MYIP="${MYIP:-}"
for _svc in https://api.ipify.org https://checkip.amazonaws.com https://ifconfig.me; do
  [[ -n "$MYIP" ]] && break
  MYIP="$(curl -fsS --max-time 8 "$_svc" 2>/dev/null | tr -d '[:space:]')"
done
[[ -n "$MYIP" ]] || { echo "Could not determine public IP. Re-run with MYIP=<your.ip>" >&2; exit 1; }
# Preflight the VM size. Since the 2026-07-31 growth restrictions on the v1-v4
# series, an unavailable/restricted SKU is a real outcome, and `az vm create`
# reports it as a late, cryptic SkuNotAvailable. Fail here instead, with the fix.
echo "== Checking $SIZE is offered and unrestricted in $LOC =="
SKU_JSON="$(az vm list-skus -l "$LOC" --resource-type virtualMachines --size "$SIZE" \
  --query "[?name=='$SIZE'] | [0]" -o json 2>/dev/null)"
if [[ -z "$SKU_JSON" || "$SKU_JSON" == "null" ]]; then
  echo "VM size '$SIZE' is not offered in $LOC for this subscription." >&2
  echo "List candidates: az vm list-skus -l $LOC --resource-type virtualMachines --query \"[?starts_with(name,'Standard_B2')].name\" -o tsv" >&2
  exit 1
fi
if [[ "$SKU_JSON" == *'"reasonCode"'* ]]; then
  echo "VM size '$SIZE' is RESTRICTED in $LOC for this subscription:" >&2
  printf '%s\n' "$SKU_JSON" | grep -A2 '"reasonCode"' >&2 || true
  echo "Pick an unrestricted size (v2+ B-series) via VM_SIZE=... and re-run." >&2
  exit 1
fi

echo "== Provisioning $VM ($SIZE) in $RG / $LOC =="
echo "   SSH will be locked to your current IP: $MYIP"

# az vm create does NOT create the resource group. Make it idempotently so this
# works green-field (e.g. after the RG was deleted to stop billing).
echo "== Ensuring resource group $RG exists in $LOC =="
az group create -n "$RG" -l "$LOC" -o none

az vm create \
  -g "$RG" -n "$VM" -l "$LOC" \
  --image Ubuntu2404 \
  --size "$SIZE" \
  --admin-username "$ADMIN" \
  --generate-ssh-keys \
  --public-ip-sku Standard \
  --os-disk-size-gb "$DISK_GB" \
  --storage-sku StandardSSD_LRS \
  --custom-data cloud-init.yaml \
  --nsg-rule NONE

# az vm create makes an NSG named "${VM}NSG". Open web to the world, SSH to you only.
az network nsg rule create -g "$RG" --nsg-name "${VM}NSG" -n allow-web \
  --priority 1000 --direction Inbound --access Allow --protocol Tcp \
  --destination-port-ranges 80 443 >/dev/null
az network nsg rule create -g "$RG" --nsg-name "${VM}NSG" -n allow-ssh \
  --priority 1100 --direction Inbound --access Allow --protocol Tcp \
  --source-address-prefixes "${MYIP}/32" --destination-port-ranges 22 >/dev/null

IP="$(az vm show -d -g "$RG" -n "$VM" --query publicIps -o tsv)"
cat <<EOF

== Done ==
VM public IP: $IP

Next:
  1. DNS: point an A record
        capitalsemilla-dev.programasemilla.com  ->  $IP
     (Caddy needs this resolvable before it can issue the TLS cert.)
  2. Copy deploy files to the VM:
        scp -r ../vm $ADMIN@$IP:~/deploy
  3. SSH in, configure secrets, bring up DB:
        ssh $ADMIN@$IP
        cd ~/deploy && cp .env.example .env && nano .env
        docker compose up -d mssql
  4. From your dev machine, publish the schema:
        ./publish-dacpac-vm.sh $IP
  5. Back on the VM, start the rest:
        docker compose up -d --build
EOF
