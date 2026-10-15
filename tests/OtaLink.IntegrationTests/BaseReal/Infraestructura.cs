using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtaLink.Domain.Datos;
using OtaLink.Domain.Entidades;

namespace OtaLink.IntegrationTests.BaseReal;

/// <summary>
/// Autenticación de pruebas: la cabecera X-Test-User con un Guid simula una sesión iniciada.
/// Solo existe en este proyecto de pruebas; la API real nunca la registra.
/// </summary>
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Esquema = "Prueba";
    public const string Cabecera = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Cabecera, out var usuario))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identidad = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, usuario.ToString())], Esquema);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identidad), Esquema);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// Arranca la API en memoria contra la base REAL de desarrollo (la de la VM, por el túnel SSH), con la
/// cadena de conexión de User Secrets, y con la autenticación sustituida por <see cref="TestAuthHandler"/>.
/// </summary>
public sealed class ApiConBaseRealFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureAppConfiguration((_, configuracion) =>
            configuracion.AddUserSecrets(typeof(Program).Assembly, optional: true));

        builder.ConfigureTestServices(servicios =>
            servicios
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.Esquema;
                    o.DefaultChallengeScheme = TestAuthHandler.Esquema;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Esquema, _ => { }));
    }
}

/// <summary>
/// Comprueba UNA vez que la base real responde. Si el túnel está cerrado o falta el secreto, las
/// pruebas que la necesitan se omiten (con el motivo) en lugar de fallar.
/// </summary>
public sealed class BaseRealFixture : IAsyncLifetime
{
    public ApiConBaseRealFactory? Factory { get; private set; }

    public string MotivoNoDisponible { get; private set; } = "No inicializada.";

    public ValueTask InitializeAsync()
    {
        var factory = new ApiConBaseRealFactory();
        try
        {
            using var alcance = factory.Services.CreateScope();
            var db = alcance.ServiceProvider.GetRequiredService<OtaLinkDbContext>();

            using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            db.Database.OpenConnection();
            db.Database.CloseConnection();
            limite.Token.ThrowIfCancellationRequested();

            Factory = factory;
        }
        catch (Exception ex)
        {
            MotivoNoDisponible =
                "No hay conexión con la base de desarrollo. Abre el túnel (deploy/scripts/dev-db-tunnel.ps1) " +
                $"y revisa el secreto Database:ConnectionString. Detalle: {ex.GetType().Name}.";
            factory.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    public ApiConBaseRealFactory Requerir()
    {
        Assert.SkipUnless(Factory is not null, MotivoNoDisponible);
        return Factory!;
    }

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}

/// <summary>Un usuario temporal en seguridad.usuarios que se borra al terminar (y su perfil, en cascada).</summary>
public sealed class UsuarioDePrueba : IAsyncDisposable
{
    private readonly IServiceProvider _servicios;

    private UsuarioDePrueba(IServiceProvider servicios, Guid id, string correo)
    {
        _servicios = servicios;
        Id = id;
        Correo = correo;
    }

    public Guid Id { get; }

    public string Correo { get; }

    public static async Task<UsuarioDePrueba> CrearAsync(IServiceProvider servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        var id = Guid.NewGuid();
        var correo = $"prueba-{id:N}@otalink.test";
        var usuario = new Usuario
        {
            Id = id,
            NombreUsuario = correo,
            NombreUsuarioNormalizado = correo.ToUpperInvariant(),
            Correo = correo,
            CorreoNormalizado = correo.ToUpperInvariant(),
            SelloSeguridad = Guid.NewGuid().ToString(),
            SelloConcurrencia = Guid.NewGuid().ToString(),
        };

        using var alcance = servicios.CreateScope();
        var resultado = await alcance.ServiceProvider.GetRequiredService<OtaLinkRutinas>()
            .CrearUsuarioAsync(usuario, TestContext.Current.CancellationToken);
        Assert.True(resultado.Exitoso, resultado.Mensaje);

        return new UsuarioDePrueba(servicios, id, correo);
    }

    public async ValueTask DisposeAsync()
    {
        using var alcance = _servicios.CreateScope();
        await alcance.ServiceProvider.GetRequiredService<OtaLinkRutinas>()
            .EliminarUsuarioAsync(Id, CancellationToken.None);
    }
}
