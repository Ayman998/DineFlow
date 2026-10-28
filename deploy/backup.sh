#!/bin/sh
# Backs up the DineFlow database and the API's data volume (photos and Data Protection keys) into
# ./backups, and keeps the last 14 days. Run from this folder, e.g. nightly from cron:
#   0 3 * * * cd /opt/dineflow/deploy && ./backup.sh >> backups/backup.log 2>&1
# Copy ./backups off the server too (rclone, restic, your provider's object storage): a backup on
# the same disk doesn't survive losing the server.
set -eu

stamp=$(date +%Y%m%d-%H%M%S)
mkdir -p backups

# SQL Server writes the backup inside its own volume; then it's copied out and removed there.
docker compose exec -T db sh -c "mkdir -p /var/opt/mssql/backup && /opt/mssql-tools18/bin/sqlcmd -C -b -S localhost -U sa -P \"\$MSSQL_SA_PASSWORD\" \
  -Q \"BACKUP DATABASE [DineFlow] TO DISK = N'/var/opt/mssql/backup/DineFlow-$stamp.bak' WITH CHECKSUM, INIT\""
docker compose cp "db:/var/opt/mssql/backup/DineFlow-$stamp.bak" "backups/DineFlow-$stamp.bak"
docker compose exec -T db rm "/var/opt/mssql/backup/DineFlow-$stamp.bak"

# Without the keys, POS access tokens stored in the database can't be decrypted after a restore.
docker run --rm -v dineflow_api-data:/data:ro -v "$(pwd)/backups:/backup" alpine \
  tar czf "/backup/app-data-$stamp.tgz" -C /data .

find backups -name 'DineFlow-*.bak' -mtime +14 -delete
find backups -name 'app-data-*.tgz' -mtime +14 -delete
echo "$(date -Is) backed up DineFlow-$stamp.bak and app-data-$stamp.tgz"
