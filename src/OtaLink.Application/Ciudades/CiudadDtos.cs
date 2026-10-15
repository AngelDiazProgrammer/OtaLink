namespace OtaLink.Application.Ciudades;

/// <summary>Una ciudad disponible.</summary>
public sealed record CiudadResponse(short Id, string Codigo, string Nombre);

public interface ICiudadService
{
    Task<IReadOnlyList<CiudadResponse>> ListarAsync(CancellationToken cancellationToken);
}
