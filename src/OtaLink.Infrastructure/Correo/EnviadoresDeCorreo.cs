using Microsoft.Extensions.Logging;
using OtaLink.Application.Correo;

namespace OtaLink.Infrastructure.Correo;

/// <summary>
/// SOLO DESARROLLO (Correo:Modo = "Registro"): en lugar de enviar el mensaje, lo escribe en el registro
/// para poder copiar el enlace de confirmación. Los mensajes llevan códigos secretos: nunca debe
/// usarse en producción.
/// </summary>
internal sealed partial class CorreoRegistroSender(ILogger<CorreoRegistroSender> logger) : ICorreoSender
{
    public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mensaje);

        LogMensaje(logger, mensaje.Destino, mensaje.Asunto, mensaje.Cuerpo);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[CORREO DE DESARROLLO] Para: {Destino} | Asunto: {Asunto}\n{Cuerpo}")]
    private static partial void LogMensaje(ILogger logger, string destino, string asunto, string cuerpo);
}

/// <summary>
/// Modo por defecto: el correo todavía no está configurado. Falla de forma explícita en lugar de
/// registrar códigos secretos en el log de producción o fingir que envió el mensaje.
/// </summary>
internal sealed class CorreoNoConfiguradoSender : ICorreoSender
{
    public Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "El envío de correo no está configurado. Defina 'Correo:Modo' (Registro solo en desarrollo; el envío real por SMTP está pendiente).");
}
