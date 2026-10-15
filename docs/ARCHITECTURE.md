# ARCHITECTURE

Una sola solución, `OtaLink.sln`, con proyectos separados por responsabilidad. La regla de oro: **las dependencias apuntan hacia el dominio**, nunca al revés.

## Capas y dependencias permitidas

```
OtaLink.Web ───────────────► OtaLink.Contracts
(Blazor, solo habla HTTP        ▲
 con la API)                    │
                                │
OtaLink.Api ──┬─────────────────┘
(composition  ├──► OtaLink.Application ──► OtaLink.Domain
 root)        └──► OtaLink.Infrastructure ─► Application, Domain
```

| Proyecto | Responsabilidad | Puede referenciar | NO puede depender de |
|---|---|---|---|
| `OtaLink.Domain` | Entidades, reglas de negocio, objetos de valor | nada | EF Core, ASP.NET, cualquier otra capa |
| `OtaLink.Application` | Casos de uso, puertos (interfaces), validaciones | Domain | Infrastructure, EF Core |
| `OtaLink.Infrastructure` | EF Core, PostgreSQL, correo, almacenamiento | Application, Domain | Api, Web |
| `OtaLink.Contracts` | DTOs públicos de la API | nada | Domain (no se exponen entidades) |
| `OtaLink.Api` | Minimal APIs, autenticación, SignalR, OpenAPI | Application, Contracts, Infrastructure | — (es la raíz de composición) |
| `OtaLink.Web` | Blazor Web App | **solo Contracts** | Domain, Application, Infrastructure, PostgreSQL |

Estas reglas **no son solo documentación**: las hacen cumplir las pruebas de `tests/OtaLink.ArchitectureTests`. Se comprobó que fallan si alguien añade una referencia prohibida.

## Inyección de dependencias

Cada capa expone su propia extensión de registro:

- `AddApplication()` en Application.
- `AddInfrastructure(IConfiguration)` en Infrastructure.
- Registros propios de la API en `Program.cs`.

Ciclos de vida: `DbContext` y casos de uso son **Scoped**; nunca Singleton para el `DbContext`. No se usa el localizador de servicios.

## Blazor: modo de renderizado

**Interactive Server global + Static SSR solo en las páginas de autenticación.**

- Los tokens de la API viven en el servidor; el navegador solo recibe una cookie `HttpOnly` (patrón BFF). No hace falta CORS para la web.
- Login, registro, recuperación y verificación de correo son Static SSR (`[ExcludeFromInteractiveRouting]`), porque una cookie solo se escribe en una respuesta HTTP y un circuito interactivo no la genera. Está contrastado con la documentación de Microsoft para .NET 10.
- La interfaz hablará con la API a través de una abstracción propia (por ejemplo `IOtaLinkApiClient`), para poder migrar a WebAssembly si la latencia del acceso externo o la memoria por circuito lo exigen.
- Riesgos aceptados y a medir en beta: latencia por interacción cuando se acceda por túnel y memoria por usuario conectado.

## Proxy y cabeceras

Nginx reenvía a `127.0.0.1:5000` (web) y `127.0.0.1:5100` (API). La API solo confía en `X-Forwarded-*` que provienen de loopback (`ForwardedHeadersOptions` con `KnownProxies` explícito). Pendiente replicarlo en la Web.

## Manejo de errores

`GlobalExceptionHandler` (API) registra el detalle en el log del servidor y responde con un `ProblemDetails` genérico: sin trazas ni datos internos.
