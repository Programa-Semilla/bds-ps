#!/usr/bin/env bash
# Publish FundingPlatform.Database dacpac to Azure SQL.
# Root cause: AppHost.cs publish-mode branch does not register SqlProject,
# so `azd up` / `azd deploy` never carries schema. Run this after each
# release until AppHost.cs is patched.
#
# NON-PRODUCTION env: defaults are destructive — allows data loss and drops
# objects not in source. Override with --safe for prod-shaped targets.

set -euo pipefail

# --- Defaults (override via env or flags) ---
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-CapitalSemilla-D}"
SQL_SERVER="${SQL_SERVER:-sqlserver-negelwcexrtzc}"
SQL_DATABASE="${SQL_DATABASE:-fundingdb}"
AAD_USER="${AAD_USER:-danny.perez.u@gmail.com}"
DACPAC_CONFIG="${DACPAC_CONFIG:-Release}"
BLOCK_DATA_LOSS="${BLOCK_DATA_LOSS:-false}"
DROP_OBJECTS_NOT_IN_SOURCE="${DROP_OBJECTS_NOT_IN_SOURCE:-true}"
SKIP_BUILD="${SKIP_BUILD:-false}"
SKIP_FIREWALL="${SKIP_FIREWALL:-false}"

usage() {
  cat <<EOF
Usage: $0 [options]

Options:
  -g <rg>         Resource group (default: $RESOURCE_GROUP)
  -s <server>     Azure SQL server name, no FQDN (default: $SQL_SERVER)
  -d <db>         Database name (default: $SQL_DATABASE)
  -u <user>       AAD user (default: $AAD_USER)
  -c <config>     dacpac build config: Release|AzureRelease (default: $DACPAC_CONFIG)
  --skip-build    Reuse existing dacpac, do not rebuild
  --skip-firewall Skip transient firewall rule
  --safe          Prod-shaped: block data loss, keep extra objects
  --no-drop       Do not drop objects missing from source (keep them)
  -h              Show this help

Defaults: BlockOnPossibleDataLoss=false, DropObjectsNotInSource=true (dev).

Env overrides: RESOURCE_GROUP, SQL_SERVER, SQL_DATABASE, AAD_USER,
DACPAC_CONFIG, BLOCK_DATA_LOSS, DROP_OBJECTS_NOT_IN_SOURCE,
SKIP_BUILD, SKIP_FIREWALL.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -g) RESOURCE_GROUP="$2"; shift 2 ;;
    -s) SQL_SERVER="$2"; shift 2 ;;
    -d) SQL_DATABASE="$2"; shift 2 ;;
    -u) AAD_USER="$2"; shift 2 ;;
    -c) DACPAC_CONFIG="$2"; shift 2 ;;
    --skip-build) SKIP_BUILD="true"; shift ;;
    --skip-firewall) SKIP_FIREWALL="true"; shift ;;
    --safe) BLOCK_DATA_LOSS="true"; DROP_OBJECTS_NOT_IN_SOURCE="false"; shift ;;
    --no-drop) DROP_OBJECTS_NOT_IN_SOURCE="false"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown arg: $1" >&2; usage; exit 2 ;;
  esac
done

REPO_ROOT="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$REPO_ROOT"

DB_PROJECT="src/FundingPlatform.Database/FundingPlatform.Database.sqlproj"
DACPAC_PATH="src/FundingPlatform.Database/bin/${DACPAC_CONFIG}/FundingPlatform.Database.dacpac"
SERVER_FQDN="${SQL_SERVER}.database.windows.net"

# --- Preflight ---
command -v sqlpackage >/dev/null || { echo "sqlpackage missing. Install: dotnet tool install -g microsoft.sqlpackage" >&2; exit 1; }
command -v az >/dev/null || { echo "az CLI missing." >&2; exit 1; }
az account show >/dev/null 2>&1 || { echo "Not logged in. Run: az login" >&2; exit 1; }

