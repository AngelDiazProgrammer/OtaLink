# ARCHITECTURE

Una sola solución, `OtaLink.sln`, con capas separadas. Esta es la arquitectura **definitiva** (decidida el 2026-10-15; reemplaza el diseño inicial con repositorios, Contracts y Minimal APIs).

## Flujo de una operación

```
PETICIÓN    .razor + .razor.cs ──► Controller ──► IXxxService ──► XxxService ──► OtaLinkRutinas ──► PostgreSQL
RESPUESTA   PostgreSQL ──► OtaLinkRutinas ──► XxxService ──► IXxxService ──► Controller ──► .razor.cs ──► .razor
                (Web)        (Api)         (Application)  (Infrastructure)    (Domain)
```

Dicho en la regla original: **controladores → interfaces → servicios** y de vuelta **servicios → interfaces → controladores**.

## Capas

| Proyecto | Contiene | Puede referenciar |
|---|---|---|
| `OtaLink.Application` | DTOs (petición/respuesta), **interfaces** de servicio, validación primaria de los DTOs, `Result` | nada |
| `OtaLink.Domain` | Todo lo de la base de datos: **entidades** (una por tabla), `OtaLinkDbContext`, y **`OtaLinkRutinas`** (el archivo dedicado a llamar los procedimientos y funciones) | nada |
| `OtaLink.Infrastructure` | **Servicios** (uno por módulo, implementan las interfaces de Application) y el registro de dependencias | Application, Domain |
| `OtaLink.Api` | **Controllers** (`[ApiController]`, solo JSON, sin vistas), autenticación, OpenAPI, SignalR | Application, Infrastructure (solo para registrar los servicios) |
| `OtaLink.Web` | Front: **solo `.razor` y `.razor.cs`**. El `.cs` llama directamente al controlador por HTTP | Application (solo los DTOs) |

Las reglas las **hace cumplir** `tests/OtaLink.ArchitectureTests` (se comprobó que fallan ante una violación):
- Application y Domain no conocen ninguna otra capa; Application tampoco conoce EF Core ni Npgsql.
- Los controladores no dependen de Infrastructure, Domain, EF Core ni Npgsql: solo interfaces y DTOs.
- Cada servicio vive en Infrastructure e implementa una interfaz de Application.
- **No existen repositorios.**
- Cada proyecto referencia únicamente lo permitido (la Web, solo Application).

## Módulos

Cada funcionalidad o módulo tiene **su propio servicio independiente**, con sus dependencias inyectadas directamente. Un servicio no depende de repositorios: usa `OtaLinkRutinas`, y si necesita otro módulo, inyecta su interfaz. Estructura por módulo:

```
Application/Perfiles/    PerfilDtos.cs, IPerfilService.cs
Infrastructure/Servicios/PerfilService.cs
Api/Controllers/PerfilesController.cs
Web/Components/Pages/Perfil.razor + Perfil.razor.cs
deploy/postgres/migrations/NNNN_proceso_perfiles*.sql
```

## La base de datos concentra la transaccionalidad

- **Escrituras = procedimientos (`sp_*`)**: cada `CALL` es una transacción completa en la base. Devuelven `p_codigo` ('ok' o un código de error estable) y `p_mensaje`.
- **Consultas = funciones (`fn_*`)**: no modifican nada.
- **Triggers (`tgf_*`/`trg_*`)**: fecha de actualización y auditoría automáticas.
- **Sin SQL de negocio en el código.** El único lugar que sabe cómo se invoca una rutina es `OtaLinkRutinas` (Domain). EF Core se usa para modelar las entidades y leer los resultados de las funciones; **`SaveChanges` está bloqueado** y lanza una excepción, para que nadie escriba saltándose las reglas de la base.
- **Todos los scripts se revisan antes de ejecutarse**; los ejecuta una persona, como `otalink_migrator`, desde DBeaver. Ver [DATABASE.md](DATABASE.md).

## Dónde vive cada regla de negocio

| Nivel | Qué hace | Dónde |
|---|---|---|
| Validación **primaria** | Forma del dato (obligatorios, longitudes) y reglas evidentes (mayor de 18). Evita viajar a la base con datos mal diligenciados | DTOs de Application (`DataAnnotations` + `IValidatableObject`) |
| Regla **autoritativa** | La que no se puede saltar: edad, longitudes, máximo de intereses, ciudad válida | Procedimientos de la base, con valores de `configuracion.parametros` |

Los límites numéricos existen en dos sitios (los DTOs y `configuracion.parametros`); si se cambia uno, se cambia el otro. Los códigos de error de los procedimientos son los mismos que viajan en la API.

## Errores

- Los servicios devuelven `Result<T>`; un dato inválido no es una excepción.
- `AppError` lleva un **código estable** (el del procedimiento) y un `ErrorType` (Validación, NoEncontrado, Conflicto) que el controlador traduce a 400, 404 o 409.
- Lo inesperado lo atrapa `GlobalExceptionHandler`: registra el detalle en el servidor y responde un `ProblemDetails` genérico, sin trazas.

## Convenciones de nombres

- Negocio y base de datos en **español** (`Perfil`, `Interes`, `Usuario`, `seguridad.usuarios`); sufijos técnicos en inglés (`Service`, `Controller`, `Request`, `Response`).
- Esquemas, tablas y columnas en `snake_case` (la convención de EF convierte `NombrePublico` en `nombre_publico`).
- Prefijos de rutinas: `sp_` procedimiento, `fn_` función, `tgf_` función de trigger, `trg_` trigger.

## Blazor: modo de renderizado

**Interactive Server global + Static SSR solo en las páginas de autenticación.**

- Los tokens de la API viven en el servidor; el navegador solo recibe una cookie `HttpOnly` (patrón BFF). No hace falta CORS para la web.
- Login, registro, recuperación y verificación de correo son Static SSR (`[ExcludeFromInteractiveRouting]`), porque una cookie solo se escribe en una respuesta HTTP y un circuito interactivo no la genera. Contrastado con la documentación de Microsoft para .NET 10.
- Riesgos aceptados y a medir en beta: latencia por interacción cuando se acceda por túnel y memoria por usuario conectado.

## Proxy y cabeceras

Nginx reenvía a `127.0.0.1:5000` (web) y `127.0.0.1:5100` (API). La API solo confía en `X-Forwarded-*` que provienen de loopback. Pendiente replicarlo en la Web.
