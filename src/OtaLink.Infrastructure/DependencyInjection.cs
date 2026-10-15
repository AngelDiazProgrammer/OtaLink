using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OtaLink.Infrastructure.Persistence;

namespace OtaLink.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registra la infraestructura (base de datos, repositorios, correo...).</summary>
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
            ConfigureNpgsql(options, database.ConnectionString);
        });

        return services;
    }

    internal static void ConfigureNpgsql(DbContextOptionsBuilder options, string connectionString)
    {
        options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            // Tablas y columnas en snake_case (usuarios, fecha_creacion...): más cómodo en SQL/DBeaver.
            .UseSnakeCaseNamingConvention();
    }
}
