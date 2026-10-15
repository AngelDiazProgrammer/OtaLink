using OtaLink.Application.Common;
using OtaLink.Application.Perfiles;
using OtaLink.Domain.Datos;
using OtaLink.Domain.Entidades;

namespace OtaLink.Infrastructure.Servicios;

/// <summary>
/// Módulo de perfiles. No contiene reglas de negocio ni SQL: delega en los procedimientos de la base de
/// datos (proceso.sp_perfil_*), que son quienes validan y guardan en una sola transacción, y traduce
/// su respuesta. La validación primaria de los datos ya la hicieron los DTOs en el controlador.
/// </summary>
internal sealed class PerfilService(OtaLinkRutinas rutinas) : IPerfilService
{
    private static readonly AppError PerfilNoEncontrado =
        new("perfil.no_encontrado", "Todavía no has creado tu perfil.", ErrorType.NotFound);

    public async Task<Result<PerfilResponse>> CrearAsync(
        Guid usuarioId, CrearPerfilRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resultado = await rutinas.CrearPerfilAsync(
            usuarioId,
            request.NombrePublico,
            request.FechaNacimiento,
            request.Biografia,
            request.CiudadCodigo,
            cancellationToken);

        return resultado.Exitoso
            ? await ObtenerAsync(usuarioId, cancellationToken)
            : Result.Failure<PerfilResponse>(ErroresRutina.Convertir(resultado));
    }

    public async Task<Result<PerfilResponse>> ObtenerAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var fila = await rutinas.ObtenerPerfilAsync(usuarioId, cancellationToken);
        return fila is null
            ? Result.Failure<PerfilResponse>(PerfilNoEncontrado)
            : Result.Success(Mapear(fila));
    }

    public async Task<Result<PerfilResponse>> ActualizarDatosAsync(
        Guid usuarioId, ActualizarPerfilRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resultado = await rutinas.ActualizarDatosPerfilAsync(
            usuarioId, request.NombrePublico, request.Biografia, cancellationToken);

        return await ResponderAsync(usuarioId, resultado, cancellationToken);
    }

    public async Task<Result<PerfilResponse>> CambiarModosAsync(
        Guid usuarioId, CambiarModosRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resultado = await rutinas.CambiarModosPerfilAsync(
            usuarioId, request.ModoAmistad == true, request.ModoCitas == true, cancellationToken);

        return await ResponderAsync(usuarioId, resultado, cancellationToken);
    }

    public async Task<Result<PerfilResponse>> EstablecerInteresesAsync(
        Guid usuarioId, EstablecerInteresesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resultado = await rutinas.EstablecerInteresesPerfilAsync(
            usuarioId, request.InteresIds ?? [], cancellationToken);

        return await ResponderAsync(usuarioId, resultado, cancellationToken);
    }

    public Task<bool> ExisteAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        rutinas.ExistePerfilAsync(usuarioId, cancellationToken);

    private async Task<Result<PerfilResponse>> ResponderAsync(
        Guid usuarioId, ResultadoRutina resultado, CancellationToken cancellationToken) =>
        resultado.Exitoso
            ? await ObtenerAsync(usuarioId, cancellationToken)
            : Result.Failure<PerfilResponse>(ErroresRutina.Convertir(resultado));

    private static PerfilResponse Mapear(PerfilDetalleFila fila) => new(
        fila.UsuarioId,
        fila.NombrePublico,
        fila.FechaNacimiento,
        fila.Edad,
        fila.Biografia,
        fila.CiudadCodigo,
        fila.ModoAmistad,
        fila.ModoCitas,
        fila.InteresIds,
        fila.FechaCreacion,
        fila.FechaActualizacion);
}
