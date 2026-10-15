using OtaLink.Application.Common;
using OtaLink.Domain.Datos;

namespace OtaLink.Infrastructure.Servicios;

/// <summary>
/// Traduce el código de error que devuelve un procedimiento almacenado a un <see cref="AppError"/>
/// con su tipo (que el controlador convierte en 400, 404 o 409). Los códigos son los de los scripts SQL.
/// </summary>
internal static class ErroresRutina
{
    public static AppError Convertir(ResultadoRutina resultado)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        var mensaje = resultado.Mensaje ?? "No se pudo completar la operación.";
        return new AppError(resultado.Codigo, mensaje, TipoDe(resultado.Codigo));
    }

    private static ErrorType TipoDe(string codigo) => codigo switch
    {
        _ when codigo.EndsWith(".no_encontrado", StringComparison.Ordinal) => ErrorType.NotFound,
        _ when codigo.EndsWith(".ya_existe", StringComparison.Ordinal) => ErrorType.Conflict,
        _ when codigo.EndsWith("_duplicado", StringComparison.Ordinal) => ErrorType.Conflict,
        _ when codigo.EndsWith(".conflicto_concurrencia", StringComparison.Ordinal) => ErrorType.Conflict,
        _ => ErrorType.Validation,
    };
}
