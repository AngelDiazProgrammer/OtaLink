namespace OtaLink.Domain.Entidades;

/// <summary>Tabla seguridad.usuarios. Cuenta de acceso gestionada por ASP.NET Core Identity.</summary>
public sealed class Usuario
{
    public Guid Id { get; set; }

    public string NombreUsuario { get; set; } = string.Empty;

    public string NombreUsuarioNormalizado { get; set; } = string.Empty;

    public string Correo { get; set; } = string.Empty;

    public string CorreoNormalizado { get; set; } = string.Empty;

    public bool CorreoConfirmado { get; set; }

    public string? HashContrasena { get; set; }

    /// <summary>Cambia al modificar credenciales: invalida sesiones y enlaces anteriores.</summary>
    public string SelloSeguridad { get; set; } = string.Empty;

    /// <summary>Control de concurrencia optimista.</summary>
    public string SelloConcurrencia { get; set; } = string.Empty;

    public DateTimeOffset? FinBloqueo { get; set; }

    public bool BloqueoHabilitado { get; set; } = true;

    public int IntentosFallidos { get; set; }

    public DateTimeOffset FechaCreacion { get; set; }

    public DateTimeOffset FechaActualizacion { get; set; }
}
