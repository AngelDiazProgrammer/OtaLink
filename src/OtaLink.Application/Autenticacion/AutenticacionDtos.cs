using System.ComponentModel.DataAnnotations;

namespace OtaLink.Application.Autenticacion;

/// <summary>Límites de las credenciales para la validación primaria (la política de contraseñas la aplica Identity).</summary>
public static class ReglasCredenciales
{
    /// <summary>Longitud mínima de la contraseña. Se prioriza la longitud sobre la complejidad.</summary>
    public const int ContrasenaMinima = 10;

    public const int ContrasenaMaxima = 128;

    public const int CorreoMaximo = 256;
}

/// <summary>Crea una cuenta nueva. Queda sin acceso hasta confirmar el correo.</summary>
public sealed record RegistrarRequest
{
    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    [StringLength(ReglasCredenciales.CorreoMaximo, ErrorMessage = "El correo es demasiado largo.")]
    public string? Correo { get; init; }

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [StringLength(ReglasCredenciales.ContrasenaMaxima, MinimumLength = ReglasCredenciales.ContrasenaMinima,
        ErrorMessage = "La contraseña debe tener entre 10 y 128 caracteres.")]
    public string? Contrasena { get; init; }
}

/// <summary>Confirma el correo con el código que llegó en el enlace.</summary>
public sealed record ConfirmarCorreoRequest
{
    [Required(ErrorMessage = "Falta el identificador de la cuenta.")]
    public Guid? UsuarioId { get; init; }

    [Required(ErrorMessage = "Falta el código de confirmación.")]
    public string? Codigo { get; init; }
}

/// <summary>Pide un nuevo mensaje de confirmación.</summary>
public sealed record ReenviarConfirmacionRequest
{
    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    [StringLength(ReglasCredenciales.CorreoMaximo)]
    public string? Correo { get; init; }
}

/// <summary>Inicia sesión con correo y contraseña.</summary>
public sealed record IniciarSesionRequest
{
    [Required(ErrorMessage = "El correo es obligatorio.")]
    [StringLength(ReglasCredenciales.CorreoMaximo)]
    public string? Correo { get; init; }

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [StringLength(ReglasCredenciales.ContrasenaMaxima)]
    public string? Contrasena { get; init; }
}

/// <summary>Obtiene una sesión nueva a partir del token de refresco.</summary>
public sealed record RefrescarSesionRequest
{
    [Required(ErrorMessage = "Falta el token de refresco.")]
    public string? TokenRefresco { get; init; }
}

/// <summary>Pide un enlace para restablecer la contraseña.</summary>
public sealed record OlvideContrasenaRequest
{
    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo no es válido.")]
    [StringLength(ReglasCredenciales.CorreoMaximo)]
    public string? Correo { get; init; }
}

/// <summary>Establece una contraseña nueva con el código que llegó en el enlace.</summary>
public sealed record RestablecerContrasenaRequest
{
    [Required(ErrorMessage = "El correo es obligatorio.")]
    [StringLength(ReglasCredenciales.CorreoMaximo)]
    public string? Correo { get; init; }

    [Required(ErrorMessage = "Falta el código.")]
    public string? Codigo { get; init; }

    [Required(ErrorMessage = "La contraseña nueva es obligatoria.")]
    [StringLength(ReglasCredenciales.ContrasenaMaxima, MinimumLength = ReglasCredenciales.ContrasenaMinima,
        ErrorMessage = "La contraseña debe tener entre 10 y 128 caracteres.")]
    public string? NuevaContrasena { get; init; }
}

/// <summary>Respuesta con un texto para mostrar a la persona.</summary>
public sealed record MensajeResponse(string Mensaje);
