# Fase 2: solución .NET por capas

> Corresponde a la "Fase 5" del plan original (crear la solución .NET).
> Estado: **en curso**. Etapa 1 (esqueleto), etapa 2 (configuración, registros y conexión a la base real) y **etapa 3: reorganización de la arquitectura** (la vigente).
> Fecha: 15 de octubre de 2026.
>
> ⚠️ **La etapa 3 reemplaza parte del diseño de las etapas 1 y 2** (repositorios, `Contracts`, EF Core con migraciones, Minimal APIs). Esas secciones se conservan como historia y están marcadas **(superado)**. La arquitectura vigente está en [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md) y en la sección 2.8.

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

### 2.2 Los proyectos (superado: ver 2.8)

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

### 2.5 Etapa 2: configuración, registros y conexión a la base real

**Objetivo:** que la API tenga configuración tipada y validada, registros estructurados, un `DbContext` de EF Core y una conexión comprobada con la PostgreSQL de la VM, **sin guardar nunca una contraseña en Git**.

**Configuración tipada.** `DatabaseOptions` (sección `Database`, clave `ConnectionString`) se enlaza con el patrón `IOptions` y se valida al arrancar (`ValidateOnStart`). Si falta la cadena de conexión, **la API se niega a arrancar** con el mensaje `Falta la configuración 'Database:ConnectionString'`, en vez de fallar en la primera consulta. Se comprobó ejecutándola sin configuración.

**De dónde sale la cadena de conexión:**

| Entorno | Fuente | En Git |
|---|---|---|
| Desarrollo (Windows) | *User Secrets* (`%APPDATA%\Microsoft\UserSecrets\otalink-api-dev`) | No |
| Servidor (VM) | Variable de entorno `Database__ConnectionString` (se definirá en el servicio `systemd`) | No |
| Pruebas | Valor ficticio inyectado por la prueba | Sí, no es real |

`appsettings.json` solo contiene la clave vacía como documentación.

**Registros estructurados (Serilog).** Configurados en `appsettings.json` (sección `Serilog`): nivel `Information`, con `Warning` para ASP.NET Core, EF Core y System. Salida por consola: en el servidor la recoge `systemd` y se lee con `journalctl`. El log de cada petición lo hace `UseSerilogRequestLogging`. EF Core **no** registra valores de parámetros (el registro de datos sensibles está desactivado por defecto), y no se registran cuerpos de petición ni cabeceras.

**`DbContext`.** `OtaLinkDbContext` (en `Infrastructure/Persistence`) se registra con `AddDbContext`, que lo deja **Scoped**: una instancia por petición, nunca Singleton. Usa:

- Npgsql como proveedor de PostgreSQL.
- Convención `snake_case` (`EFCore.NamingConventions`): las tablas y columnas se llamarán `usuarios`, `fecha_creacion`, y se consultan sin comillas en DBeaver.
- Historial de migraciones en la tabla `__ef_migrations_history`.
- `ApplyConfigurationsFromAssembly`: cada entidad se configura en su propia clase, descubierta automáticamente. Hoy no hay ninguna y por eso aparece la advertencia `No instantiatable types implementing IEntityTypeConfiguration` (inofensiva; desaparece con la primera entidad).

**Salud.** Dos *endpoints* distintos:

| Ruta | Qué comprueba | Para qué |
|---|---|---|
| `/health` | Que la aplicación está viva (no consulta nada externo) | Saber si el proceso responde |
| `/health/ready` | Además, que la base de datos responde (abre una conexión real) | Saber si puede atender peticiones |

Si la base falla, `/health/ready` responde `503` con el cuerpo `Unhealthy` y **nada más**. La causa real (con el código de error de Npgsql) queda solo en el registro del servidor. Hay una prueba automática que verifica que el cuerpo no filtra el host ni el nombre de usuario.

**Herramienta `dotnet-ef`.** Instalada como herramienta **local del repositorio** (`dotnet-tools.json`, versión fija 10.0.13), no global. Se usa para generar migraciones y scripts SQL; generar scripts **no se conecta a ninguna base**: el contexto de diseño (`DesignTimeDbContextFactory`) usa una cadena ficticia.

### 2.6 Cómo se cambia la base de datos: scripts SQL que ejecuta una persona (superado en parte: ver 2.8)

Decisión del equipo: **ninguna migración se aplica automáticamente**. EF Core es la fuente de verdad del modelo, pero cada cambio se entrega como un script SQL numerado en [deploy/postgres/migrations/](../deploy/postgres/migrations/README.md), que se **lee y se ejecuta a mano** desde DBeaver como el rol `otalink_migrator`.

Ventajas: cada cambio de esquema se revisa antes de ejecutarse; la API (rol `otalink_app`) no puede alterar el esquema; el mismo procedimiento sirve para producción, con copia de seguridad previa.

### 2.7 Desarrollar contra la base real de la VM

