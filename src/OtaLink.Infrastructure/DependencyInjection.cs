using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OtaLink.Application.Ciudades;
using OtaLink.Application.Intereses;
using OtaLink.Application.Perfiles;
using OtaLink.Domain.Datos;
using OtaLink.Infrastructure.Persistence;
using OtaLink.Infrastructure.Servicios;

namespace OtaLink.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registra la infraestructura: conexión a la base de datos y los servicios de cada módulo.</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Configuración tipada: si falta la cadena de conexión, la aplicación NO arranca
        // (falla rápido y con un mensaje claro, en vez de fallar en la primera consulta).
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                $"Falta la configuración '{DatabaseOptions.SectionName}:ConnectionString'.")
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

        // Servicios de cada módulo: uno por funcionalidad, independientes entre sí. Scoped: comparten
        // el DbContext de la petición en curso. Los controladores solo ven las interfaces.
        services.AddScoped<ICiudadService, CiudadService>();
        services.AddScoped<IInteresService, InteresService>();
        services.AddScoped<IPerfilService, PerfilService>();

        return services;
    }
}
