using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OtaLink.Application.Autenticacion;
using OtaLink.Application.Common;
using OtaLink.Application.Correo;
using OtaLink.Domain.Entidades;
using OtaLink.Infrastructure.Persistence;

namespace OtaLink.Infrastructure.Servicios;

/// <summary>
/// Módulo de autenticación. La seguridad (hash de contraseñas, bloqueo de cuenta, tokens de un solo uso)
/// la hace ASP.NET Core Identity; los datos se guardan con los procedimientos de la base a través del
/// almacén propio (<c>UsuarioStore</c>). Este servicio solo orquesta el flujo.
///
/// Reglas de seguridad aplicadas:
///  - Registro, reenvío y olvido de contraseña responden SIEMPRE lo mismo, exista o no el correo.
///  - Al iniciar sesión, la contraseña se comprueba ANTES de revelar si el correo está confirmado.
///  - Un correo inexistente tarda lo mismo en rechazarse que uno real (se calcula un hash de relleno).
///  - Tras 5 intentos fallidos la cuenta se bloquea 15 minutos.
///  - Los enlaces llevan tokens de un solo uso ligados al sello de seguridad: cambiar la contraseña los invalida.
/// </summary>
internal sealed partial class AutenticacionService(
    UserManager<Usuario> usuarios,
    SignInManager<Usuario> sesiones,
    ICorreoSender correo,
    IOptions<WebOptions> web,
    IOptionsMonitor<BearerTokenOptions> bearer,
    TimeProvider reloj,
    ILogger<AutenticacionService> logger) : IAutenticacionService
{
    private static readonly AppError CodigoInvalido = new(
        "autenticacion.codigo_invalido", "El enlace no es válido o ya venció. Solicita uno nuevo.", ErrorType.Validation);

    private static readonly AppError CredencialesInvalidas = new(
        "autenticacion.credenciales_invalidas", "Correo o contraseña incorrectos.", ErrorType.Unauthorized);

    private static readonly AppError CuentaBloqueada = new(
        "autenticacion.cuenta_bloqueada", "Demasiados intentos fallidos. Intenta de nuevo en unos minutos.", ErrorType.Locked);

    private static readonly AppError CorreoNoConfirmado = new(
        "autenticacion.correo_no_confirmado", "Debes confirmar tu correo antes de iniciar sesión.", ErrorType.Forbidden);

    private static readonly AppError SesionInvalida = new(
        "autenticacion.sesion_invalida", "La sesión no es válida. Inicia sesión de nuevo.", ErrorType.Unauthorized);

    // ---------------------------------------------------------------- registro --

    public async Task<Result<MensajeResponse>> RegistrarAsync(RegistrarRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var direccion = request.Correo!.Trim();
        var usuario = new Usuario { Id = Guid.NewGuid(), NombreUsuario = direccion, Correo = direccion };

        // Identity valida la contraseña, comprueba que el correo no exista, calcula el hash y crea la cuenta.
        var resultado = await usuarios.CreateAsync(usuario, request.Contrasena!);

        if (!resultado.Succeeded)
        {
            if (resultado.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                // Anti-enumeración: el correo ya tiene cuenta, pero la respuesta es la misma que si fuera nuevo.
                LogRegistroConCorreoExistente(logger);
                return Result.Success(new MensajeResponse(MensajeRegistro));
            }

            return Result.Failure<MensajeResponse>(ErrorDeContrasena(resultado));
        }

        await EnviarConfirmacionAsync(usuario, cancellationToken);
        return Result.Success(new MensajeResponse(MensajeRegistro));
    }

    public async Task<Result<MensajeResponse>> ConfirmarCorreoAsync(ConfirmarCorreoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var usuario = await usuarios.FindByIdAsync(request.UsuarioId!.Value.ToString());
        if (usuario is null || !TryDecodificar(request.Codigo!, out var token))
        {
            return Result.Failure<MensajeResponse>(CodigoInvalido);
        }

        var resultado = await usuarios.ConfirmEmailAsync(usuario, token);
        return resultado.Succeeded
            ? Result.Success(new MensajeResponse("Tu correo quedó confirmado. Ya puedes iniciar sesión."))
            : Result.Failure<MensajeResponse>(CodigoInvalido);
    }

    public async Task<Result<MensajeResponse>> ReenviarConfirmacionAsync(
        ReenviarConfirmacionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var usuario = await usuarios.FindByEmailAsync(request.Correo!.Trim());
        if (usuario is { CorreoConfirmado: false })
        {
            await EnviarConfirmacionAsync(usuario, cancellationToken);
        }

        return Result.Success(new MensajeResponse(MensajeRegistro));
    }

    // ------------------------------------------------------------------ sesión --

    public async Task<Result<ClaimsPrincipal>> IniciarSesionAsync(IniciarSesionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contrasena = request.Contrasena!;
        var usuario = await usuarios.FindByEmailAsync(request.Correo!.Trim());

        if (usuario is null)
        {
            // Se hace el mismo trabajo costoso que con un correo real: así el tiempo de respuesta no delata
            // qué correos tienen cuenta.
            _ = usuarios.PasswordHasher.HashPassword(new Usuario(), contrasena);
            return Result.Failure<ClaimsPrincipal>(CredencialesInvalidas);
        }

        if (await usuarios.IsLockedOutAsync(usuario))
        {
            return Result.Failure<ClaimsPrincipal>(CuentaBloqueada);
        }

        if (!await usuarios.CheckPasswordAsync(usuario, contrasena))
        {
            await usuarios.AccessFailedAsync(usuario);
            return Result.Failure<ClaimsPrincipal>(
                await usuarios.IsLockedOutAsync(usuario) ? CuentaBloqueada : CredencialesInvalidas);
        }

        // Solo quien conoce la contraseña llega a saber que le falta confirmar el correo.
        if (!usuario.CorreoConfirmado)
        {
            return Result.Failure<ClaimsPrincipal>(CorreoNoConfirmado);
        }

        await usuarios.ResetAccessFailedCountAsync(usuario);
        return Result.Success(await sesiones.CreateUserPrincipalAsync(usuario));
    }

    public async Task<Result<ClaimsPrincipal>> RefrescarSesionAsync(RefrescarSesionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var protector = bearer.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket = protector.Unprotect(request.TokenRefresco);

        // Debe ser un token de refresco auténtico, vigente, y el sello de seguridad de la cuenta no debe haber
        // cambiado (cambiar la contraseña invalida todas las sesiones anteriores).
        if (ticket?.Properties.ExpiresUtc is not { } vence
            || reloj.GetUtcNow() >= vence
            || await sesiones.ValidateSecurityStampAsync(ticket.Principal) is not { } usuario
            || !usuario.CorreoConfirmado
            || await usuarios.IsLockedOutAsync(usuario))
        {
            return Result.Failure<ClaimsPrincipal>(SesionInvalida);
        }

        return Result.Success(await sesiones.CreateUserPrincipalAsync(usuario));
    }

    // ------------------------------------------------------------- contraseña --

    public async Task<Result<MensajeResponse>> OlvideContrasenaAsync(OlvideContrasenaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var usuario = await usuarios.FindByEmailAsync(request.Correo!.Trim());
        if (usuario is { CorreoConfirmado: true })
        {
            var codigo = Codificar(await usuarios.GeneratePasswordResetTokenAsync(usuario));
            var enlace = $"{UrlBase}/restablecer-contrasena?correo={Uri.EscapeDataString(usuario.Correo)}&codigo={codigo}";

            await EnviarSinFallarAsync(
                new MensajeCorreo(
                    usuario.Correo,
                    "Restablece tu contraseña de OtaLink",
                    $"Recibimos una solicitud para restablecer tu contraseña. Si fuiste tú, usa este enlace (vence en 1 día):\n\n{enlace}\n\nSi no fuiste tú, ignora este mensaje: tu contraseña no cambia."),
                cancellationToken);
        }

        return Result.Success(new MensajeResponse(
            "Si el correo tiene una cuenta confirmada, recibirás un mensaje para restablecer la contraseña."));
    }

    public async Task<Result<MensajeResponse>> RestablecerContrasenaAsync(
        RestablecerContrasenaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var usuario = await usuarios.FindByEmailAsync(request.Correo!.Trim());
        if (usuario is null || !TryDecodificar(request.Codigo!, out var token))
        {
            return Result.Failure<MensajeResponse>(CodigoInvalido);
        }

        var resultado = await usuarios.ResetPasswordAsync(usuario, token, request.NuevaContrasena!);
        if (resultado.Succeeded)
        {
            return Result.Success(new MensajeResponse("Tu contraseña se actualizó. Ya puedes iniciar sesión."));
        }

        return resultado.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken))
            ? Result.Failure<MensajeResponse>(CodigoInvalido)
            : Result.Failure<MensajeResponse>(ErrorDeContrasena(resultado));
    }

    // --------------------------------------------------------------- mecánica --

    private const string MensajeRegistro =
        "Si el correo es válido, recibirás un mensaje para confirmar tu cuenta.";

    private string UrlBase => web.Value.UrlBase.TrimEnd('/');

    private async Task EnviarConfirmacionAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        var codigo = Codificar(await usuarios.GenerateEmailConfirmationTokenAsync(usuario));
        var enlace = $"{UrlBase}/confirmar-correo?usuarioId={usuario.Id}&codigo={codigo}";

        await EnviarSinFallarAsync(
            new MensajeCorreo(
                usuario.Correo,
                "Confirma tu cuenta de OtaLink",
                $"¡Bienvenida o bienvenido a OtaLink! Para activar tu cuenta confirma tu correo con este enlace (vence en 1 día):\n\n{enlace}\n\nSi no creaste una cuenta, ignora este mensaje."),
            cancellationToken);
    }

    /// <summary>
    /// Un fallo al enviar el correo se registra pero no cambia la respuesta: si cambiara, la diferencia entre
    /// "falló" y "funcionó" revelaría qué correos tienen cuenta.
    /// </summary>
    private async Task EnviarSinFallarAsync(MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        try
        {
            await correo.EnviarAsync(mensaje, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogErrorDeEnvio(logger, ex);
        }
    }

    // Los tokens de Identity llevan caracteres que no sirven en una URL: se codifican en Base64 seguro para URL.
    private static string Codificar(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static bool TryDecodificar(string codigo, out string token)
    {
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(codigo));
            return true;
        }
        catch (FormatException)
        {
            token = string.Empty;
            return false;
        }
    }

    private static AppError ErrorDeContrasena(IdentityResult resultado) => new(
        "autenticacion.datos_invalidos",
        string.Join(" ", resultado.Errors.Select(e => e.Description)),
        ErrorType.Validation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Intento de registro con un correo que ya tiene cuenta (respuesta idéntica a un registro nuevo).")]
    private static partial void LogRegistroConCorreoExistente(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "No se pudo enviar el correo. La respuesta al cliente no cambia.")]
    private static partial void LogErrorDeEnvio(ILogger logger, Exception exception);
}
