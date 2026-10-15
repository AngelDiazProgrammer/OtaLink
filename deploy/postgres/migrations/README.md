# Scripts SQL de la base de datos

Toda la estructura y la lógica transaccional de OtaLink vive en la base de datos, y se entrega como **scripts SQL numerados, escritos a mano**. Ninguno se aplica automáticamente.

## Reglas de trabajo

1. **Cada script se revisa antes de ejecutarse.** Cada uno es pequeño y trae al inicio: qué hace, qué requiere y como qué rol se ejecuta.
2. Se ejecutan **en orden numérico**, desde DBeaver, conectado como **`otalink_migrator`** a **`otalink_dev`**: `Archivo → Abrir archivo`, leer, y ejecutar como script completo con **`Alt + X`**.
3. Cada script va dentro de `BEGIN; ... COMMIT;`: si algo falla, no se aplica nada a medias. Al final trae una consulta de **comprobación** (comentada) para ejecutar aparte.
4. Antes de un cambio delicado, copia de seguridad: `sudo systemctl start otalink-db-backup.service` (en la VM).
5. Para cambiar la base se crea un script **nuevo** con el siguiente número. Los scripts ya ejecutados no se editan.

## Esquemas

| Esquema | Para qué |
|---|---|
| `configuracion` | Parámetros del sistema (reglas de negocio configurables) |
| `proceso` | Tablas transaccionales que dependen de otras (perfiles...) |
| `log` | Auditoría y registros |
| `seguridad` | Cuentas y datos de acceso |
| `smart` | Tablas **fuertes**: no dependen de ninguna otra (catálogos) |

## Modelo de permisos

La aplicación (`otalink_app`) **no tiene permiso sobre ninguna tabla**: solo puede ejecutar procedimientos y funciones, que se crean `SECURITY DEFINER` (se ejecutan con los permisos de `otalink_migrator`). Así la regla de negocio y la transacción no se pueden saltar con un `SELECT` o `INSERT` directo. Lo configura `0001`.

## Convenciones de rutinas

| Prefijo | Tipo | Para qué |
|---|---|---|
| `sp_` | Procedimiento | **Escribe** (transacción). Devuelve `p_codigo` (`'ok'` o un código de error estable) y `p_mensaje` |
| `fn_` | Función | **Consulta** (no modifica) |
| `tgf_` / `trg_` | Función de trigger / trigger | Fecha de actualización y auditoría automáticas |

Todas llevan `SECURITY DEFINER` con `search_path` fijo y nombres calificados con su esquema.

## Orden de ejecución

| # | Archivo | Contenido | Estado |
|---|---|---|---|
| 0001 | `0001_esquemas_y_permisos.sql` | Los 5 esquemas y el modelo de permisos | Ejecutado |
| 0002 | `0002_configuracion.sql` | `configuracion.parametros` (reglas con números), `tgf_actualizar_fecha`, `fn_parametro_entero` | Ejecutado |
| 0003 | `0003_log_auditoria.sql` | `log.auditoria` y el trigger genérico `tgf_auditar` | Ejecutado |
| 0004 | `0004_smart_catalogos.sql` | `smart.ciudades`, `smart.intereses` y sus funciones de lectura | Ejecutado |
| 0005 | `0005_smart_datos_intereses_otaku.sql` | 172 intereses en 9 categorías (repetible: no duplica) | Ejecutado |
| 0006 | `0006_seguridad_usuarios.sql` | `seguridad.usuarios` y sus triggers | Ejecutado |
| 0007 | `0007_seguridad_usuarios_rutinas.sql` | `sp_usuario_crear/actualizar/eliminar` y `fn_usuario_por_*` | Ejecutado |
| 0008 | `0008_proceso_perfiles.sql` | `proceso.perfiles`, `proceso.perfiles_intereses` y sus triggers | Ejecutado |
| 0009 | `0009_proceso_perfiles_rutinas.sql` | Reglas de negocio de perfiles: `sp_perfil_*` y `fn_perfil_*` | Ejecutado |

Dependencias: `0003` y `0004` necesitan `0001`; `0006` necesita `0002` y `0003`; `0008` necesita `0004` y `0006`; `0009` necesita `0002`, `0007` y `0008`.

Todos los scripts se ejecutaron el 2026-10-15 sin errores y se verificaron con pruebas automáticas contra la base real (ver `tests/OtaLink.IntegrationTests/BaseReal`).
