namespace OtaLink.Infrastructure.Persistence;

/// <summary>
/// Dirección pública de la web. Se usa para armar los enlaces de los correos (confirmar cuenta,
/// restablecer contraseña). Sección "Web" de la configuración.
/// </summary>
public sealed class WebOptions
{
    public const string SectionName = "Web";

    public string UrlBase { get; set; } = string.Empty;
}
