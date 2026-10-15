using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OtaLink.Application.Ciudades;
using OtaLink.Application.Intereses;
using OtaLink.Domain.Datos;

namespace OtaLink.IntegrationTests.BaseReal;

/// <summary>Verifica contra la base real los catálogos (0004, 0005) y el modelo de permisos (0001).</summary>
public class CatalogosYPermisosDbTests(BaseRealFixture fx) : IClassFixture<BaseRealFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task El_catalogo_tiene_los_172_intereses_en_las_9_categorias()
    {
        using var alcance = fx.Requerir().Services.CreateScope();

        var intereses = await alcance.ServiceProvider.GetRequiredService<IInteresService>().ListarAsync(Ct);

        Assert.Equal(172, intereses.Count);
        var porCategoria = intereses.GroupBy(i => i.Categoria).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(9, porCategoria.Count);
        Assert.Equal(28, porCategoria[CategoriaInteres.GenerosDeAnime]);
        Assert.Equal(41, porCategoria[CategoriaInteres.SeriesYFranquicias]);
        Assert.Equal(41, porCategoria[CategoriaInteres.Videojuegos]);
        Assert.Equal(13, porCategoria[CategoriaInteres.ComunidadYActividades]);
        Assert.Equal(intereses.Count, intereses.Select(i => i.Slug).Distinct().Count());
    }

    [Fact]
    public async Task El_catalogo_viene_ordenado_por_categoria_y_nombre()
    {
        using var alcance = fx.Requerir().Services.CreateScope();

        var intereses = await alcance.ServiceProvider.GetRequiredService<IInteresService>().ListarAsync(Ct);

        Assert.Equal(intereses.OrderBy(i => i.Categoria).Select(i => i.Categoria), intereses.Select(i => i.Categoria));
        Assert.Contains(intereses, i => i is { Slug: "isekai", Categoria: CategoriaInteres.GenerosDeAnime });
        Assert.Contains(intereses, i => i is { Slug: "cosplay", Categoria: CategoriaInteres.Cosplay });
    }

    [Fact]
    public async Task Bogota_es_la_unica_ciudad_disponible()
    {
        using var alcance = fx.Requerir().Services.CreateScope();

        var ciudades = await alcance.ServiceProvider.GetRequiredService<ICiudadService>().ListarAsync(Ct);

        var bogota = Assert.Single(ciudades);
        Assert.Equal("bogota", bogota.Codigo);
        Assert.Equal("Bogotá", bogota.Nombre);
    }

    // ---- Modelo de permisos: la aplicación solo ejecuta rutinas, nunca toca tablas ----

    [Theory]
    [InlineData("SELECT count(*) AS \"Value\" FROM seguridad.usuarios")]
    [InlineData("SELECT count(*) AS \"Value\" FROM proceso.perfiles")]
    [InlineData("SELECT count(*) AS \"Value\" FROM proceso.perfiles_intereses")]
    [InlineData("SELECT count(*) AS \"Value\" FROM smart.intereses")]
    [InlineData("SELECT count(*) AS \"Value\" FROM smart.ciudades")]
    public async Task La_aplicacion_no_puede_leer_las_tablas_directamente(string consulta)
    {
        using var alcance = fx.Requerir().Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<OtaLinkDbContext>();

        // Es una prueba de seguridad: se intenta a propósito lo que la aplicación NO debe poder hacer.
        var excepcion = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.SqlQueryRaw<long>(consulta).SingleAsync(Ct));

        var postgres = Assert.IsType<PostgresException>(excepcion is NpgsqlException ? excepcion : excepcion.InnerException ?? excepcion);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgres.SqlState);
    }

    [Theory]
    [InlineData("SELECT count(*) AS \"Value\" FROM log.auditoria")]
    [InlineData("SELECT count(*) AS \"Value\" FROM configuracion.parametros")]
    public async Task La_aplicacion_no_ve_los_esquemas_log_ni_configuracion(string consulta)
    {
        using var alcance = fx.Requerir().Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<OtaLinkDbContext>();

        var excepcion = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.SqlQueryRaw<long>(consulta).SingleAsync(Ct));

        var postgres = Assert.IsType<PostgresException>(excepcion is NpgsqlException ? excepcion : excepcion.InnerException ?? excepcion);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgres.SqlState);
    }

    [Fact]
    public async Task La_aplicacion_no_puede_escribir_en_las_tablas_directamente()
    {
        using var alcance = fx.Requerir().Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<OtaLinkDbContext>();

        var excepcion = await Assert.ThrowsAnyAsync<Exception>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE smart.intereses SET nombre = 'hackeado'", Ct));

        var postgres = Assert.IsType<PostgresException>(excepcion is NpgsqlException ? excepcion : excepcion.InnerException ?? excepcion);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgres.SqlState);
    }
}