La base de datos de la VM solo escucha en `127.0.0.1:5432`. Para que la API que corre en Windows la use, hay un **túnel SSH** que conecta un puerto local con ese puerto interno, sin abrir nada en el firewall.

```
API en Windows ──► localhost:15432 ══(túnel SSH)══► 127.0.0.1:5432 en la VM (PostgreSQL)
```

Procedimiento (todo en WINDOWS, PowerShell):

1. **Túnel** (ventana que se queda abierta): `powershell -ExecutionPolicy Bypass -File deploy\scripts\dev-db-tunnel.ps1`
2. **Secreto** (una sola vez): copiar la contraseña de `otalink_app` desde la VM (`sudo less /etc/otalink/db-app-password`, doble clic sobre la palabra, `q` para salir) y ejecutar `powershell -ExecutionPolicy Bypass -File deploy\scripts\dev-set-db-secret.ps1`. El script la pide sin mostrarla y **rechaza valores con formato incorrecto**.
3. **API:** `dotnet run --project src\OtaLink.Api --launch-profile http -c Release`
4. **Comprobar:** `curl.exe -s -i http://127.0.0.1:5100/health/ready` → `200 Healthy`.

`-ExecutionPolicy Bypass` solo afecta a ese proceso: no se cambia la política de seguridad de Windows. Se usa únicamente con scripts del propio repositorio que se pueden leer.

**Resultado verificado:** `/health/ready` respondió `200 Healthy`: la API de Windows llegó, por el túnel, a la PostgreSQL de la VM con el rol limitado.

---

### 2.8 Etapa 3: reorganización de la arquitectura (vigente)

**Qué pasó.** Al llegar al primer módulo de negocio (perfiles), el desarrollador aclaró cómo quería la arquitectura de **todo** el proyecto. El esqueleto de las etapas 1 y 2 (dominio rico, repositorios, EF Core con migraciones) no encajaba, y era el momento más barato para cambiarlo: no se había ejecutado ningún script de tablas ni se había construido ningún módulo encima.

**Las reglas que dio:**

1. Flujo: **controladores → interfaces → servicios** (y de vuelta en sentido contrario).
2. **Sin repositorios.** Cada módulo tiene su servicio independiente, con las dependencias inyectadas directamente.
3. **La transaccionalidad vive en la base de datos**: nada de SQL de negocio en el código, solo procedimientos, funciones y triggers. Los scripts los revisa y ejecuta el desarrollador.
4. **Front:** solo `.razor` y `.razor.cs`; el `.cs` llama al controlador.
5. Base de datos organizada por **esquemas**: `configuracion`, `proceso`, `log`, `seguridad` y `smart` (tablas fuertes, sin dependencias).

**Cómo se mapearon a las capas que ya existían** (decisión del desarrollador: conservar las capas y solo reorganizar lo que contiene cada una):

| Capa | Antes | Ahora |
|---|---|---|
| `Application` | Casos de uso, repositorios | **DTOs e interfaces** de servicio, validación primaria |
| `Domain` | Reglas de negocio puras | **Todo lo de la base de datos**: entidades, `DbContext` y `OtaLinkRutinas` |
| `Infrastructure` | EF Core, repositorios | **Servicios** (uno por módulo) |
| `Api` | Minimal APIs | **Controllers** |
| `Contracts` | DTOs | Eliminado (sus DTOs pasan a `Application`) |
| `Web` | Referenciaba `Contracts` | Referencia `Application` (solo DTOs) |

**Piezas nuevas importantes:**

- **`OtaLinkRutinas`** (`Domain/Datos`): el **único** archivo que sabe cómo se invoca cada procedimiento o función. Los servicios llaman a sus métodos, sin SQL. Los procedimientos se invocan con `CALL`; las funciones, con una llamada de una línea que EF parametriza.
- **`OtaLinkDbContext` con `SaveChanges` bloqueado**: EF modela las entidades y lee los resultados de las funciones, pero cualquier intento de escribir con EF lanza `NotSupportedException`, para que nadie se salte las reglas de la base.
- **`SECURITY DEFINER`**: las rutinas se ejecutan con los permisos de su dueño, y la aplicación (`otalink_app`) **no tiene permiso sobre ninguna tabla**, solo `EXECUTE` sobre rutinas. Así no hay forma de saltarse una regla con un `SELECT` o un `INSERT` directo.
- **Reglas con números en `configuracion.parametros`**: edad mínima, longitudes y máximo de intereses se leen de una tabla, no están fijas dentro de cada procedimiento.
- **Auditoría por trigger** que omite datos sensibles (hash de contraseña, sellos de seguridad, fecha de nacimiento).
- **Validación en dos niveles:** primaria en los DTOs (forma del dato y reglas evidentes, para no viajar a la base con datos mal diligenciados) y **autoritativa en el procedimiento** (la que no se puede saltar). Los límites existen en dos sitios y deben mantenerse iguales.

