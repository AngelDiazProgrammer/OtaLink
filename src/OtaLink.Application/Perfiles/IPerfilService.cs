using OtaLink.Application.Common;

namespace OtaLink.Application.Perfiles;

/// <summary>Perfil de la persona autenticada. Lo implementa el servicio de Infrastructure.</summary>
public interface IPerfilService
{
    Task<Result<PerfilResponse>> CrearAsync(Guid usuarioId, CrearPerfilRequest request, CancellationToken cancellationToken);

    Task<Result<PerfilResponse>> ObtenerAsync(Guid usuarioId, CancellationToken cancellationToken);

    Task<Result<PerfilResponse>> ActualizarDatosAsync(Guid usuarioId, ActualizarPerfilRequest request, CancellationToken cancellationToken);

    Task<Result<PerfilResponse>> CambiarModosAsync(Guid usuarioId, CambiarModosRequest request, CancellationToken cancellationToken);

    Task<Result<PerfilResponse>> EstablecerInteresesAsync(Guid usuarioId, EstablecerInteresesRequest request, CancellationToken cancellationToken);

    /// <summary>Si la persona ya tiene perfil (y por tanto ya validó que es mayor de edad).</summary>
    Task<bool> ExisteAsync(Guid usuarioId, CancellationToken cancellationToken);
}
