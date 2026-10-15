# DATABASE

## Motor

PostgreSQL 18.6 (paquete `postgresql` de Ubuntu 26.04), instalado en la VM `otalink-server`.
Escucha solo en `127.0.0.1:5432` (verificado con `ss -tln`). El puerto 5432 no se abre en UFW.

## Base y roles (desarrollo)

| Elemento | Detalle |
|---|---|
| Base | `otalink_dev` |
| `otalink_migrator` | Dueño de la base y de todos los objetos. Ejecuta los scripts SQL, manualmente y tras revisarlos. |
| `otalink_app` | La API en ejecución. **Sin permiso sobre ninguna tabla**: solo puede ejecutar los procedimientos y funciones de `proceso`, `seguridad` y `smart` (lo configura el script 0001). |
| `postgres` | Superusuario. Solo administración local con `sudo -u postgres`. La aplicación nunca lo usa. |

Script versionado: [deploy/postgres/01-create-database.sql](../deploy/postgres/01-create-database.sql).
No contiene contraseñas: las recibe como variables de `psql`.

### Contraseñas

- Generadas aleatoriamente en la VM, en `/etc/otalink/db-migrator-password` y `/etc/otalink/db-app-password`.
- Permisos `600`, propietario `root`. Fuera de Git.
- Pendiente: incluirlas en el respaldo cifrado (ver BACKUP-RESTORE.md). La del migrador se rotó el 2026-10-15 por haberse mostrado en el chat.

## Verificación de permisos del modelo anterior (2026-10-15)

Se hizo con el modelo inicial (permisos de `INSERT/SELECT` sobre tablas). El diseño definitivo es más estricto: la aplicación ya no toca tablas, solo rutinas. Se volverá a verificar al ejecutar los scripts.

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

- Base de producción separada de `otalink_dev`.

## Desarrollo desde Windows (2026-10-15)

- La API de desarrollo se conecta a `otalink_dev` con el rol `otalink_app` por un túnel SSH: `localhost:15432` (Windows) → `127.0.0.1:5432` (VM). El puerto 5432 sigue cerrado en UFW.
- La cadena de conexión vive en User Secrets (`Database:ConnectionString`), nunca en Git. En el servidor será la variable de entorno `Database__ConnectionString`.
- Scripts: `deploy/scripts/dev-db-tunnel.ps1` y `deploy/scripts/dev-set-db-secret.ps1` (este valida el formato de la contraseña).
- Verificado: `GET /health/ready` → `200 Healthy`.

## Esquemas, tablas y rutinas (diseño definitivo, 2026-10-15)

Toda la estructura y la lógica transaccional están en scripts SQL numerados, escritos a mano y revisados antes de ejecutarse: ver [deploy/postgres/migrations/README.md](../deploy/postgres/migrations/README.md).

| Esquema | Tablas |
|---|---|
| `configuracion` | `parametros` |
| `proceso` | `perfiles`, `perfiles_intereses` |
| `log` | `auditoria` |
| `seguridad` | `usuarios` |
| `smart` (tablas fuertes, sin claves foráneas) | `ciudades`, `intereses` |

- **Escrituras = procedimientos (`sp_*`)**, **consultas = funciones (`fn_*`)**, y triggers para fecha de actualización y auditoría.
- **La aplicación no tiene permiso sobre ninguna tabla**: solo ejecuta rutinas, que son `SECURITY DEFINER` (se ejecutan como `otalink_migrator`).
- **Reglas de negocio con números** (edad mínima 18, nombre de 2 a 40 caracteres, biografía de hasta 500, máximo 20 intereses) en `configuracion.parametros`, no dentro del código de cada rutina.
- **Auditoría** (`log.auditoria`) por trigger, **omitiendo datos sensibles**: nunca se copian el hash de contraseña, los sellos de seguridad ni la fecha de nacimiento.
- La hora de negocio es la de Colombia (`America/Bogota`); la mayoría de edad se calcula con ella, y quien nació un 29 de febrero cumple en años no bisiestos el 1 de marzo.

## Estado

Scripts 0001 a 0009 **ejecutados** (2026-10-15) y verificados con pruebas automáticas contra la base real: esquemas y permisos, parámetros, auditoría, catálogos (172 intereses, 1 ciudad), usuarios y perfiles.

Las pruebas crean usuarios temporales (`prueba-…@otalink.test`) y los borran al terminar; su auditoría queda en `log.auditoria`. Para comprobar que no quedan restos, como `otalink_migrator` en DBeaver:

```sql
SELECT count(*) FROM seguridad.usuarios WHERE correo LIKE 'prueba-%@otalink.test';   -- 0
```

## Cómo llama el código a la base

Solo `OtaLinkRutinas` (en `OtaLink.Domain`) sabe cómo se invoca cada rutina; los servicios llaman a sus métodos y nunca contienen SQL. EF Core mapea las entidades y lee los resultados de las funciones; `SaveChanges` está bloqueado. Ver [ARCHITECTURE.md](ARCHITECTURE.md).
