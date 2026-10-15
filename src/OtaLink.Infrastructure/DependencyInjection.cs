using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OtaLink.Application.Autenticacion;
using OtaLink.Application.Ciudades;
using OtaLink.Application.Correo;
using OtaLink.Application.Intereses;
using OtaLink.Application.Perfiles;
using OtaLink.Domain.Datos;
using OtaLink.Domain.Entidades;
using OtaLink.Infrastructure.Correo;
using OtaLink.Infrastructure.Identity;
using OtaLink.Infrastructure.Persistence;
using OtaLink.Infrastructure.Servicios;

namespace OtaLink.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registra la infraestructura: base de datos, identidad, correo y los servicios de cada módulo.</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Configuración tipada: si falta algo esencial, la aplicación NO arranca
        // (falla rápido y con un mensaje claro, en vez de fallar en la primera petición).
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                $"Falta la configuración '{DatabaseOptions.SectionName}:ConnectionString'.")
            .ValidateOnStart();

        services.AddOptions<WebOptions>()
            .Bind(configuration.GetSection(WebOptions.SectionName))
            .Validate(
                o => Uri.TryCreate(o.UrlBase, UriKind.Absolute, out _),
                $"Falta la configuración '{WebOptions.SectionName}:UrlBase' (la dirección pública de la web, con http:// o https://).")
            .ValidateOnStart();

        // AddDbContext registra el contexto como Scoped (el ciclo de vida correcto).
        services.AddDbContext<OtaLinkDbContext>((serviceProvider, options) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options
                .UseNpgsql(database.ConnectionString)
                // Tablas y columnas en snake_case (nombre_publico, fecha_creacion...): coincide con la base.
                .UseSnakeCaseNamingConvention();
        });

        // El archivo que llama a los procedimientos y funciones. Scoped: comparte el DbContext de la petición.
        services.AddScoped<OtaLinkRutinas>();

        AddIdentidad(services);
        AddCorreo(services, configuration);

        // Servicios de cada módulo: uno por funcionalidad, independientes entre sí. Scoped: comparten
        // el DbContext de la petición en curso. Los controladores solo ven las interfaces.
        services.AddScoped<IAutenticacionService, AutenticacionService>();
        services.AddScoped<ICiudadService, CiudadService>();
        services.AddScoped<IInteresService, InteresService>();
        services.AddScoped<IPerfilService, PerfilService>();

        return services;
    }

    /// <summary>
    /// ASP.NET Core Identity con almacén propio (procedimientos de la base). Identity aporta la seguridad
    /// (hash de contraseñas, bloqueo, tokens); no se inventa nada.
    /// </summary>
    private static void AddIdentidad(IServiceCollection services)
    {
        services
            .AddIdentityCore<Usuario>(opciones =>
            {
                // Se prioriza la longitud sobre la complejidad (recomendación de NIST).
                opciones.Password.RequiredLength = ReglasCredenciales.ContrasenaMinima;
                opciones.Password.RequireDigit = false;
                opciones.Password.RequireLowercase = false;
                opciones.Password.RequireUppercase = false;
                opciones.Password.RequireNonAlphanumeric = false;
                opciones.Password.RequiredUniqueChars = 4;

                opciones.User.RequireUniqueEmail = true;
                opciones.SignIn.RequireConfirmedEmail = true;

                // Contra la adivinanza de contraseñas: 5 fallos bloquean la cuenta 15 minutos.
                opciones.Lockout.AllowedForNewUsers = true;
                opciones.Lockout.MaxFailedAccessAttempts = 5;
                opciones.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddUserStore<UsuarioStore>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<DescriptorErroresEs>();

        // Los enlaces de confirmación y de restablecer contraseña valen 1 día.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(1));
    }

    /// <summary>Envío de correo: solo "Registro" (desarrollo). Sin configurar, falla de forma explícita.</summary>
    private static void AddCorreo(IServiceCollection services, IConfiguration configuration)
    {
        var modo = configuration["Correo:Modo"];

        if (string.Equals(modo, "Registro", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ICorreoSender, CorreoRegistroSender>();
        }
        else
        {
            services.AddSingleton<ICorreoSender, CorreoNoConfiguradoSender>();
        }
    }
}
