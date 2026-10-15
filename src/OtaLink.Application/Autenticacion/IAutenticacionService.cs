using System.Security.Claims;
using OtaLink.Application.Common;

namespace OtaLink.Application.Autenticacion;

/// <summary>
/// Autenticación y gestión de la cuenta. Lo implementa el servicio de Infrastructure con ASP.NET Core Identity.
///
/// Las operaciones que pueden revelar si un correo tiene cuenta (registro, reenvío y olvido de contraseña)
/// responden SIEMPRE lo mismo, exista o no el correo: así nadie puede averiguar quién tiene cuenta.
/// </summary>
public interface IAutenticacionService
{
    Task<Result<MensajeResponse>> RegistrarAsync(RegistrarRequest request, CancellationToken cancellationToken);

    Task<Result<MensajeResponse>> ConfirmarCorreoAsync(ConfirmarCorreoRequest request, CancellationToken cancellationToken);

    Task<Result<MensajeResponse>> ReenviarConfirmacionAsync(ReenviarConfirmacionRequest request, CancellationToken cancellationToken);

    /// <summary>Valida las credenciales y devuelve la identidad con la que el controlador emite los tokens.</summary>
    Task<Result<ClaimsPrincipal>> IniciarSesionAsync(IniciarSesionRequest request, CancellationToken cancellationToken);

    /// <summary>Valida el token de refresco y devuelve la identidad actualizada para emitir tokens nuevos.</summary>
    Task<Result<ClaimsPrincipal>> RefrescarSesionAsync(RefrescarSesionRequest request, CancellationToken cancellationToken);

    Task<Result<MensajeResponse>> OlvideContrasenaAsync(OlvideContrasenaRequest request, CancellationToken cancellationToken);

    Task<Result<MensajeResponse>> RestablecerContrasenaAsync(RestablecerContrasenaRequest request, CancellationToken cancellationToken);
}
