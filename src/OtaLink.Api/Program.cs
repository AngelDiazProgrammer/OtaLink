using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using OtaLink.Api.Controllers;
using OtaLink.Api.Errors;
using OtaLink.Api.Health;
using OtaLink.Application;
using OtaLink.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Registros estructurados. La configuración (niveles, destinos) está en appsettings.json,
// sección "Serilog". En el servidor la salida de consola la recoge systemd (journalctl).
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Inyección de dependencias: cada capa se registra con su propia extensión.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Controladores de API (solo JSON). Sin vistas: no se usa AddControllersWithViews.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

// Autenticación por token de portador (bearer): el controlador de autenticación emite un token de acceso
// de vida corta y uno de refresco; los demás endpoints lo exigen con [Authorize].
builder.Services.AddAuthentication(IdentityConstants.BearerScheme).AddBearerToken(IdentityConstants.BearerScheme);
builder.Services.AddAuthorization();

// Los tokens se protegen con las llaves de Data Protection. El nombre de la aplicación fija el "espacio" de
// esas llaves. En el servidor las llaves deben guardarse en disco para que sobrevivan a un reinicio
// (pendiente al desplegar: de lo contrario, un reinicio cerraría todas las sesiones).
builder.Services.AddDataProtection().SetApplicationName("OtaLink");

// Límite de peticiones por dirección IP en los endpoints públicos de autenticación (frena fuerza bruta y spam).
// El límite sale de la configuración y se lee de forma diferida para poder cambiarlo en las pruebas.
builder.Services.AddRateLimiter(o => o.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
builder.Services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((opciones, configuracion) =>
{
    var porMinuto = configuracion.GetValue("RateLimit:AutenticacionPorMinuto", 20);
    opciones.AddPolicy(AutenticacionController.PoliticaLimite, contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = porMinuto, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// /health       -> la aplicación está viva (no consulta nada externo).
// /health/ready -> además, la base de datos responde.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// Nginx está en la misma máquina: solo se confía en las cabeceras X-Forwarded-* que
// vienen de la propia VM (loopback). Un cliente externo no puede falsear su IP.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();
app.MapHealthChecks("/health", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.Run();

// Necesario para que las pruebas de integración (WebApplicationFactory) vean el punto de entrada.
public partial class Program;
