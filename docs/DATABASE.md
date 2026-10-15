# DATABASE

## Motor

PostgreSQL 18.6 (paquete `postgresql` de Ubuntu 26.04), instalado en la VM `otalink-server`.
Escucha solo en `127.0.0.1:5432` (verificado con `ss -tln`). El puerto 5432 no se abre en UFW.

## Base y roles (desarrollo)

| Elemento | Detalle |
|---|---|
| Base | `otalink_dev` |
| `otalink_migrator` | Dueño de la base y del esquema `public`. Aplica migraciones, manualmente y con revisión. |
| `otalink_app` | La API en ejecución. Solo `SELECT/INSERT/UPDATE/DELETE` en tablas y `USAGE/SELECT` en secuencias. |
| `postgres` | Superusuario. Solo administración local con `sudo -u postgres`. La aplicación nunca lo usa. |

Script versionado: [deploy/postgres/01-create-database.sql](../deploy/postgres/01-create-database.sql).
No contiene contraseñas: las recibe como variables de `psql`.

### Contraseñas

- Generadas aleatoriamente en la VM, en `/etc/otalink/db-migrator-password` y `/etc/otalink/db-app-password`.
- Permisos `600`, propietario `root`. Fuera de Git.
- Pendiente: incluirlas en el respaldo cifrado (ver BACKUP-RESTORE.md). La del migrador se rotó el 2026-10-15 por haberse mostrado en el chat.

## Verificación de permisos (2026-10-15)

- `otalink_app` se autentica por `127.0.0.1`.
- `otalink_app` no puede crear tablas (`permission denied for schema public`).
- `otalink_migrator` puede crear tablas.
- `otalink_app` puede insertar y leer en tablas del migrador, pero no borrarlas (`must be owner of table`).

## Cómo ejecutar el script

`psql` corre como el usuario `postgres`, que no puede leer `/home/angel-diaz`. Por eso el script se entrega por entrada estándar (`<`) y no con `-f`:

```bash
sudo -u postgres psql -v ON_ERROR_STOP=1 \
  -v mig_pw="$(sudo cat /etc/otalink/db-migrator-password)" \
  -v app_pw="$(sudo cat /etc/otalink/db-app-password)" \
  < 01-create-database.sql
```

## Pendiente

- Modelo de datos (tablas) con las migraciones de EF Core.
- Copias de seguridad y restauración probada.
- Base de producción separada de `otalink_dev`.

## Desarrollo desde Windows (2026-10-15)

- La API de desarrollo se conecta a `otalink_dev` con el rol `otalink_app` por un túnel SSH: `localhost:15432` (Windows) → `127.0.0.1:5432` (VM). El puerto 5432 sigue cerrado en UFW.
- La cadena de conexión vive en User Secrets (`Database:ConnectionString`), nunca en Git. En el servidor será la variable de entorno `Database__ConnectionString`.
- Scripts: `deploy/scripts/dev-db-tunnel.ps1` y `deploy/scripts/dev-set-db-secret.ps1` (este valida el formato de la contraseña).
- Verificado: `GET /health/ready` → `200 Healthy`.

## Cambios de esquema

Cada migración de EF Core se entrega como un script SQL numerado en `deploy/postgres/migrations/` (ver su README). Se ejecutan a mano desde DBeaver como `otalink_migrator`. Convención de nombres: `snake_case`; historial en `__ef_migrations_history`.
