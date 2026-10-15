namespace OtaLink.Application.Common;

/// <summary>
/// Hora de negocio. OtaLink empieza en Bogotá: Colombia usa UTC-5 todo el año (no tiene horario de
/// verano), así que un desfase fijo es exacto y no depende de la base de zonas horarias del sistema.
/// </summary>
public static class HoraNegocio
{
    public static readonly TimeSpan DesfaseColombia = TimeSpan.FromHours(-5);

    /// <summary>La fecha de hoy en Colombia para un instante UTC dado.</summary>
    public static DateOnly HoyEnColombia(DateTimeOffset ahoraUtc) =>
        DateOnly.FromDateTime(ahoraUtc.ToOffset(DesfaseColombia).DateTime);
}

/// <summary>
/// Reglas de edad para la VALIDACIÓN PRIMARIA en código (evita viajar a la base con datos
/// obviamente inválidos). La regla autoritativa vive en la base de datos (proceso.sp_perfil_crear,
/// con los valores de configuracion.parametros); ambas deben coincidir.
/// </summary>
public static class ReglasEdad
{
    public const int EdadMinima = 18;

    /// <summary>Límite de cordura para descartar fechas de nacimiento absurdas.</summary>
    public const int EdadMaximaPlausible = 120;

    /// <summary>
    /// El día en que la persona cumple <paramref name="anios"/> años. Quien nació un 29 de febrero
    /// cumple, en años no bisiestos, el 1 de marzo: criterio prudente (el más tardío), para no
    /// admitir a nadie un día antes de ser mayor de edad.
    /// </summary>
    public static DateOnly CumpleanosEnAnio(DateOnly fechaNacimiento, int anios)
    {
        var candidato = fechaNacimiento.AddYears(anios);
        var nacioEn29DeFebrero = fechaNacimiento is { Month: 2, Day: 29 };
        return nacioEn29DeFebrero && candidato is { Month: 2, Day: 28 }
            ? candidato.AddDays(1)
            : candidato;
    }

    /// <summary>Edad cumplida en la fecha <paramref name="hoy"/>.</summary>
    public static int EdadCumplida(DateOnly fechaNacimiento, DateOnly hoy)
    {
        var edad = hoy.Year - fechaNacimiento.Year;
        return hoy < CumpleanosEnAnio(fechaNacimiento, edad) ? edad - 1 : edad;
    }

    public static bool EsMayorDeEdad(DateOnly fechaNacimiento, DateOnly hoy) =>
        CumpleanosEnAnio(fechaNacimiento, EdadMinima) <= hoy;

    public static bool EsPlausible(DateOnly fechaNacimiento, DateOnly hoy) =>
        fechaNacimiento <= hoy && CumpleanosEnAnio(fechaNacimiento, EdadMaximaPlausible) > hoy;
}
