using System.ComponentModel.DataAnnotations;
using OtaLink.Application.Perfiles;

namespace OtaLink.UnitTests;

/// <summary>
/// Validación PRIMARIA de los DTOs (lo que el controlador comprueba antes de ir a la base).
/// La regla autoritativa vive en los procedimientos de la base de datos.
/// </summary>
public class ValidacionDtosTests
{
    // 15 de octubre de 2026, 10:00 en Bogotá.
    private static readonly DateTimeOffset Ahora = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

    private static List<ValidationResult> Validar(object dto, DateTimeOffset? ahora = null)
    {
        var contexto = new ValidationContext(dto, new ProveedorDeServicios(new RelojFijo(ahora ?? Ahora)), null);
        var errores = new List<ValidationResult>();
        Validator.TryValidateObject(dto, contexto, errores, validateAllProperties: true);
        return errores;
    }

    private static CrearPerfilRequest PerfilValido() => new()
    {
        NombrePublico = "Sakura",
        FechaNacimiento = new DateOnly(1998, 3, 12),
        Biografia = "Fan de Frieren y de los JRPG",
        CiudadCodigo = "bogota",
    };

    // ---- CrearPerfilRequest ----

    [Fact]
    public void Un_perfil_valido_no_tiene_errores()
    {
        Assert.Empty(Validar(PerfilValido()));
    }

    [Fact]
    public void Acepta_un_nombre_con_caracteres_japoneses_y_emoji()
    {
        Assert.Empty(Validar(PerfilValido() with { NombrePublico = "さくら 🌸" }));
    }

    [Fact]
    public void Rechaza_a_un_menor_de_edad()
    {
        // Cumple 18 mañana (en hora de Bogotá).
        var errores = Validar(PerfilValido() with { FechaNacimiento = new DateOnly(2008, 10, 16) });

        var error = Assert.Single(errores);
        Assert.Contains("mayores de 18", error.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(nameof(CrearPerfilRequest.FechaNacimiento), error.MemberNames);
    }

    [Fact]
    public void Acepta_a_quien_cumple_18_hoy()
    {
        Assert.Empty(Validar(PerfilValido() with { FechaNacimiento = new DateOnly(2008, 10, 15) }));
    }

    [Fact]
    public void Usa_la_fecha_de_Bogota_y_no_la_de_UTC()
    {
        // 02:00 UTC del 16 de octubre = 21:00 del 15 en Bogotá: quien cumple 18 el día 16 todavía no los tiene.
        var tardeEnUtc = new DateTimeOffset(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);

        var errores = Validar(PerfilValido() with { FechaNacimiento = new DateOnly(2008, 10, 16) }, tardeEnUtc);

        Assert.Single(errores);
    }

    [Theory]
    [InlineData(2026, 10, 20)] // en el futuro
    [InlineData(1850, 1, 1)]   // más de 120 años
    public void Rechaza_fechas_de_nacimiento_imposibles(int anio, int mes, int dia)
    {
        var errores = Validar(PerfilValido() with { FechaNacimiento = new DateOnly(anio, mes, dia) });

        var error = Assert.Single(errores);
        Assert.Contains("no es válida", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    public void Rechaza_nombres_faltantes_o_demasiado_cortos(string? nombre)
    {
        Assert.NotEmpty(Validar(PerfilValido() with { NombrePublico = nombre }));
    }

    [Fact]
    public void Rechaza_un_nombre_de_mas_de_40_caracteres()
    {
        Assert.NotEmpty(Validar(PerfilValido() with { NombrePublico = new string('a', 41) }));
        Assert.Empty(Validar(PerfilValido() with { NombrePublico = new string('a', 40) }));
    }

    [Fact]
    public void Rechaza_una_biografia_de_mas_de_500_caracteres()
    {
        Assert.NotEmpty(Validar(PerfilValido() with { Biografia = new string('x', 501) }));
        Assert.Empty(Validar(PerfilValido() with { Biografia = new string('x', 500) }));
    }

    [Fact]
    public void La_biografia_es_opcional()
    {
        Assert.Empty(Validar(PerfilValido() with { Biografia = null }));
    }

    [Fact]
    public void Exige_fecha_de_nacimiento_y_ciudad()
    {
        var errores = Validar(PerfilValido() with { FechaNacimiento = null, CiudadCodigo = null });

        Assert.Equal(2, errores.Count);
    }

    // ---- ActualizarPerfilRequest ----

    [Fact]
    public void Actualizar_exige_un_nombre_valido()
    {
        Assert.Empty(Validar(new ActualizarPerfilRequest { NombrePublico = "Sakura-chan", Biografia = null }));
        Assert.NotEmpty(Validar(new ActualizarPerfilRequest { NombrePublico = "A" }));
    }

    // ---- CambiarModosRequest ----

    [Fact]
    public void Los_dos_modos_son_obligatorios_y_se_pueden_combinar_libremente()
    {
        Assert.Empty(Validar(new CambiarModosRequest { ModoAmistad = true, ModoCitas = false }));
        Assert.Empty(Validar(new CambiarModosRequest { ModoAmistad = false, ModoCitas = false }));
        Assert.NotEmpty(Validar(new CambiarModosRequest { ModoAmistad = true }));
    }

    // ---- EstablecerInteresesRequest ----

    [Fact]
    public void Acepta_hasta_20_intereses_distintos()
    {
        Assert.Empty(Validar(new EstablecerInteresesRequest { InteresIds = [.. Enumerable.Range(1, 20)] }));
    }

    [Fact]
    public void Rechaza_21_intereses_distintos()
    {
        var error = Assert.Single(Validar(new EstablecerInteresesRequest { InteresIds = [.. Enumerable.Range(1, 21)] }));

        Assert.Contains("hasta 20", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_repetidos_no_ocupan_cupo()
    {
        // 30 elementos, pero solo 20 distintos.
        var conRepetidos = Enumerable.Range(1, 20).Concat(Enumerable.Range(1, 10)).ToArray();

        Assert.Empty(Validar(new EstablecerInteresesRequest { InteresIds = conRepetidos }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Rechaza_identificadores_no_positivos(int identificador)
    {
        Assert.NotEmpty(Validar(new EstablecerInteresesRequest { InteresIds = [1, identificador] }));
    }

    [Fact]
    public void Permite_quedarse_sin_intereses_pero_exige_enviar_la_lista()
    {
        Assert.Empty(Validar(new EstablecerInteresesRequest { InteresIds = [] }));
        Assert.NotEmpty(Validar(new EstablecerInteresesRequest { InteresIds = null }));
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private sealed class ProveedorDeServicios(TimeProvider reloj) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(TimeProvider) ? reloj : null;
    }
}
