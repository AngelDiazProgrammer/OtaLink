# Documentación de OtaLink

Esta carpeta cuenta **cómo se construyó OtaLink, fase por fase**, y está escrita para que un desarrollador que llega nuevo entienda el proyecto sin haber estado presente.

## Qué es OtaLink

Plataforma social para personas interesadas en anime, manga, cosplay, videojuegos y cultura japonesa y geek. Empieza en Bogotá, Colombia, solo para mayores de 18 años. Tiene dos modos independientes: **Amistad** y **Citas**.

Piezas previstas: aplicación web Blazor, API REST independiente, base de datos PostgreSQL, tiempo real con SignalR (chat), y en el futuro una app móvil que consumirá la misma API.

Todo corre, por ahora, en un servidor Ubuntu virtualizado en el propio computador del desarrollador.

## Índice de fases

| Fase | Documento | Estado |
|---|---|---|
| 1 | [Configuración del entorno](fase-01-configuracion-del-entorno.md) | Completada (con una observación abierta, ver el documento) |
| 2 | [Solución .NET por capas](fase-02-solucion-dotnet.md) y primer flujo vertical (registro, perfil) | En curso (etapa 1: esqueleto, hecha) |
| 3 | MVP: identidad, descubrimiento, citas, chat, comunidad | Pendiente |
| 4 | Acceso desde Internet, dominio y HTTPS | Pendiente |
| 5 | Seguridad, publicación y mantenimiento | Pendiente |

Los números de esta carpeta son de **documentación** y no coinciden uno a uno con las "Fases 0 a 9" del plan de trabajo original; el documento de cada fase indica a cuáles corresponde.

## Cómo se relaciona con la carpeta `docs/`

| Carpeta | Para qué sirve | Estilo |
|---|---|---|
| `documentacion/` (esta) | Entender el proyecto: qué se hizo, por qué y cómo, en orden cronológico | Narrativo, didáctico |
| `docs/` | Referencia técnica viva de cada tema (red, base de datos, respaldos...) | Tablas y datos exactos, se actualiza al cambiar algo |

Si necesitas **aprender** el proyecto, lee esta carpeta. Si necesitas **un dato concreto** (la IP, los roles de la base, el procedimiento de restauración), ve a `docs/`.

## Reglas para mantener esta documentación

1. Cada fase nueva es un archivo nuevo: `fase-NN-nombre-corto.md`.
2. Solo se documenta lo que **se hizo y se verificó**. Lo pendiente va en una sección propia, marcado como pendiente.
3. Nunca se escriben contraseñas, tokens ni claves. Se indica **dónde** se guardan, no su valor.
4. Cada paso indica **dónde se ejecuta**: en WINDOWS (el anfitrión) o en la VM (Ubuntu).
5. Los errores y su causa real se documentan: son parte de lo que enseña el proyecto.
6. Al terminar una fase se actualiza la tabla de arriba.
