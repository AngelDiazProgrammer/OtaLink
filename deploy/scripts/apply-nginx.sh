#!/usr/bin/env bash
# Aplica la configuración de deploy/nginx al servidor.
# Uso (en la VM): sudo ./apply-nginx.sh <carpeta-con-conf.d-snippets-sites-available>
# Hace copia de /etc/nginx, valida con "nginx -t" y SOLO entonces recarga.
# Si la validación falla, restaura la copia y no recarga.
set -euo pipefail

SRC="${1:?uso: sudo $0 <carpeta-nginx>}"
[ "$(id -u)" -eq 0 ] || { echo "Ejecutar con sudo"; exit 1; }
[ -d "$SRC/sites-available" ] || { echo "No encuentro $SRC/sites-available"; exit 1; }

BACKUP="/var/backups/nginx-config-$(date +%Y%m%d-%H%M%S)"
cp -a /etc/nginx "$BACKUP"
echo "Copia previa: $BACKUP"

restore() {
    echo "Validación fallida: restaurando la configuración anterior"
    rm -rf /etc/nginx
    cp -a "$BACKUP" /etc/nginx
}

install -m 644 "$SRC"/conf.d/*.conf          /etc/nginx/conf.d/
install -m 644 "$SRC"/snippets/*.conf        /etc/nginx/snippets/
install -m 644 "$SRC"/sites-available/*.conf /etc/nginx/sites-available/

for f in "$SRC"/sites-available/*.conf; do
    ln -sf "/etc/nginx/sites-available/$(basename "$f")" "/etc/nginx/sites-enabled/$(basename "$f")"
done
# El sitio de bienvenida de Ubuntu choca con nuestro default_server.
rm -f /etc/nginx/sites-enabled/default

if ! nginx -t; then
    restore
    exit 1
fi

systemctl reload nginx
echo "Nginx recargado correctamente."
