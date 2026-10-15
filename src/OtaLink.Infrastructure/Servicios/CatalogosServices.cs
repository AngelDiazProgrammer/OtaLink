using OtaLink.Application.Ciudades;
using OtaLink.Application.Intereses;
using OtaLink.Domain.Datos;

namespace OtaLink.Infrastructure.Servicios;

/// <summary>Módulo de ciudades. Consulta smart.fn_ciudades_listar a través de <see cref="OtaLinkRutinas"/>.</summary>
internal sealed class CiudadService(OtaLinkRutinas rutinas) : ICiudadService
{
    public async Task<IReadOnlyList<CiudadResponse>> ListarAsync(CancellationToken cancellationToken)
    {
        var ciudades = await rutinas.ListarCiudadesAsync(cancellationToken);
        return [.. ciudades.Select(c => new CiudadResponse(c.Id, c.Codigo, c.Nombre))];
    }
}

/// <summary>Módulo del catálogo de intereses. Consulta smart.fn_intereses_listar a través de <see cref="OtaLinkRutinas"/>.</summary>
internal sealed class InteresService(OtaLinkRutinas rutinas) : IInteresService
{
    public async Task<IReadOnlyList<InteresResponse>> ListarAsync(CancellationToken cancellationToken)
    {
        var intereses = await rutinas.ListarInteresesAsync(cancellationToken);
        return [.. intereses.Select(i => new InteresResponse(i.Id, i.Slug, i.Nombre, (CategoriaInteres)i.Categoria))];
    }
}
