using System.ComponentModel.DataAnnotations;
using OtaLink.Application.Common;

namespace OtaLink.Application.Perfiles;

/// <summary>
/// Límites de los datos del perfil para la validación primaria. La regla autoritativa vive en
/// configuracion.parametros (ver script 0002); si se cambia allí, se actualiza aquí.
/// </summary>
public static class ReglasPerfil
{
    public const int NombreMinimo = 2;
    public const int NombreMaximo = 40;
    public const int BiografiaMaxima = 500;
    public const int InteresesMaximos = 20;
}

/// <summary>Datos para crear el perfil de la persona autenticada.</summary>
public sealed record CrearPerfilRequest : IValidatableObject
{
    [Required(ErrorMessage = "El nombre público es obligatorio.")]
    [StringLength(ReglasPerfil.NombreMaximo, MinimumLength = ReglasPerfil.NombreMinimo,
        ErrorMessage = "El nombre público debe tener entre 2 y 40 caracteres.")]
    public string? NombrePublico { get; init; }

    [Required(ErrorMessage = "La fecha de nacimiento es obligatoria.")]
    public DateOnly? FechaNacimiento { get; init; }

    [StringLength(ReglasPerfil.BiografiaMaxima, ErrorMessage = "La biografía no puede superar 500 caracteres.")]
    public string? Biografia { get; init; }

    [Required(ErrorMessage = "La ciudad es obligatoria.")]
    public string? CiudadCodigo { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        ArgumentNullException.ThrowIfNull(validationContext);

        if (FechaNacimiento is not { } fecha)
        {
            yield break;
        }

        // El reloj se puede sustituir en las pruebas; en producción es TimeProvider.System.
        var reloj = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var hoy = HoraNegocio.HoyEnColombia(reloj.GetUtcNow());

        if (!ReglasEdad.EsPlausible(fecha, hoy))
        {
            yield return new ValidationResult("La fecha de nacimiento no es válida.", [nameof(FechaNacimiento)]);
        }
        else if (!ReglasEdad.EsMayorDeEdad(fecha, hoy))
        {
            yield return new ValidationResult(
                $"OtaLink es solo para mayores de {ReglasEdad.EdadMinima} años.", [nameof(FechaNacimiento)]);
        }
    }
}

/// <summary>Cambia el nombre público y la biografía.</summary>
public sealed record ActualizarPerfilRequest
{
    [Required(ErrorMessage = "El nombre público es obligatorio.")]
    [StringLength(ReglasPerfil.NombreMaximo, MinimumLength = ReglasPerfil.NombreMinimo,
        ErrorMessage = "El nombre público debe tener entre 2 y 40 caracteres.")]
    public string? NombrePublico { get; init; }

    [StringLength(ReglasPerfil.BiografiaMaxima, ErrorMessage = "La biografía no puede superar 500 caracteres.")]
    public string? Biografia { get; init; }
}

/// <summary>Activa o desactiva cada modo (Amistad y Citas) de forma independiente.</summary>
public sealed record CambiarModosRequest
{
    [Required(ErrorMessage = "Indica si quieres el modo Amistad.")]
    public bool? ModoAmistad { get; init; }

    [Required(ErrorMessage = "Indica si quieres el modo Citas.")]
    public bool? ModoCitas { get; init; }
}

/// <summary>Reemplaza los intereses del perfil por la selección enviada.</summary>
public sealed record EstablecerInteresesRequest : IValidatableObject
{
    [Required(ErrorMessage = "Envía la lista de intereses (puede estar vacía).")]
    public int[]? InteresIds { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (InteresIds is null)
        {
            yield break;
        }

        if (InteresIds.Any(id => id <= 0))
        {
            yield return new ValidationResult("Alguno de los intereses no es válido.", [nameof(InteresIds)]);
        }

        // Se cuentan los DISTINTOS: enviar el mismo interés dos veces no ocupa un cupo extra.
        if (InteresIds.Distinct().Count() > ReglasPerfil.InteresesMaximos)
        {
            yield return new ValidationResult(
                $"Puedes elegir hasta {ReglasPerfil.InteresesMaximos} intereses.", [nameof(InteresIds)]);
        }
    }
}

/// <summary>Vista del propio perfil (incluye la fecha de nacimiento; las vistas públicas no la mostrarán).</summary>
public sealed record PerfilResponse(
    Guid UsuarioId,
    string NombrePublico,
    DateOnly FechaNacimiento,
    int Edad,
    string? Biografia,
    string CiudadCodigo,
    bool ModoAmistad,
    bool ModoCitas,
    IReadOnlyList<int> InteresIds,
    DateTimeOffset FechaCreacion,
    DateTimeOffset FechaActualizacion);
