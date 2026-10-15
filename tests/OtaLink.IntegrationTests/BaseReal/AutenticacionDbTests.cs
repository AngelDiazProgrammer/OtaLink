using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using OtaLink.Application.Correo;
using OtaLink.Domain.Datos;

namespace OtaLink.IntegrationTests.BaseReal;

/// <summary>
/// El módulo de autenticación de punta a punta contra la base real y con tokens de verdad:
/// controlador → servicio → Identity → almacén propio → procedimientos de la base.
/// El correo no se envía: una bandeja en memoria lo captura para leer el enlace como lo haría una persona.
/// </summary>
public sealed partial class AutenticacionDbTests(AutenticacionRealFixture fx) : IClassFixture<AutenticacionRealFixture>, IAsyncLifetime
{
    private const string ContrasenaBuena = "una-contrasena-larga-1";
    private const string ContrasenaOtra = "otra-contrasena-larga-2";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<string> _correosCreados = [];

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        // Limpieza: se borran las cuentas que creó cada prueba.
        if (fx.Factory is { } fabrica)
        {
            foreach (var correo in _correosCreados)
            {
                await UsuarioDePrueba.EliminarPorCorreoAsync(fabrica.Services, correo);
            }
        }
    }

    // ------------------------------------------------------------------ ayudas --

    private HttpClient Cliente() => fx.Requerir().CreateClient();

    private BandejaDeCorreo Bandeja => fx.Requerir().Bandeja;

    private string CorreoNuevo()
    {
        var correo = $"auth-{Guid.NewGuid():N}@otalink.test";
        _correosCreados.Add(correo);
        return correo;
    }

    private static Task<HttpResponseMessage> Enviar(HttpClient cliente, string ruta, object cuerpo) =>
        cliente.PostAsJsonAsync(new Uri($"/api/autenticacion/{ruta}", UriKind.Relative), cuerpo, Ct);

    private static async Task<JsonElement> Json(HttpResponseMessage respuesta) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static Task<HttpResponseMessage> Registrar(HttpClient cliente, string correo, string contrasena = ContrasenaBuena) =>
        Enviar(cliente, "registro", new { correo, contrasena });

    private static Task<HttpResponseMessage> Iniciar(HttpClient cliente, string correo, string contrasena) =>
        Enviar(cliente, "iniciar-sesion", new { correo, contrasena });

    /// <summary>Lee el último correo enviado a una dirección y saca el código y el identificador del enlace.</summary>
    private (Guid? UsuarioId, string Codigo) EnlaceDe(string correo)
    {
        var mensajes = Bandeja.Para(correo);
        var mensaje = mensajes[^1];
        var codigo = Uri.UnescapeDataString(CodigoEnEnlace().Match(mensaje.Cuerpo).Groups[1].Value);
        var id = IdEnEnlace().Match(mensaje.Cuerpo);
        return (id.Success ? Guid.Parse(id.Groups[1].Value) : null, codigo);
    }

    private async Task RegistrarYConfirmarAsync(HttpClient cliente, string correo, string contrasena = ContrasenaBuena)
    {
        using var registro = await Registrar(cliente, correo, contrasena);
        Assert.Equal(HttpStatusCode.Accepted, registro.StatusCode);

        var (id, codigo) = EnlaceDe(correo);
        using var confirmacion = await Enviar(cliente, "confirmar-correo", new { usuarioId = id, codigo });
        Assert.Equal(HttpStatusCode.OK, confirmacion.StatusCode);
    }

    private static async Task<(string Acceso, string Refresco)> TokensDe(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = await Json(respuesta);
        return (json.GetProperty("accessToken").GetString()!, json.GetProperty("refreshToken").GetString()!);
    }

    [GeneratedRegex(@"codigo=([^&\s]+)")]
    private static partial Regex CodigoEnEnlace();

    [GeneratedRegex(@"usuarioId=([0-9a-fA-F-]{36})")]
    private static partial Regex IdEnEnlace();

    // ------------------------------------------------------------------ registro --

    [Fact]
    public async Task Registrar_responde_202_envia_el_correo_y_la_cuenta_queda_sin_confirmar()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();

        using var respuesta = await Registrar(cliente, correo);

        Assert.Equal(HttpStatusCode.Accepted, respuesta.StatusCode);
        var mensaje = Assert.Single(Bandeja.Para(correo));
        Assert.Contains("/confirmar-correo?usuarioId=", mensaje.Cuerpo, StringComparison.Ordinal);

        using var alcance = fx.Requerir().Services.CreateScope();
        var cuenta = await alcance.ServiceProvider.GetRequiredService<OtaLinkRutinas>()
            .UsuarioPorCorreoNormalizadoAsync(correo.ToUpperInvariant(), Ct);
        Assert.NotNull(cuenta);
        Assert.False(cuenta.CorreoConfirmado);
    }

    [Fact]
    public async Task La_contrasena_se_guarda_con_hash_y_nunca_en_claro()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        using var _ = await Registrar(cliente, correo);

        using var alcance = fx.Requerir().Services.CreateScope();
        var cuenta = await alcance.ServiceProvider.GetRequiredService<OtaLinkRutinas>()
            .UsuarioPorCorreoNormalizadoAsync(correo.ToUpperInvariant(), Ct);

        Assert.NotNull(cuenta?.HashContrasena);
        Assert.DoesNotContain(ContrasenaBuena, cuenta.HashContrasena, StringComparison.Ordinal);
        Assert.StartsWith("AQAAAA", cuenta.HashContrasena, StringComparison.Ordinal);   // formato PBKDF2 de Identity (v3)
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("123456789")]   // 9 caracteres: una menos que el mínimo
    public async Task Una_contrasena_demasiado_corta_se_rechaza_sin_crear_la_cuenta(string contrasena)
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();

        using var respuesta = await Registrar(cliente, correo, contrasena);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(Bandeja.Para(correo));
        using var alcance = fx.Requerir().Services.CreateScope();
        Assert.Null(await alcance.ServiceProvider.GetRequiredService<OtaLinkRutinas>()
            .UsuarioPorCorreoNormalizadoAsync(correo.ToUpperInvariant(), Ct));
    }

    [Theory]
    [InlineData("no-es-un-correo")]
    [InlineData("")]
    public async Task Un_correo_invalido_se_rechaza(string correo)
    {
        using var cliente = Cliente();

        using var respuesta = await Registrar(cliente, correo);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Registrar_un_correo_que_ya_existe_responde_exactamente_igual_y_no_envia_otro_mensaje()
    {
        // Anti-enumeración: nadie puede averiguar qué correos tienen cuenta mirando la respuesta.
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        using var primero = await Registrar(cliente, correo);

        using var repetido = await Registrar(cliente, correo, ContrasenaOtra);

        Assert.Equal(primero.StatusCode, repetido.StatusCode);
        Assert.Equal(await primero.Content.ReadAsStringAsync(Ct), await repetido.Content.ReadAsStringAsync(Ct));
        Assert.Single(Bandeja.Para(correo));
    }

    [Fact]
    public async Task El_correo_no_distingue_mayusculas_y_no_permite_duplicar_la_cuenta()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        using var _ = await Registrar(cliente, correo);

        using var repetido = await Registrar(cliente, correo.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Accepted, repetido.StatusCode);
        Assert.Single(Bandeja.Para(correo));
    }

    // -------------------------------------------------- confirmación del correo --

    [Fact]
    public async Task Sin_confirmar_el_correo_no_se_puede_entrar_pero_con_una_contrasena_incorrecta_no_se_revela()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        using var _ = await Registrar(cliente, correo);

        using var correcta = await Iniciar(cliente, correo, ContrasenaBuena);
        using var incorrecta = await Iniciar(cliente, correo, ContrasenaOtra);

        // Solo quien conoce la contraseña llega a saber que falta confirmar el correo.
        Assert.Equal(HttpStatusCode.Forbidden, correcta.StatusCode);
        Assert.Equal("autenticacion.correo_no_confirmado", (await Json(correcta)).GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, incorrecta.StatusCode);
    }

    [Fact]
    public async Task Confirmar_con_un_codigo_invalido_o_de_una_cuenta_inexistente_responde_lo_mismo()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        using var _ = await Registrar(cliente, correo);
        var (id, _codigoBueno) = EnlaceDe(correo);

        using var codigoMalo = await Enviar(cliente, "confirmar-correo", new { usuarioId = id, codigo = "Zm9v" });
        using var noBase64 = await Enviar(cliente, "confirmar-correo", new { usuarioId = id, codigo = "***no-es-base64***" });
        using var cuentaFantasma = await Enviar(cliente, "confirmar-correo", new { usuarioId = Guid.NewGuid(), codigo = _codigoBueno });

        foreach (var respuesta in new[] { codigoMalo, noBase64, cuentaFantasma })
        {
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
            Assert.Equal("autenticacion.codigo_invalido", (await Json(respuesta)).GetProperty("codigo").GetString());
        }
    }

    [Fact]
    public async Task Reenviar_la_confirmacion_responde_igual_exista_o_no_la_cuenta_y_solo_envia_si_corresponde()
    {
        using var cliente = Cliente();
        var sinConfirmar = CorreoNuevo();
        var confirmada = CorreoNuevo();
        var inexistente = CorreoNuevo();
        using var _ = await Registrar(cliente, sinConfirmar);
        await RegistrarYConfirmarAsync(cliente, confirmada);

        using var a = await Enviar(cliente, "reenviar-confirmacion", new { correo = sinConfirmar });
        using var b = await Enviar(cliente, "reenviar-confirmacion", new { correo = confirmada });
        using var c = await Enviar(cliente, "reenviar-confirmacion", new { correo = inexistente });

        Assert.All(new[] { a, b, c }, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Equal(await a.Content.ReadAsStringAsync(Ct), await c.Content.ReadAsStringAsync(Ct));
        Assert.Equal(2, Bandeja.Para(sinConfirmar).Count);   // el del registro y el reenviado
        Assert.Single(Bandeja.Para(confirmada));             // solo el del registro
        Assert.Empty(Bandeja.Para(inexistente));
    }

    // ------------------------------------------------------------ inicio de sesión --

    [Fact]
    public async Task Con_el_correo_confirmado_se_inicia_sesion_y_el_token_abre_los_endpoints_protegidos()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        await RegistrarYConfirmarAsync(cliente, correo);

        using var respuesta = await Iniciar(cliente, correo, ContrasenaBuena);
        var (acceso, refresco) = await TokensDe(respuesta);

        Assert.False(string.IsNullOrWhiteSpace(acceso));
        Assert.False(string.IsNullOrWhiteSpace(refresco));

        using var sinToken = await cliente.GetAsync(new Uri("/api/perfiles/me", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, sinToken.StatusCode);

        using var conToken = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/perfiles/me", UriKind.Relative));
        conToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acceso);
        using var autenticado = await cliente.SendAsync(conToken, Ct);

        // 404 y no 401: la sesión es válida, simplemente todavía no creó su perfil.
        Assert.Equal(HttpStatusCode.NotFound, autenticado.StatusCode);
    }

    [Fact]
    public async Task Un_token_inventado_o_manipulado_no_abre_nada()
    {
        using var cliente = Cliente();
        using var falso = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/perfiles/me", UriKind.Relative));
        falso.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "CfDJ8-esto-no-es-un-token-valido");

        using var respuesta = await cliente.SendAsync(falso, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_correo_inexistente_y_una_contrasena_incorrecta_responden_exactamente_lo_mismo()
    {
        using var cliente = Cliente();
        var existente = CorreoNuevo();
        await RegistrarYConfirmarAsync(cliente, existente);

        using var contrasenaMala = await Iniciar(cliente, existente, ContrasenaOtra);
        using var correoFantasma = await Iniciar(cliente, $"fantasma-{Guid.NewGuid():N}@otalink.test", ContrasenaBuena);

        Assert.Equal(HttpStatusCode.Unauthorized, contrasenaMala.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, correoFantasma.StatusCode);

        // Cada cuerpo se lee UNA sola vez; las dos respuestas deben ser indistinguibles.
        var problemaA = await Json(contrasenaMala);
        var problemaB = await Json(correoFantasma);
        Assert.Equal(problemaA.GetProperty("codigo").GetString(), problemaB.GetProperty("codigo").GetString());
        Assert.Equal(problemaA.GetProperty("title").GetString(), problemaB.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Tras_5_intentos_fallidos_la_cuenta_se_bloquea_incluso_con_la_contrasena_correcta()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        await RegistrarYConfirmarAsync(cliente, correo);

        var estados = new List<HttpStatusCode>();
        for (var intento = 1; intento <= 5; intento++)
        {
            using var respuesta = await Iniciar(cliente, correo, ContrasenaOtra);
            estados.Add(respuesta.StatusCode);
        }

        using var conLaCorrecta = await Iniciar(cliente, correo, ContrasenaBuena);

        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Locked],
            estados);
        Assert.Equal(HttpStatusCode.Locked, conLaCorrecta.StatusCode);
        Assert.Equal("autenticacion.cuenta_bloqueada", (await Json(conLaCorrecta)).GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Un_inicio_de_sesion_correcto_reinicia_el_contador_de_fallos()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        await RegistrarYConfirmarAsync(cliente, correo);

        for (var i = 0; i < 4; i++)
        {
            using var mala = await Iniciar(cliente, correo, ContrasenaOtra);
        }

        using var buena = await Iniciar(cliente, correo, ContrasenaBuena);
        using var otraMala = await Iniciar(cliente, correo, ContrasenaOtra);

        Assert.Equal(HttpStatusCode.OK, buena.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otraMala.StatusCode);   // no está bloqueada: el contador volvió a cero
    }

    // ------------------------------------------------------------------- refresco --

    [Fact]
    public async Task El_token_de_refresco_entrega_una_sesion_nueva()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        await RegistrarYConfirmarAsync(cliente, correo);
        var (accesoViejo, refresco) = await TokensDe(await Iniciar(cliente, correo, ContrasenaBuena));

        using var respuesta = await Enviar(cliente, "refrescar", new { tokenRefresco = refresco });
        var (accesoNuevo, _) = await TokensDe(respuesta);

        Assert.False(string.IsNullOrWhiteSpace(accesoNuevo));
        Assert.NotEqual(accesoViejo, accesoNuevo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("esto-no-es-un-token-de-refresco")]
    public async Task Un_token_de_refresco_invalido_se_rechaza(string tokenRefresco)
    {
        using var cliente = Cliente();

        using var respuesta = await Enviar(cliente, "refrescar", new { tokenRefresco });

        Assert.True(respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
    }

    // -------------------------------------------------------- recuperar contraseña --

    [Fact]
    public async Task Recuperar_la_contrasena_cambia_la_clave_el_enlace_sirve_una_sola_vez_y_cierra_las_sesiones_viejas()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        await RegistrarYConfirmarAsync(cliente, correo);
        var (_, refrescoViejo) = await TokensDe(await Iniciar(cliente, correo, ContrasenaBuena));

        using var olvido = await Enviar(cliente, "olvide-contrasena", new { correo });
        Assert.Equal(HttpStatusCode.Accepted, olvido.StatusCode);
        var (_, codigo) = EnlaceDe(correo);

        using var cambio = await Enviar(cliente, "restablecer-contrasena", new { correo, codigo, nuevaContrasena = ContrasenaOtra });
        using var segundoUso = await Enviar(cliente, "restablecer-contrasena", new { correo, codigo, nuevaContrasena = "una-tercera-contrasena-3" });
        using var conLaVieja = await Iniciar(cliente, correo, ContrasenaBuena);
        using var conLaNueva = await Iniciar(cliente, correo, ContrasenaOtra);
        using var refrescoDeLaSesionVieja = await Enviar(cliente, "refrescar", new { tokenRefresco = refrescoViejo });

        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, segundoUso.StatusCode);   // el enlace es de un solo uso
        Assert.Equal("autenticacion.codigo_invalido", (await Json(segundoUso)).GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, conLaVieja.StatusCode);
        Assert.Equal(HttpStatusCode.OK, conLaNueva.StatusCode);
        // Cambiar la contraseña invalida las sesiones anteriores (cambia el sello de seguridad).
        Assert.Equal(HttpStatusCode.Unauthorized, refrescoDeLaSesionVieja.StatusCode);
    }

    [Fact]
    public async Task Olvide_mi_contrasena_responde_igual_exista_o_no_y_solo_envia_a_cuentas_confirmadas()
    {
        using var cliente = Cliente();
        var confirmada = CorreoNuevo();
        var sinConfirmar = CorreoNuevo();
        var inexistente = CorreoNuevo();
        await RegistrarYConfirmarAsync(cliente, confirmada);
        using var _ = await Registrar(cliente, sinConfirmar);

        using var a = await Enviar(cliente, "olvide-contrasena", new { correo = confirmada });
        using var b = await Enviar(cliente, "olvide-contrasena", new { correo = sinConfirmar });
        using var c = await Enviar(cliente, "olvide-contrasena", new { correo = inexistente });

        Assert.All(new[] { a, b, c }, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Equal(await a.Content.ReadAsStringAsync(Ct), await c.Content.ReadAsStringAsync(Ct));
        Assert.Equal(2, Bandeja.Para(confirmada).Count);       // confirmación + restablecimiento
        Assert.Single(Bandeja.Para(sinConfirmar));             // solo la confirmación del registro
        Assert.Empty(Bandeja.Para(inexistente));
    }

    [Fact]
    public async Task Restablecer_con_un_codigo_falso_o_para_otra_cuenta_se_rechaza_sin_cambiar_nada()
    {
        using var cliente = Cliente();
        var victima = CorreoNuevo();
        var atacante = CorreoNuevo();
        await RegistrarYConfirmarAsync(cliente, victima);
        await RegistrarYConfirmarAsync(cliente, atacante);
        using var olvido = await Enviar(cliente, "olvide-contrasena", new { correo = atacante });
        var (_, codigoDelAtacante) = EnlaceDe(atacante);

        // El atacante usa SU código contra la cuenta de otra persona.
        using var cruzado = await Enviar(cliente, "restablecer-contrasena",
            new { correo = victima, codigo = codigoDelAtacante, nuevaContrasena = "contrasena-del-atacante-9" });
        using var inventado = await Enviar(cliente, "restablecer-contrasena",
            new { correo = victima, codigo = "Zm9vYmFy", nuevaContrasena = "contrasena-del-atacante-9" });
        using var entraConLaOriginal = await Iniciar(cliente, victima, ContrasenaBuena);

        Assert.Equal(HttpStatusCode.BadRequest, cruzado.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inventado.StatusCode);
        Assert.Equal(HttpStatusCode.OK, entraConLaOriginal.StatusCode);
    }

    [Fact]
    public async Task Restablecer_con_una_contrasena_demasiado_corta_se_rechaza()
    {
        using var cliente = Cliente();
        var correo = CorreoNuevo();
        await RegistrarYConfirmarAsync(cliente, correo);
        using var olvido = await Enviar(cliente, "olvide-contrasena", new { correo });
        var (_, codigo) = EnlaceDe(correo);

        using var respuesta = await Enviar(cliente, "restablecer-contrasena", new { correo, codigo, nuevaContrasena = "corta" });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }
}

/// <summary>El límite de peticiones por dirección IP en los endpoints públicos de autenticación.</summary>
public class LimiteDePeticionesDbTests(LimiteBajoFixture fx) : IClassFixture<LimiteBajoFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pasado_el_limite_por_minuto_la_API_responde_429()
    {
        using var cliente = fx.Requerir().CreateClient();
        var estados = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
        {
            using var respuesta = await cliente.PostAsJsonAsync(
                new Uri("/api/autenticacion/iniciar-sesion", UriKind.Relative),
                new { correo = "nadie@otalink.test", contrasena = "una-contrasena-larga-1" },
                Ct);
            estados.Add(respuesta.StatusCode);
        }

        // El límite de esta fábrica es 3 por minuto: las tres primeras llegan al servicio (401) y el resto se frena.
        Assert.Equal(
            [HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests],
            estados);
    }
}