**Qué se descartó y por qué:**

| Descartado | Razón |
|---|---|
| EF Core para escribir y generar migraciones | La transacción debe vivir en la base y los scripts los escribe y revisa una persona |
| Repositorios | Decisión explícita de arquitectura |
| Proyecto `Contracts` | Sus DTOs viven ahora en `Application` |
| `MapIdentityApi` (endpoints de Identity) | Se necesitan controladores; se usará `UserManager`/`SignInManager` con almacenes propios que llaman a procedimientos |
| Pruebas unitarias del dominio con objetos ricos | Las reglas viven en la base; las pruebas pasan a ser de validación primaria (DTOs) y de arquitectura, y más adelante pruebas contra la base real |

**Cómo se verificó:**

- Compila con **0 advertencias y 0 errores**; **62 pruebas correctas**.
- Una prueba comprueba, **sin base de datos**, que el modelo de EF coincide con los scripts: esquema, tabla y columnas de cada entidad (incluido el resultado de `fn_perfil_obtener`). Si un nombre cambia en un lado y no en el otro, falla.
- Las reglas de arquitectura detectan una violación deliberada (el front referenciando `Domain`), que luego se revirtió.

**Lección:** una instrucción de arquitectura dada a mitad de camino puede invalidar mucho código. Se resolvió **confirmando antes** cómo se había entendido, **respaldando** el estado anterior y **haciendo cumplir** las reglas nuevas con pruebas de arquitectura, de modo que no se puedan romper sin que fallen.

**Estado de los scripts SQL:** los nueve scripts (0001 a 0009) están escritos y **a la espera de que el desarrollador los revise y ejecute**. Hasta entonces la base no tiene ninguna tabla de OtaLink.

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
| 7 | `la ejecución de scripts está deshabilitada en este sistema` | Política de ejecución de PowerShell | No cambiar la política del sistema; usar `-ExecutionPolicy Bypass` solo para ese proceso y solo con scripts propios |
| 8 | `El argumento ... para el parámetro -File no existe` | PowerShell estaba en `C:\WINDOWS\system32`, y la ruta del script era relativa al repositorio | Hacer `cd` al repositorio antes, o usar la ruta completa |
| 9 | `/health/ready` daba `503` y el registro solo decía "No se pudo conectar" | `CanConnectAsync` de EF **oculta la excepción** y devuelve `false` | Abrir la conexión de verdad (`OpenConnectionAsync`) para que el error real llegue al log del servidor |
| 10 | `Format of the initialization string does not conform to specification starting at index 68` | La contraseña guardada en los secretos tenía un formato inválido (el pegado trajo caracteres de más o quedó incompleto) | El script de secretos ahora valida el formato y rechaza valores incorrectos. Contar posiciones ayuda: el índice 68 era justo el inicio de `Password=` |
| 11 | `dotnet build` fallaba con `MSB3026` (no se puede copiar `OtaLink.Api.exe`) | La API seguía ejecutándose y Windows bloquea su `.exe` | Detener la API (`Ctrl+C`) antes de recompilar |
| 12 | Advertencia `MSB3277`: dos versiones de `EntityFrameworkCore.Relational` (10.0.4 y 10.0.13) | Npgsql exige ≥ 10.0.4 y las herramientas de diseño traen 10.0.13 | `CentralPackageTransitivePinningEnabled` y fijar la versión en `Directory.Packages.props` |

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

**Hecho en la etapa 2:**

- [x] Configuración tipada con validación al arrancar (la API no inicia sin cadena de conexión)
- [x] Registros estructurados con Serilog
- [x] `OtaLinkDbContext` (Scoped) con Npgsql y `snake_case`
- [x] `dotnet-ef` como herramienta local, con scripts SQL numerados ejecutados a mano
- [x] `/health` y `/health/ready`; conexión real comprobada a la PostgreSQL de la VM por túnel SSH
- [x] 13 pruebas correctas, 0 advertencias

**Decisión tomada:** sin Docker; la base real es la de la VM. Los scripts SQL los ejecuta una persona desde DBeaver.

**Pendiente en esta fase:**

- [ ] Revisar y ejecutar los scripts SQL 0001 a 0009 en la base (los ejecuta el desarrollador)
- [ ] Servicios (`PerfilService`, `InteresService`, `CiudadService`) y sus controladores
- [ ] Autenticación: `UserManager`/`SignInManager` con almacenes propios que llaman a los procedimientos de `seguridad` (envío de correo real pendiente de elegir proveedor)
- [ ] Pruebas contra la base real de la VM (una base de pruebas aparte)
- [ ] Páginas de Blazor (`.razor` + `.razor.cs`) para registro, perfil e intereses
- [ ] Servicios `systemd` `otalink-api` y `otalink-web` y primer despliegue real en la VM
- [ ] Replicar en la Web la política de cabeceras de proxy
- [ ] Instalar y configurar MudBlazor en la Web
