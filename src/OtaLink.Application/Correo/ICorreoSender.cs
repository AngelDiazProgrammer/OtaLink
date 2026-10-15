namespace OtaLink.Application.Correo;

/// <summary>Un mensaje de correo electrónico.</summary>
public sealed record MensajeCorreo(string Destino, string Asunto, string Cuerpo);

/// <summary>
/// Envío de correo. En desarrollo el mensaje se escribe en el registro; el envío real por SMTP se
/// añadirá cuando se elija el proveedor. Mientras no esté configurado, en producción falla de forma explícita.
/// </summary>
public interface ICorreoSender
{
    Task EnviarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken);
}
