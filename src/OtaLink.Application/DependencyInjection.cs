using Microsoft.Extensions.DependencyInjection;

namespace OtaLink.Application;

public static class DependencyInjection
{
    /// <summary>Registra los servicios de la capa de aplicación (casos de uso, validadores).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Los casos de uso se registran aquí, con su ciclo de vida explícito (normalmente Scoped).
        return services;
    }
}
