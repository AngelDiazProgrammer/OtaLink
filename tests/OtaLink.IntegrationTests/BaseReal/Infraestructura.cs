using System.Collections.Concurrent;
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
using OtaLink.Application.Correo;
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
/// Correos capturados en memoria en lugar de enviarse. Permite a una prueba leer el enlace de
/// confirmación o de restablecimiento como lo haría una persona.
/// </summary>
public sealed class BandejaDeCorreo : ICorreoSender
{
    private readonly ConcurrentQueue<MensajeCorreo> _mensajes = new();

    public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        _mensajes.Enqueue(mensaje);
        return Task.CompletedTask;
    }

    public IReadOnlyList<MensajeCorreo> Para(string destino) =>
        [.. _mensajes.Where(m => string.Equals(m.Destino, destino, StringComparison.OrdinalIgnoreCase))];

    public int Total => _mensajes.Count;
}

/// <summary>
/// Base de las fábricas: arranca la API en memoria contra la base REAL de desarrollo (la de la VM, por el
/// túnel SSH), con la cadena de conexión de User Secrets.
/// </summary>
public abstract class ApiRealBase : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureAppConfiguration((_, configuracion) =>
        {
            configuracion.AddUserSecrets(typeof(Program).Assembly, optional: true);

            // Permite apuntar las pruebas a OTRA base sin tocar los secretos (por ejemplo, una de pruebas,
            // o un puerto vacío para comprobar que las pruebas se omiten cuando no hay base). Se añade
            // DESPUÉS de los secretos, así que tiene prioridad sobre ellos.
            var conexion = Environment.GetEnvironmentVariable("OTALINK_TEST_CONNECTION");
            var ajustes = new Dictionary<string, string?>();
            if (!string.IsNullOrWhiteSpace(conexion))
            {
                ajustes["Database:ConnectionString"] = conexion;
            }

            ConfigurarAjustes(ajustes);
            configuracion.AddInMemoryCollection(ajustes);
        });

        ConfigurarServicios(builder);
    }

    /// <summary>Ajustes de configuración propios de cada fábrica (por ejemplo, el límite de peticiones).</summary>
    protected virtual void ConfigurarAjustes(Dictionary<string, string?> ajustes)
    {
    }

    protected abstract void ConfigurarServicios(IWebHostBuilder builder);
}

/// <summary>La API con la autenticación sustituida por <see cref="TestAuthHandler"/> (para probar perfiles, catálogos, etc.).</summary>
public sealed class ApiConBaseRealFactory : ApiRealBase
{
    protected override void ConfigurarServicios(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(servicios =>
            servicios
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.Esquema;
                    o.DefaultChallengeScheme = TestAuthHandler.Esquema;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Esquema, _ => { }));
}

/// <summary>
/// La API con la autenticación REAL (tokens de portador de verdad) y el correo capturado en una bandeja.
/// El límite de peticiones es muy alto para no estorbar a las pruebas funcionales.
/// </summary>
public class ApiConAutenticacionRealFactory : ApiRealBase
{
    public BandejaDeCorreo Bandeja { get; } = new();

    protected virtual int LimitePorMinuto => 100_000;

    protected override void ConfigurarAjustes(Dictionary<string, string?> ajustes) =>
        ajustes["RateLimit:AutenticacionPorMinuto"] = LimitePorMinuto.ToString(System.Globalization.CultureInfo.InvariantCulture);

    protected override void ConfigurarServicios(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(servicios =>
        {
            servicios.AddSingleton<ICorreoSender>(Bandeja);
        });
}

/// <summary>La misma API, pero con un límite de 3 peticiones por minuto, para probar el límite por IP.</summary>
public sealed class ApiConLimiteBajoFactory : ApiConAutenticacionRealFactory
{
    protected override int LimitePorMinuto => 3;
}

/// <summary>
/// Comprueba UNA vez que la base real responde. Si el túnel está cerrado o falta el secreto, las
/// pruebas que la necesitan se omiten (con el motivo) en lugar de fallar.
/// </summary>
public abstract class BaseRealFixtureBase<TFabrica> : IAsyncLifetime
    where TFabrica : ApiRealBase, new()
{
    public TFabrica? Factory { get; private set; }

    public string MotivoNoDisponible { get; private set; } = "No inicializada.";

    public ValueTask InitializeAsync()
    {
        var factory = new TFabrica();
        try
        {
            using var alcance = factory.Services.CreateScope();
            var db = alcance.ServiceProvider.GetRequiredService<OtaLinkDbContext>();

            db.Database.OpenConnection();
            db.Database.CloseConnection();

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

    public TFabrica Requerir()
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

        GC.SuppressFinalize(this);
    }
}

public sealed class BaseRealFixture : BaseRealFixtureBase<ApiConBaseRealFactory>;

public sealed class AutenticacionRealFixture : BaseRealFixtureBase<ApiConAutenticacionRealFactory>;

public sealed class LimiteBajoFixture : BaseRealFixtureBase<ApiConLimiteBajoFactory>;

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

    /// <summary>Borra una cuenta creada por la API (a partir de su correo). Se usa para limpiar tras las pruebas de registro.</summary>
    public static async Task EliminarPorCorreoAsync(IServiceProvider servicios, string correo)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        using var alcance = servicios.CreateScope();
        var rutinas = alcance.ServiceProvider.GetRequiredService<OtaLinkRutinas>();
        var usuario = await rutinas.UsuarioPorCorreoNormalizadoAsync(correo.ToUpperInvariant(), CancellationToken.None);
        if (usuario is not null)
        {
            await rutinas.EliminarUsuarioAsync(usuario.Id, CancellationToken.None);
        }
    }
}
