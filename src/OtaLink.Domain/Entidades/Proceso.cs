namespace OtaLink.Domain.Entidades;

/// <summary>Tabla proceso.perfiles. Un perfil por usuario: comparte su identificador.</summary>
public sealed class Perfil
{
    public Guid UsuarioId { get; set; }

    public string NombrePublico { get; set; } = string.Empty;

    public DateOnly FechaNacimiento { get; set; }

    public string? Biografia { get; set; }

    public short CiudadId { get; set; }

    /// <summary>Modo Amistad. Independiente del modo Citas.</summary>
    public bool ModoAmistad { get; set; }

    /// <summary>Modo Citas. Independiente del modo Amistad.</summary>
    public bool ModoCitas { get; set; }

    public DateTimeOffset FechaCreacion { get; set; }

    public DateTimeOffset FechaActualizacion { get; set; }

    public ICollection<PerfilInteres> Intereses { get; } = [];
}

/// <summary>Tabla proceso.perfiles_intereses: intereses elegidos por cada perfil.</summary>
public sealed class PerfilInteres
{
    public Guid UsuarioId { get; set; }

    public int InteresId { get; set; }

    public DateTimeOffset FechaCreacion { get; set; }
}
