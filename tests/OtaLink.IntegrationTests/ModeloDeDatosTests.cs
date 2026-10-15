using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OtaLink.Domain.Datos;
using OtaLink.Domain.Entidades;

namespace OtaLink.IntegrationTests;

/// <summary>
/// Comprueba el modelo de EF contra lo que dicen los scripts SQL, sin conectarse a ninguna base.
/// Si un nombre de tabla, esquema o columna cambia en un lado y no en el otro, falla aquí.
/// </summary>
public class ModeloDeDatosTests
{
    private static OtaLinkDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<OtaLinkDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=modelo;Username=modelo;Password=modelo")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new OtaLinkDbContext(opciones);
    }

    [Theory]
    [InlineData(typeof(Usuario), "seguridad", "usuarios")]
    [InlineData(typeof(Ciudad), "smart", "ciudades")]
    [InlineData(typeof(Interes), "smart", "intereses")]
    [InlineData(typeof(Perfil), "proceso", "perfiles")]
    [InlineData(typeof(PerfilInteres), "proceso", "perfiles_intereses")]
    public void Cada_entidad_se_asigna_a_su_esquema_y_tabla(Type entidad, string esquema, string tabla)
    {
        using var db = CrearContexto();

        var tipo = db.Model.FindEntityType(entidad);

        Assert.NotNull(tipo);
        Assert.Equal(esquema, tipo.GetSchema());
        Assert.Equal(tabla, tipo.GetTableName());
    }

    [Fact]
    public void Las_columnas_salen_en_snake_case_y_coinciden_con_el_script_0006()
    {
        using var db = CrearContexto();
        var usuario = db.Model.FindEntityType(typeof(Usuario))!;
        var tabla = StoreObjectIdentifier.Table("usuarios", "seguridad");

        var columnas = usuario.GetProperties().Select(p => p.GetColumnName(tabla)).Order().ToArray();

        string[] esperadas =
        [
            "bloqueo_habilitado", "correo", "correo_confirmado", "correo_normalizado", "fecha_actualizacion",
            "fecha_creacion", "fin_bloqueo", "hash_contrasena", "id", "intentos_fallidos",
            "nombre_usuario", "nombre_usuario_normalizado", "sello_concurrencia", "sello_seguridad",
        ];
        Assert.Equal(esperadas, columnas);
    }

    [Fact]
    public void El_resultado_de_fn_perfil_obtener_no_es_una_tabla_y_sus_columnas_coinciden_con_el_script_0009()
    {
        using var db = CrearContexto();
        var fila = db.Model.FindEntityType(typeof(PerfilDetalleFila))!;

        Assert.True(fila.FindPrimaryKey() is null);
        Assert.Null(fila.GetTableName());

        // El nombre de columna por defecto de una consulta SQL es el de la propiedad en snake_case.
        var columnas = fila.GetProperties()
            .Select(p => p.GetColumnName(StoreObjectIdentifier.SqlQuery(fila)))
            .Order()
            .ToArray();

        string[] esperadas =
        [
            "biografia", "ciudad_codigo", "edad", "fecha_actualizacion", "fecha_creacion", "fecha_nacimiento",
            "interes_ids", "modo_amistad", "modo_citas", "nombre_publico", "usuario_id",
        ];
        Assert.Equal(esperadas, columnas);
    }

    [Fact]
    public void SaveChanges_esta_bloqueado_porque_las_escrituras_son_de_la_base()
    {
        using var db = CrearContexto();

        Assert.Throws<NotSupportedException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task SaveChangesAsync_tambien_esta_bloqueado()
    {
        await using var db = CrearContexto();

        await Assert.ThrowsAsync<NotSupportedException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }
}
