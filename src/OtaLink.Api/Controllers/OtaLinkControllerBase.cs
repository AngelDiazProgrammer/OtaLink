using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OtaLink.Application.Common;

namespace OtaLink.Api.Controllers;

/// <summary>
/// Base de los controladores. Convierte el <see cref="Result{T}"/> que devuelve un servicio en la
/// respuesta HTTP: éxito → 200; error → ProblemDetails con el código estable en la extensión "codigo".
/// </summary>
[ApiController]
public abstract class OtaLinkControllerBase : ControllerBase
{
    /// <summary>Identificador de la persona autenticada. Solo es válido en acciones protegidas con [Authorize].</summary>
    protected Guid UsuarioId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    protected ActionResult<T> Responder<T>(Result<T> resultado, Func<T, ActionResult<T>>? exito = null)
    {
        ArgumentNullException.ThrowIfNull(resultado);

        return resultado.IsSuccess
            ? (exito is null ? Ok(resultado.Value) : exito(resultado.Value))
            : ProblemaDe(resultado.Error);
    }

    protected ObjectResult ProblemaDe(AppError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var estado = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Locked => StatusCodes.Status423Locked,
            _ => StatusCodes.Status400BadRequest,
        };

        var problema = new ProblemDetails { Status = estado, Title = error.Message };
        problema.Extensions["codigo"] = error.Code;

        return new ObjectResult(problema) { StatusCode = estado };
    }
}
