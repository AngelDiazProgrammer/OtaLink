# BACKUP-RESTORE

## Copia local automática (activa)

| Elemento | Detalle |
|---|---|
| Qué copia | Base `otalink_dev` (formato custom de `pg_dump`) |
| Cuándo | Diario ~03:00 (`otalink-db-backup.timer`, `Persistent=true`: si la VM estaba apagada, corre al encender) |
| Dónde | `/var/backups/otalink/` dentro de la VM (solo `postgres`, permisos `600`) |
| Retención | 14 días |
| Integridad | Cada `.dump` tiene su `.dump.sha256`; el script valida con `pg_restore --list` antes de aceptar la copia |
| Archivos versionados | `deploy/scripts/otalink-db-backup.sh`, `deploy/systemd/otalink-db-backup.{service,timer}` |

Comandos de consulta (en la VM):

```bash
sudo ls -lh /var/backups/otalink
systemctl list-timers otalink-db-backup.timer --no-pager
journalctl -u otalink-db-backup.service -n 10 --no-pager
```

### Limitación

Las copias están en el mismo disco que la base. Protegen contra errores humanos, no contra la pérdida del disco, de la VM o del portátil.

## Restauración probada (2026-10-15)

Se creó una tabla con 3 filas, se hizo una copia, se comprobaron las huellas SHA256 (`OK`) y se restauró en una base aparte (`otalink_restore_test`): las 3 filas volvieron. La base de pruebas y la tabla de ejemplo se eliminaron.

Procedimiento (en la VM; restaura en una base nueva, nunca encima de la que está en uso):

```bash
sudo -u postgres bash -c 'cd /var/backups/otalink && sha256sum -c *.sha256'
sudo -u postgres createdb otalink_restore_test
sudo -u postgres bash -c 'pg_restore --no-owner --dbname=otalink_restore_test "$(ls -t /var/backups/otalink/*.dump | head -1)"'
sudo -u postgres psql -d otalink_restore_test -c "\dt"
sudo -u postgres dropdb otalink_restore_test
```

## Copia externa (pendiente, decidido)

- Destino: Google Drive de una cuenta de Gmail dedicada a la aplicación. No se guarda nada en el Windows local.
- Las copias contienen datos de usuarios: se cifrarán antes de subirlas.
- A definir: herramienta de subida, manejo del token de acceso (fuera de Git, permisos restringidos) y cifrado.
- También deben respaldarse, cifradas, las contraseñas de `/etc/otalink/`.

## Recuperar en otra máquina

Pendiente: se documentará cuando exista la copia externa.
