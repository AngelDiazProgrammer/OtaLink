#!/usr/bin/env bash
# Copia de seguridad lógica de la base de OtaLink (formato custom de pg_dump).
# Se ejecuta como el usuario "postgres" (autenticación peer por socket local).
# Instalar en: /usr/local/sbin/otalink-db-backup
set -euo pipefail
umask 077

DB="${1:-otalink_dev}"
DIR="/var/backups/otalink"
RETENTION_DAYS=14

ts="$(date +%Y%m%d-%H%M%S)"
partial="$DIR/.${DB}-${ts}.dump.partial"
final="$DIR/${DB}-${ts}.dump"

# Si algo falla, no dejamos un archivo a medias que parezca una copia válida.
trap 'rm -f "$partial"' EXIT

pg_dump --format=custom --file="$partial" "$DB"

# Comprueba que el archivo se puede leer como volcado válido antes de aceptarlo.
pg_restore --list "$partial" > /dev/null

mv "$partial" "$final"
( cd "$DIR" && sha256sum "$(basename "$final")" > "$(basename "$final").sha256" )

# Retención: borra copias (y sus huellas) de más de $RETENTION_DAYS días.
find "$DIR" -maxdepth 1 -type f -name "${DB}-*" -mtime "+$RETENTION_DAYS" -delete

echo "backup ok: $final ($(stat -c %s "$final") bytes)"
