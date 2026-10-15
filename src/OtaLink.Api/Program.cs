using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using OtaLink.Api.Errors;
using OtaLink.Application;
using OtaLink.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Inyección de dependencias: cada capa se registra con su propia extensión.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

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
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");

app.Run();

// Necesario para que las pruebas de integración (WebApplicationFactory) vean el punto de entrada.
public partial class Program;
