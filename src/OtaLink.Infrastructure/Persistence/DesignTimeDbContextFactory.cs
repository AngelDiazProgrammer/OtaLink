using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OtaLink.Infrastructure.Persistence;

/// <summary>
/// Solo lo usan las herramientas de EF (dotnet ef) para generar migraciones y scripts SQL.
/// Generar scripts NO se conecta a la base de datos, por eso basta una cadena ficticia.
/// Nunca contiene credenciales reales.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OtaLinkDbContext>
{
    private const string DummyConnection = "Host=localhost;Database=otalink_design;Username=design;Password=design";

    public OtaLinkDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<OtaLinkDbContext>();
        DependencyInjection.ConfigureNpgsql(builder, DummyConnection);
        return new OtaLinkDbContext(builder.Options);
    }
}