echo "== Target =="
echo "  Server:   $SERVER_FQDN"
echo "  Database: $SQL_DATABASE"
echo "  User:     $AAD_USER"
echo "  Config:   $DACPAC_CONFIG"
echo "  BlockOnPossibleDataLoss:  $BLOCK_DATA_LOSS"
echo "  DropObjectsNotInSource:   $DROP_OBJECTS_NOT_IN_SOURCE"
if [[ "$BLOCK_DATA_LOSS" == "false" ]]; then
  echo "  *** Destructive deploy — data loss + dropped objects allowed ***"
fi
echo

# --- 1. Build dacpac ---
if [[ "$SKIP_BUILD" == "true" ]]; then
  echo "[1/3] Skipping build (--skip-build)"
  [[ -f "$DACPAC_PATH" ]] || { echo "Missing dacpac: $DACPAC_PATH" >&2; exit 1; }
else
  echo "[1/3] Building dacpac ($DACPAC_CONFIG)..."
  dotnet build "$DB_PROJECT" -c "$DACPAC_CONFIG" --nologo -v quiet
fi
echo "  dacpac: $DACPAC_PATH"
echo

# --- 2. Firewall rule (transient) ---
FIREWALL_RULE=""
cleanup_firewall() {
  if [[ -n "$FIREWALL_RULE" ]]; then
    echo "  Removing firewall rule: $FIREWALL_RULE"
    az sql server firewall-rule delete \
      -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n "$FIREWALL_RULE" \
      --only-show-errors 2>/dev/null || true
  fi
}
trap cleanup_firewall EXIT

if [[ "$SKIP_FIREWALL" == "true" ]]; then
  echo "[2/3] Skipping firewall rule (--skip-firewall)"
else
  MYIP="$(curl -fsS https://api.ipify.org)"
  [[ -n "$MYIP" ]] || { echo "Could not detect public IP." >&2; exit 1; }
  FIREWALL_RULE="cli-$(whoami)-$(date +%s)"
  echo "[2/3] Adding firewall rule $FIREWALL_RULE for $MYIP..."
  az sql server firewall-rule create \
    -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n "$FIREWALL_RULE" \
    --start-ip-address "$MYIP" --end-ip-address "$MYIP" \
    --only-show-errors >/dev/null
fi
echo

# --- 3. Publish ---
# Prefer access token from `az` (works in non-interactive shells / CI).
# Falls back to UniversalAuthentication when no logged-in az session.
ACCESS_TOKEN=""
if az account show >/dev/null 2>&1; then
  ACCESS_TOKEN="$(az account get-access-token \
    --resource https://database.windows.net/ \
    --query accessToken -o tsv 2>/dev/null || true)"
fi

if [[ -n "$ACCESS_TOKEN" ]]; then
  echo "[3/3] Publishing dacpac (access token from az)..."
  sqlpackage /Action:Publish \
    /SourceFile:"$DACPAC_PATH" \
    /TargetServerName:"$SERVER_FQDN" \
    /TargetDatabaseName:"$SQL_DATABASE" \
    /AccessToken:"$ACCESS_TOKEN" \
    /p:BlockOnPossibleDataLoss="$BLOCK_DATA_LOSS" \
    /p:DropObjectsNotInSource="$DROP_OBJECTS_NOT_IN_SOURCE" \
    /p:AllowIncompatiblePlatform=true \
    /p:GenerateSmartDefaults=true \
    /p:IncludeTransactionalScripts=true
else
  echo "[3/3] Publishing dacpac (AAD interactive — may open browser)..."
  sqlpackage /Action:Publish \
    /SourceFile:"$DACPAC_PATH" \
    /TargetServerName:"$SERVER_FQDN" \
    /TargetDatabaseName:"$SQL_DATABASE" \
    /TargetUser:"$AAD_USER" \
    /UniversalAuthentication:True \
    /p:BlockOnPossibleDataLoss="$BLOCK_DATA_LOSS" \
    /p:DropObjectsNotInSource="$DROP_OBJECTS_NOT_IN_SOURCE" \
    /p:AllowIncompatiblePlatform=true \
    /p:GenerateSmartDefaults=true \
    /p:IncludeTransactionalScripts=true
fi

echo
echo "Done. Schema deployed to $SERVER_FQDN/$SQL_DATABASE."
