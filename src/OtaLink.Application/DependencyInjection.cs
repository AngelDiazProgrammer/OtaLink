using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace OtaLink.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra lo propio de Application. Aquí solo hay DTOs e interfaces: las implementaciones
    /// (servicios) las registra Infrastructure. Lo único compartido es el reloj, inyectable para pruebas.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
