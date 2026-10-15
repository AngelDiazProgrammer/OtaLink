using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

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
