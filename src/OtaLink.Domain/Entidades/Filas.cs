namespace OtaLink.Domain.Entidades;

/// <summary>
/// Fila que devuelve la función proceso.fn_perfil_obtener: el perfil con la edad ya calculada,
/// el código de la ciudad y los identificadores de sus intereses. No es una tabla: es el
/// resultado de una consulta, por eso no tiene clave.
/// </summary>
public sealed class PerfilDetalleFila
{
    public Guid UsuarioId { get; set; }

    public string NombrePublico { get; set; } = string.Empty;

    public DateOnly FechaNacimiento { get; set; }

    public int Edad { get; set; }

    public string? Biografia { get; set; }

    public string CiudadCodigo { get; set; } = string.Empty;

    public bool ModoAmistad { get; set; }

    public bool ModoCitas { get; set; }

    public int[] InteresIds { get; set; } = [];

    public DateTimeOffset FechaCreacion { get; set; }

    public DateTimeOffset FechaActualizacion { get; set; }
}
