using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OtaLink.Application.Perfiles;

namespace OtaLink.Api.Controllers;

/// <summary>
/// Perfil de la persona autenticada. La persona se identifica SIEMPRE por su sesión (el identificador
/// sale del token), nunca por un identificador enviado en la URL o el cuerpo: así nadie puede leer ni
/// modificar el perfil de otra persona manipulando identificadores.
/// </summary>
[Route("api/perfiles/me")]
[Authorize]
public sealed class PerfilesController(IPerfilService perfiles) : OtaLinkControllerBase
{
    [HttpPost]
    public async Task<ActionResult<PerfilResponse>> Crear(
        CrearPerfilRequest request, CancellationToken cancellationToken)
    {
        var resultado = await perfiles.CrearAsync(UsuarioId, request, cancellationToken);
        return Responder(resultado, perfil => CreatedAtAction(nameof(Obtener), perfil));
    }

    [HttpGet]
    public async Task<ActionResult<PerfilResponse>> Obtener(CancellationToken cancellationToken) =>
        Responder(await perfiles.ObtenerAsync(UsuarioId, cancellationToken));

    [HttpPut]
    public async Task<ActionResult<PerfilResponse>> ActualizarDatos(
        ActualizarPerfilRequest request, CancellationToken cancellationToken) =>
        Responder(await perfiles.ActualizarDatosAsync(UsuarioId, request, cancellationToken));

    [HttpPut("modos")]
    public async Task<ActionResult<PerfilResponse>> CambiarModos(
        CambiarModosRequest request, CancellationToken cancellationToken) =>
        Responder(await perfiles.CambiarModosAsync(UsuarioId, request, cancellationToken));

    [HttpPut("intereses")]
    public async Task<ActionResult<PerfilResponse>> EstablecerIntereses(
        EstablecerInteresesRequest request, CancellationToken cancellationToken) =>
        Responder(await perfiles.EstablecerInteresesAsync(UsuarioId, request, cancellationToken));
}
