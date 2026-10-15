using Microsoft.EntityFrameworkCore;

namespace OtaLink.Infrastructure.Persistence;

/// <summary>
/// Contexto de EF Core. Se registra como Scoped (uno por petición); nunca como Singleton.
/// Las entidades y sus configuraciones se añaden en las siguientes etapas.
/// </summary>
public sealed class OtaLinkDbContext(DbContextOptions<OtaLinkDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Aplica automáticamente todas las clases IEntityTypeConfiguration<T> de este ensamblado.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OtaLinkDbContext).Assembly);
    }
}
