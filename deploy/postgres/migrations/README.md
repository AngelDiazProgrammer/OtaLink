# Scripts SQL de migración

Cada cambio en el modelo de datos se entrega como un **script SQL numerado**. Los scripts se ejecutan **a mano y en orden**, desde DBeaver, conectado como el rol **`otalink_migrator`** a la base **`otalink_dev`**. Ninguna migración se aplica automáticamente.

## Por qué así

- Cada cambio de esquema se puede **leer y revisar** antes de ejecutarse.
- La API en ejecución usa `otalink_app`, que **no puede** alterar el esquema.
- El mismo procedimiento sirve para producción (con copia de seguridad antes).

## Cómo se generan (lo hace el equipo de desarrollo)

EF Core es la fuente de verdad del modelo. Para cada migración se genera el SQL con `dotnet ef`, que **no se conecta a ninguna base**:

```powershell
dotnet ef migrations script <migracion-anterior> <migracion-nueva> --idempotent `
  -p src\OtaLink.Infrastructure -s src\OtaLink.Api -o deploy\postgres\migrations\NNNN_nombre.sql
```

`--idempotent` hace que el script compruebe en `__ef_migrations_history` qué ya se aplicó, así que ejecutarlo dos veces no duplica nada.

## Cómo se ejecutan

1. Haz una copia de seguridad si el cambio es delicado: `sudo systemctl start otalink-db-backup.service` (en la VM).
2. En DBeaver, conéctate como `otalink_migrator` a `otalink_dev`.
3. Abre el script (`Archivo → Abrir archivo`), **léelo**, y ejecútalo como script completo (`Alt + X`).
4. Comprueba el resultado con la consulta que acompaña a cada script.

## Orden

| Archivo | Contenido | Estado |
|---|---|---|
| `NNNN_...sql` | (aún no hay migraciones: el modelo de datos llega en la siguiente etapa) | — |

Los permisos para `otalink_app` sobre las tablas nuevas se conceden solos: `01-create-database.sql` configuró `ALTER DEFAULT PRIVILEGES` para los objetos que cree `otalink_migrator`.
