# Fase 2: solución .NET por capas

> Corresponde a la "Fase 5" del plan original (crear la solución .NET).
> Estado: **en curso**. Esta versión documenta la etapa 1, el esqueleto. Las siguientes etapas (base de datos con EF Core, identidad, primer flujo vertical) se añadirán aquí.
> Fecha de la etapa 1: 15 de octubre de 2026.

## Índice

1. [Objetivo de la fase](#1-objetivo-de-la-fase)
2. [Etapa 1: el esqueleto de la solución](#2-etapa-1-el-esqueleto-de-la-solución)
3. [Decisión: modo de renderizado de Blazor](#3-decisión-modo-de-renderizado-de-blazor)
4. [Versiones de paquetes y por qué estas](#4-versiones-de-paquetes-y-por-qué-estas)
5. [Pruebas: cómo ejecutarlas y qué comprueban](#5-pruebas-cómo-ejecutarlas-y-qué-comprueban)
6. [Problemas encontrados y lecciones](#6-problemas-encontrados-y-lecciones)
7. [Estado y pendientes](#7-estado-y-pendientes)

---

## 1. Objetivo de la fase

Construir OtaLink por capas, con la arquitectura protegida por pruebas desde el primer día, y llegar a un **primer flujo vertical completo**: registro, verificación de correo, inicio de sesión, perfil e intereses, guardados en PostgreSQL y visibles en Blazor a través de la API.

**Por qué por capas:** las reglas de negocio no deben depender de la base de datos ni del framework web. Así se pueden probar sin infraestructura y cambiar la infraestructura sin reescribir el negocio.

Detalle de capas y dependencias: [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md).

---

## 2. Etapa 1: el esqueleto de la solución

### 2.1 Archivos de la raíz

| Archivo | Qué es y para qué sirve |
|---|---|
| `OtaLink.sln` | La **única** solución. Agrupa los 6 proyectos de código y los 3 de pruebas |
| `global.json` | Fija el SDK de .NET (10.0.400) para que todos compilen con la misma versión, y activa el motor de pruebas nuevo |
| `Directory.Build.props` | Reglas comunes a todos los proyectos: .NET 10, nulabilidad, **advertencias tratadas como errores**, análisis de código |
| `Directory.Packages.props` | **Gestión central de paquetes**: las versiones se escriben solo aquí; los `.csproj` solo nombran el paquete |
| `.editorconfig` | Estilo de código y saltos de línea (LF en los archivos del servidor Linux) |

**Por qué `TreatWarningsAsErrors`:** una advertencia ignorada hoy es un fallo mañana. Con esta opción el código no compila hasta que se resuelve.

### 2.2 Los proyectos

```
src/
  OtaLink.Domain          reglas de negocio puras (sin dependencias)
  OtaLink.Contracts       DTOs públicos de la API (sin dependencias)
  OtaLink.Application     casos de uso y puertos      → Domain
  OtaLink.Infrastructure  EF Core, PostgreSQL, correo → Application, Domain
  OtaLink.Api             API REST (Minimal APIs)     → Application, Contracts, Infrastructure
  OtaLink.Web             Blazor Web App              → solo Contracts
tests/
  OtaLink.UnitTests          prueba Domain y Application sin infraestructura
  OtaLink.IntegrationTests   levanta la API en memoria y la consulta por HTTP
  OtaLink.ArchitectureTests  hace cumplir las reglas de dependencias entre capas
```

Qué hay hoy dentro de cada uno (es deliberadamente poco):

- **Domain, Contracts:** solo un marcador (`AssemblyReference`) para que las pruebas de arquitectura localicen el ensamblado.
- **Application:** `AddApplication()`, el punto donde se registrarán los casos de uso.
- **Infrastructure:** `AddInfrastructure(IConfiguration)`, vacío por ahora. En la siguiente etapa recibe el `DbContext`.
- **Api:** `Program.cs` con el registro de capas, OpenAPI y Scalar (solo en Desarrollo), `/health`, manejo centralizado de errores y cabeceras de proxy.
- **Web:** la plantilla oficial de Blazor, **sin páginas de ejemplo**, con interactividad global en modo Server.

### 2.3 Detalles de la API que conviene entender

**Inyección de dependencias.** `Program.cs` llama a `AddApplication()` y `AddInfrastructure(configuration)`. Cada capa decide sus propios registros, y la API solo los compone.

**Manejo centralizado de errores.** `Errors/GlobalExceptionHandler.cs` captura cualquier excepción no controlada, la registra con detalle **en el log del servidor** y devuelve al cliente un `ProblemDetails` genérico (`500`, "Ocurrió un error inesperado."). El cliente nunca ve trazas ni nombres internos.

**Cabeceras de proxy.** Nginx añade `X-Forwarded-For` y `X-Forwarded-Proto`. La API solo las acepta si vienen de `127.0.0.1` o `::1` (la propia VM), porque `KnownProxies` se limpió y se rellenó explícitamente. Así un cliente de Internet no puede falsear su IP ni su esquema.

**OpenAPI.** `/openapi/v1.json` y la interfaz Scalar en `/scalar` existen solo en Desarrollo, para no publicar la descripción de la API en producción sin decidirlo.

**Puertos.** Los `launchSettings.json` fijan Web en `127.0.0.1:5000` y API en `127.0.0.1:5100`, los mismos destinos que configuramos en Nginx.

**`public partial class Program;`** al final de `Program.cs` hace visible el punto de entrada a las pruebas de integración (`WebApplicationFactory`).

### 2.4 Cómo se creó

1. `dotnet new sln -n OtaLink --format sln`. En .NET 10 el formato por defecto es `.slnx`; se pidió `.sln` porque así lo exige el proyecto.
2. Los proyectos de biblioteca y la API se escribieron a mano (son pocos archivos y así el contenido es controlado).
3. La web salió de la plantilla oficial: `dotnet new blazor --interactivity Server --all-interactive --empty --no-https --auth None`. Después se ajustó el `.csproj` para que referencie **solo Contracts**.
4. `dotnet sln add` de todos los proyectos.

---

## 3. Decisión: modo de renderizado de Blazor

**Interactive Server global + Static SSR únicamente en las páginas de autenticación.**

| Opción | Dónde corre la interfaz | Por qué se descartó o se eligió |
|---|---|---|
| Static SSR | Servidor, sin interactividad | Insuficiente para chat y listas dinámicas; solo se usa en autenticación |
| **Interactive Server** | **Servidor, con conexión WebSocket permanente** | **Elegida** |
| WebAssembly | Navegador | Los tokens quedarían en el navegador y haría falta CORS |
| Auto | Primero servidor, luego navegador | Cada componente debería funcionar en ambos entornos: demasiada complejidad |

**Razones de la elección:**

1. **Seguridad.** Los tokens de la API quedan en el servidor; el navegador solo tiene una cookie `HttpOnly`.
2. **Sin CORS** para la web: es de servidor a servidor.
3. **Simplicidad:** un solo modelo de ejecución que depurar.

**Por qué el login es Static SSR:** una cookie de sesión se escribe en una respuesta HTTP, y un circuito interactivo (una conexión WebSocket abierta) no genera respuestas HTTP. Es una obligación técnica, no una elección. Se marca la página con `[ExcludeFromInteractiveRouting]`; se contrastó con la documentación de Microsoft para .NET 10.

**Costos aceptados, que se medirán en beta:** latencia en cada interacción cuando se acceda por un túnel desde un portátil casero, y memoria por usuario conectado. Para no quedar atrapados, la interfaz hablará con la API a través de una abstracción propia, de modo que migrar a WebAssembly sea acotado.

---

## 4. Versiones de paquetes y por qué estas

Se consultó nuget.org el 15 de octubre de 2026 y se eligieron las **últimas versiones estables**. Ya existe .NET 11 en versión candidata (RC); **se evitó a propósito**, porque el proyecto usa .NET 10 LTS y un despliegue estable no debe depender de preliminares.

| Paquete | Versión | Para qué |
|---|---|---|
| EF Core, Design, Identity.EntityFrameworkCore | 10.0.13 | Acceso a datos e identidad |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | Proveedor de PostgreSQL |
| Microsoft.AspNetCore.OpenApi, Mvc.Testing, SignalR.Client | 10.0.13 | Documentación de la API, pruebas, tiempo real |
| Scalar.AspNetCore | 2.18.0 | Interfaz visual de la documentación OpenAPI |
| FluentValidation | 12.2.0 | Validaciones |
| Serilog.AspNetCore | 10.0.0 | Registros estructurados |
| MudBlazor | 9.11.0 | Componentes de interfaz (soporta `net10.0`, verificado en su metadato) |
| xunit.v3.mtp-v2 | 4.0.2 | Pruebas |
| NSubstitute | 6.2.0 | Dobles de prueba |
| NetArchTest.Rules | 1.3.2 | Pruebas de arquitectura |

La mayoría están declaradas pero **aún no se usan**: se activan en las etapas siguientes. Declararlas aquí centraliza las versiones desde el principio.

**Nota sobre el servidor:** el servidor tiene el runtime 10.0.12 y los paquetes son 10.0.13. Es compatible: los paquetes viajan dentro de la aplicación; el runtime compartido solo necesita ser de la misma versión mayor. Se confirmará con el primer despliegue real.

---

## 5. Pruebas: cómo ejecutarlas y qué comprueban

```powershell
dotnet build OtaLink.sln -c Release
dotnet test --solution OtaLink.sln -c Release --no-build
```

Resultado actual: **12 pruebas, todas correctas**, **0 advertencias, 0 errores**.

| Suite | Qué comprueba |
|---|---|
| `UnitTests` | `AddApplication()` se encadena con la colección de servicios |
| `IntegrationTests` | La API arranca en memoria; `/health` responde `200 Healthy`; una ruta inexistente responde `404` |
| `ArchitectureTests` | **Domain** sin EF Core/ASP.NET/otras capas; **Application** sin Infrastructure; **Contracts** sin dominio; y que cada proyecto referencia **solo** lo permitido (la Web, solo Contracts) |

**Se verificó que las pruebas de arquitectura funcionan:** se añadió a propósito una referencia prohibida (Web → Infrastructure), la prueba falló, y se revirtió. Una regla que nunca falla no protege nada.

---

## 6. Problemas encontrados y lecciones

| # | Síntoma | Causa real | Lección |
|---|---|---|---|
| 1 | Error de sintaxis del shell al crear muchos archivos con un solo comando | Comillas dentro de un bloque largo | Para archivos de contenido, usar la herramienta de escritura de archivos, no `heredocs` gigantes |
| 2 | `CA1707` en las pruebas | El analizador prohíbe guiones bajos en nombres | En `tests/` se desactiva con un `.editorconfig` propio: los nombres descriptivos con guion bajo son una convención aceptada |
| 3 | `TestResult` ambiguo | Existe en xUnit y en NetArchTest | Calificar el nombre completo |
| 4 | `dotnet test` decía "Testing with VSTest target is no longer supported..." | En .NET 10 `dotnet test` ya no usa el motor antiguo (VSTest) con xUnit v3 | Activar en `global.json`: `"test": { "runner": "Microsoft.Testing.Platform" }` |
| 5 | "No se ejecutaron pruebas" (código 5) con las pruebas bien escritas | Varias causas encadenadas, ver abajo | Probar el ejecutable directamente para separar "las pruebas fallan" de "el puente falla" |
| 6 | Tildes dañadas (`aÃ±adir`) en archivos reescritos | PowerShell 5.1 lee/escribe sin UTF-8 por defecto | Reescribir con la herramienta de archivos, o indicar `-Encoding UTF8` en lectura y escritura |

### Detalle del problema 5 (el más largo)

Había **tres causas apiladas**, y arreglar solo una no bastaba:

1. `xunit.v3` habla la versión 1 de Microsoft Testing Platform y el SDK 10 trae la 2 → se cambió a **`xunit.v3.mtp-v2`**.
2. Los paquetes del motor antiguo (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`) sobraban → se quitaron.
3. Hubo que **recompilar de cero** (`--no-incremental`) tras activar `UseMicrosoftTestingPlatformRunner`, porque la compilación incremental conservaba el ejecutable anterior.
4. **La causa final:** `dotnet test` reenviaba **`--nologo`** al ejecutable de pruebas, que lo rechazaba. Sin esa opción, las 12 pruebas pasaron.

Lección general: cuando "nada funciona", ejecutar la pieza más pequeña por separado (aquí, el `.exe` de pruebas) y leer el log de diagnóstico (`--diagnostic`) en lugar de probar combinaciones al azar.

---

## 7. Estado y pendientes

**Hecho y verificado:**

- [x] `OtaLink.sln` con 6 proyectos y 3 de pruebas
- [x] Referencias entre proyectos, con reglas de arquitectura protegidas por pruebas
- [x] Inyección de dependencias por capa (`AddApplication`, `AddInfrastructure`)
- [x] Manejo centralizado de errores, `/health`, OpenAPI y cabeceras de proxy en la API
- [x] Blazor Web App con modo de renderizado decidido
- [x] Versiones de paquetes verificadas y centralizadas
- [x] Compila sin advertencias; 12 pruebas correctas

**Pendiente en esta fase:**

- [ ] Configuración tipada (`IOptions`) y logging estructurado (Serilog)
- [ ] EF Core + PostgreSQL: `DbContext`, migraciones (aplicadas manualmente con el rol `otalink_migrator`)
- [ ] Pruebas de integración contra una base real. **Decisión pendiente:** Testcontainers necesita Docker, que no se instaló a propósito; hay que elegir otra vía (por ejemplo, la PostgreSQL de la VM por el túnel SSH, o una instalación local)
- [ ] Primer flujo vertical: registro, verificación de correo, login, perfil e intereses
- [ ] Servicios `systemd` `otalink-api` y `otalink-web` y primer despliegue real en la VM
- [ ] Replicar en la Web la política de cabeceras de proxy
- [ ] Instalar y configurar MudBlazor en la Web
