#!/usr/bin/env bash
# Nightly SQL backup + app-storage archive. Run ON THE VM via cron.
# You own backups on a VM — Azure SQL's automatic backups are gone here.
#
# Install (on the VM):
#   chmod +x ~/deploy/backup.sh
#   ( crontab -l 2>/dev/null; echo "30 3 * * * ~/deploy/backup.sh >> ~/backup.log 2>&1" ) | crontab -
#
# Restore a .bak: copy it into the mssql container and RESTORE DATABASE.
set -euo pipefail

cd "$(dirname "$0")"
set -a; [[ -f .env ]] && . ./.env; set +a
: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD missing (source .env)}"

STAMP="$(date +%Y%m%d-%H%M%S)"
OUT="${BACKUP_DIR:-$HOME/backups}"
KEEP_DAYS="${BACKUP_KEEP_DAYS:-7}"
mkdir -p "$OUT"

echo "== [$STAMP] SQL backup =="
docker compose exec -T mssql /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "BACKUP DATABASE [fundingdb] TO DISK='/var/opt/mssql/fundingdb-${STAMP}.bak' WITH INIT, COMPRESSION"
docker compose cp "mssql:/var/opt/mssql/fundingdb-${STAMP}.bak" "$OUT/fundingdb-${STAMP}.bak"
docker compose exec -T mssql rm -f "/var/opt/mssql/fundingdb-${STAMP}.bak"

echo "== [$STAMP] app-storage archive =="
docker run --rm -v capitalsemilla_app_storage:/data -v "$OUT":/out alpine \
  tar czf "/out/app-storage-${STAMP}.tar.gz" -C /data .

echo "== Pruning backups older than ${KEEP_DAYS}d =="
find "$OUT" -type f \( -name '*.bak' -o -name '*.tar.gz' \) -mtime +"$KEEP_DAYS" -delete

echo "== Done. Files in $OUT =="
# Optional off-VM copy (cheap, durable): uncomment to push to Azure Blob.
#   az storage blob upload-batch -d backups -s "$OUT" --account-name <acct> --auth-mode login
