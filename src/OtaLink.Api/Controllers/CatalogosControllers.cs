using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OtaLink.Application.Ciudades;
using OtaLink.Application.Intereses;

namespace OtaLink.Api.Controllers;

/// <summary>Ciudades disponibles.</summary>
[Route("api/ciudades")]
[Authorize]
public sealed class CiudadesController(ICiudadService ciudades) : OtaLinkControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CiudadResponse>>> Listar(CancellationToken cancellationToken) =>
        Ok(await ciudades.ListarAsync(cancellationToken));
}

/// <summary>Catálogo cerrado de intereses de la comunidad otaku.</summary>
[Route("api/intereses")]
[Authorize]
public sealed class InteresesController(IInteresService intereses) : OtaLinkControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InteresResponse>>> Listar(CancellationToken cancellationToken) =>
        Ok(await intereses.ListarAsync(cancellationToken));
}
