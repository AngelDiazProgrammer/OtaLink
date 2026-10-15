using System.Globalization;
using OtaLink.Application.Common;

namespace OtaLink.UnitTests;

public class ReglasEdadTests
{
    private static DateOnly Fecha(string valor) =>
        DateOnly.ParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2008-10-15", "2026-10-15", true)]   // cumple 18 hoy: ya es mayor de edad
    [InlineData("2008-10-16", "2026-10-15", false)]  // le falta un día
    [InlineData("2008-10-14", "2026-10-15", true)]   // cumplió ayer
    [InlineData("2000-01-01", "2026-10-15", true)]
    [InlineData("2015-06-01", "2026-10-15", false)]
    public void EsMayorDeEdad_respeta_el_dia_exacto_del_cumpleanos(string nacimiento, string hoy, bool esperado)
    {
        Assert.Equal(esperado, ReglasEdad.EsMayorDeEdad(Fecha(nacimiento), Fecha(hoy)));
    }

    [Fact]
    public void Nacido_un_29_de_febrero_cumple_18_el_1_de_marzo_en_anio_no_bisiesto()
    {
        var nacimiento = new DateOnly(2008, 2, 29); // 2026 no es bisiesto

        Assert.False(ReglasEdad.EsMayorDeEdad(nacimiento, new DateOnly(2026, 2, 28)));
        Assert.True(ReglasEdad.EsMayorDeEdad(nacimiento, new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void Cumpleanos_de_un_29_de_febrero_en_anio_bisiesto_es_el_29()
    {
        var nacimiento = new DateOnly(2004, 2, 29);

        Assert.Equal(new DateOnly(2022, 3, 1), ReglasEdad.CumpleanosEnAnio(nacimiento, 18)); // 2022 no es bisiesto
        Assert.Equal(new DateOnly(2028, 2, 29), ReglasEdad.CumpleanosEnAnio(nacimiento, 24)); // 2028 sí
    }

    [Theory]
    [InlineData("2026-10-16", false)] // en el futuro
    [InlineData("1890-01-01", false)] // más de 120 años
    [InlineData("1990-05-20", true)]
    public void EsPlausible_descarta_fechas_absurdas(string nacimiento, bool esperado)
    {
        Assert.Equal(esperado, ReglasEdad.EsPlausible(Fecha(nacimiento), new DateOnly(2026, 10, 15)));
    }

    [Theory]
    [InlineData("1998-03-12", "2026-10-15", 28)]
    [InlineData("1998-10-15", "2026-10-15", 28)] // cumple hoy
    [InlineData("1998-10-16", "2026-10-15", 27)] // cumple mañana
    [InlineData("2008-10-15", "2026-10-15", 18)]
    public void EdadCumplida_cuenta_los_anios_cumplidos(string nacimiento, string hoy, int esperada)
    {
        Assert.Equal(esperada, ReglasEdad.EdadCumplida(Fecha(nacimiento), Fecha(hoy)));
    }
}

public class HoraNegocioTests
{
    [Fact]
    public void A_las_03_UTC_en_Bogota_todavia_es_el_dia_anterior()
    {
        // 03:00 UTC del 1 de enero son las 22:00 del 31 de diciembre en Bogotá (UTC-5).
        var ahoraUtc = new DateTimeOffset(2027, 1, 1, 3, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 12, 31), HoraNegocio.HoyEnColombia(ahoraUtc));
    }

    [Fact]
    public void A_las_05_UTC_en_Bogota_ya_es_medianoche_del_nuevo_dia()
    {
        var ahoraUtc = new DateTimeOffset(2027, 1, 1, 5, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2027, 1, 1), HoraNegocio.HoyEnColombia(ahoraUtc));
    }
}
