using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OtaLink.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registra la infraestructura (base de datos, repositorios, correo...).</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Siguiente etapa: DbContext (Scoped, nunca Singleton) y repositorios.
        return services;
    }
}
