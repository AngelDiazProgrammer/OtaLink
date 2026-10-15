using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OtaLink.Infrastructure.Persistence;

namespace OtaLink.Api.Health;

/// <summary>
/// Comprueba que la base de datos responde. Se expone en /health/ready.
/// Abre la conexión de verdad (CanConnectAsync de EF oculta la causa del fallo): si falla, la
/// excepción real queda en el log del servidor y el cliente solo recibe "Unhealthy", sin detalles.
/// </summary>
internal sealed class DatabaseHealthCheck(OtaLinkDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            await dbContext.Database.CloseConnectionAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("No se pudo conectar con la base de datos.", ex);
        }
    }
}
