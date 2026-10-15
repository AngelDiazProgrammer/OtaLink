using Microsoft.EntityFrameworkCore;
using OtaLink.Domain.Entidades;

namespace OtaLink.Domain.Datos;

/// <summary>
/// Contexto de EF Core. Modela las entidades de las tablas y sirve para LEER (las funciones de
/// la base devuelven filas que se mapean a estas entidades) y para abrir la conexión con la que
/// se llaman los procedimientos.
///
/// LAS ESCRITURAS NO SE HACEN CON EF: toda transacción vive en la base de datos, en procedimientos
/// almacenados (ver <see cref="OtaLinkRutinas"/>). Por eso <c>SaveChanges</c> está bloqueado: si
/// alguien lo llama por error, falla de inmediato en lugar de escribir saltándose las reglas de la base.
/// Se registra como Scoped (uno por petición); nunca como Singleton.
/// </summary>
public sealed class OtaLinkDbContext(DbContextOptions<OtaLinkDbContext> options) : DbContext(options)
{
    private const string EscrituraNoPermitida =
        "Las escrituras se hacen con procedimientos almacenados (OtaLinkRutinas), no con SaveChanges.";

    // seguridad
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    // smart
    public DbSet<Ciudad> Ciudades => Set<Ciudad>();

    public DbSet<Interes> Intereses => Set<Interes>();

    // proceso
    public DbSet<Perfil> Perfiles => Set<Perfil>();

    public DbSet<PerfilInteres> PerfilesIntereses => Set<PerfilInteres>();

    // Resultado de fn_perfil_obtener (no es una tabla).
    public DbSet<PerfilDetalleFila> PerfilesDetalle => Set<PerfilDetalleFila>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException(EscrituraNoPermitida);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(EscrituraNoPermitida);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Aplica automáticamente todas las clases IEntityTypeConfiguration<T> de este ensamblado.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OtaLinkDbContext).Assembly);
    }
}
