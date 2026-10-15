using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OtaLink.Application.Autenticacion;

namespace OtaLink.Api.Controllers;

/// <summary>
/// Registro, confirmación de correo, inicio y refresco de sesión y recuperación de contraseña.
/// Es público (no exige sesión) pero está limitado por dirección IP para frenar la fuerza bruta y el spam.
/// </summary>
[Route("api/autenticacion")]
[AllowAnonymous]
[EnableRateLimiting(PoliticaLimite)]
public sealed class AutenticacionController(IAutenticacionService autenticacion) : OtaLinkControllerBase
{
    public const string PoliticaLimite = "autenticacion";

    [HttpPost("registro")]
    public async Task<ActionResult<MensajeResponse>> Registrar(RegistrarRequest request, CancellationToken cancellationToken)
    {
        var resultado = await autenticacion.RegistrarAsync(request, cancellationToken);
        return Responder(resultado, mensaje => Accepted(mensaje));
    }

    [HttpPost("confirmar-correo")]
    public async Task<ActionResult<MensajeResponse>> ConfirmarCorreo(
        ConfirmarCorreoRequest request, CancellationToken cancellationToken) =>
        Responder(await autenticacion.ConfirmarCorreoAsync(request, cancellationToken));

    [HttpPost("reenviar-confirmacion")]
    public async Task<ActionResult<MensajeResponse>> ReenviarConfirmacion(
        ReenviarConfirmacionRequest request, CancellationToken cancellationToken)
    {
        var resultado = await autenticacion.ReenviarConfirmacionAsync(request, cancellationToken);
        return Responder(resultado, mensaje => Accepted(mensaje));
    }

    /// <summary>Si las credenciales son correctas, responde con los tokens (acceso y refresco).</summary>
    [HttpPost("iniciar-sesion")]
    public async Task<IActionResult> IniciarSesion(IniciarSesionRequest request, CancellationToken cancellationToken)
    {
        var resultado = await autenticacion.IniciarSesionAsync(request, cancellationToken);

        // Los tokens los emite el esquema de portador de ASP.NET Core a partir de la identidad que devuelve el servicio.
        return resultado.IsSuccess
            ? SignIn(resultado.Value, IdentityConstants.BearerScheme)
            : ProblemaDe(resultado.Error);
    }

    /// <summary>Cambia un token de refresco válido por un par de tokens nuevo.</summary>
    [HttpPost("refrescar")]
    public async Task<IActionResult> Refrescar(RefrescarSesionRequest request, CancellationToken cancellationToken)
    {
        var resultado = await autenticacion.RefrescarSesionAsync(request, cancellationToken);

        return resultado.IsSuccess
            ? SignIn(resultado.Value, IdentityConstants.BearerScheme)
            : ProblemaDe(resultado.Error);
    }

    [HttpPost("olvide-contrasena")]
    public async Task<ActionResult<MensajeResponse>> OlvideContrasena(
        OlvideContrasenaRequest request, CancellationToken cancellationToken)
    {
        var resultado = await autenticacion.OlvideContrasenaAsync(request, cancellationToken);
        return Responder(resultado, mensaje => Accepted(mensaje));
    }

    [HttpPost("restablecer-contrasena")]
    public async Task<ActionResult<MensajeResponse>> RestablecerContrasena(
        RestablecerContrasenaRequest request, CancellationToken cancellationToken) =>
        Responder(await autenticacion.RestablecerContrasenaAsync(request, cancellationToken));
}
