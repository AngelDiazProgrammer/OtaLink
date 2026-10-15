using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OtaLink.Application.Perfiles;

namespace OtaLink.IntegrationTests.BaseReal;

/// <summary>Los controladores de punta a punta: petición HTTP → controller → servicio → procedimiento → base real.</summary>
public class PerfilesApiDbTests(BaseRealFixture fx) : IClassFixture<BaseRealFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly object PerfilValido = new
    {
        nombrePublico = "Sakura",
        fechaNacimiento = "1998-03-12",
        biografia = "Fan de Frieren",
        ciudadCodigo = "bogota",
    };

    private HttpClient ClienteDe(Guid? usuario)
    {
        var cliente = fx.Requerir().CreateClient();
        if (usuario is { } id)
        {
            cliente.DefaultRequestHeaders.Add(TestAuthHandler.Cabecera, id.ToString());
        }

        return cliente;
    }

    private static async Task<JsonElement> JsonDe(HttpResponseMessage respuesta) =>
        await respuesta.Content.ReadFromJsonAsync<JsonElement>(Ct);

    [Theory]
    [InlineData("/api/perfiles/me")]
    [InlineData("/api/intereses")]
    [InlineData("/api/ciudades")]
    public async Task Sin_sesion_todo_responde_401(string ruta)
    {
        using var cliente = ClienteDe(null);

        using var respuesta = await cliente.GetAsync(new Uri(ruta, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_catalogo_de_intereses_se_entrega_con_las_categorias_como_texto()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        using var cliente = ClienteDe(usuario.Id);

        using var respuesta = await cliente.GetAsync(new Uri("/api/intereses", UriKind.Relative), Ct);
        var json = await JsonDe(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(172, json.GetArrayLength());
        var isekai = json.EnumerateArray().Single(i => i.GetProperty("slug").GetString() == "isekai");
        Assert.Equal("GenerosDeAnime", isekai.GetProperty("categoria").GetString());
    }

    [Fact]
    public async Task Crear_el_perfil_responde_201_con_su_ubicacion_y_se_puede_consultar()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        using var cliente = ClienteDe(usuario.Id);

        using var creado = await cliente.PostAsJsonAsync(new Uri("/api/perfiles/me", UriKind.Relative), PerfilValido, Ct);
        using var consultado = await cliente.GetAsync(new Uri("/api/perfiles/me", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        Assert.EndsWith("/api/perfiles/me", creado.Headers.Location?.AbsolutePath, StringComparison.Ordinal);
        var perfil = await JsonDe(consultado);
        Assert.Equal(HttpStatusCode.OK, consultado.StatusCode);
        Assert.Equal("Sakura", perfil.GetProperty("nombrePublico").GetString());
        Assert.Equal("1998-03-12", perfil.GetProperty("fechaNacimiento").GetString());
        Assert.Equal(usuario.Id, perfil.GetProperty("usuarioId").GetGuid());
    }

    [Fact]
    public async Task Un_menor_de_edad_se_rechaza_con_400_antes_de_llegar_a_la_base()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        using var cliente = ClienteDe(usuario.Id);
        var menor = new { nombrePublico = "Sakura", fechaNacimiento = "2015-06-01", ciudadCodigo = "bogota" };

        using var respuesta = await cliente.PostAsJsonAsync(new Uri("/api/perfiles/me", UriKind.Relative), menor, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("mayores de 18", await respuesta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        using var alcance = fx.Requerir().Services.CreateScope();
        Assert.False(await alcance.ServiceProvider.GetRequiredService<IPerfilService>().ExisteAsync(usuario.Id, Ct));
    }

    [Fact]
    public async Task Crear_dos_veces_responde_409_con_el_codigo_estable()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        using var cliente = ClienteDe(usuario.Id);
        using var primero = await cliente.PostAsJsonAsync(new Uri("/api/perfiles/me", UriKind.Relative), PerfilValido, Ct);

        using var segundo = await cliente.PostAsJsonAsync(new Uri("/api/perfiles/me", UriKind.Relative), PerfilValido, Ct);
        var problema = await JsonDe(segundo);

        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.Equal("perfil.ya_existe", problema.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Consultar_sin_haber_creado_el_perfil_responde_404()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        using var cliente = ClienteDe(usuario.Id);

        using var respuesta = await cliente.GetAsync(new Uri("/api/perfiles/me", UriKind.Relative), Ct);
        var problema = await JsonDe(respuesta);

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Equal("perfil.no_encontrado", problema.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Editar_datos_modos_e_intereses_se_guarda_y_se_ve_al_consultar()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        using var cliente = ClienteDe(usuario.Id);
        using var _ = await cliente.PostAsJsonAsync(new Uri("/api/perfiles/me", UriKind.Relative), PerfilValido, Ct);

        var ids = (await cliente.GetFromJsonAsync<JsonElement[]>(new Uri("/api/intereses", UriKind.Relative), Ct))!
            .Take(3).Select(i => i.GetProperty("id").GetInt32()).ToArray();

        using var datos = await cliente.PutAsJsonAsync(new Uri("/api/perfiles/me", UriKind.Relative),
            new { nombrePublico = "Sakura-chan", biografia = "Nueva bio" }, Ct);
        using var modos = await cliente.PutAsJsonAsync(new Uri("/api/perfiles/me/modos", UriKind.Relative),
            new { modoAmistad = true, modoCitas = false }, Ct);
        using var elegidos = await cliente.PutAsJsonAsync(new Uri("/api/perfiles/me/intereses", UriKind.Relative),
            new { interesIds = ids }, Ct);
        var perfil = await JsonDe(await cliente.GetAsync(new Uri("/api/perfiles/me", UriKind.Relative), Ct));

        Assert.All(new[] { datos, modos, elegidos }, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal("Sakura-chan", perfil.GetProperty("nombrePublico").GetString());
        Assert.True(perfil.GetProperty("modoAmistad").GetBoolean());
        Assert.False(perfil.GetProperty("modoCitas").GetBoolean());
        Assert.Equal(ids.Order(), perfil.GetProperty("interesIds").EnumerateArray().Select(e => e.GetInt32()).Order());
    }

    [Fact]
    public async Task Una_persona_nunca_ve_ni_modifica_el_perfil_de_otra()
    {
        await using var ana = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await using var luis = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        using var clienteAna = ClienteDe(ana.Id);
        using var clienteLuis = ClienteDe(luis.Id);
        using var _ = await clienteAna.PostAsJsonAsync(new Uri("/api/perfiles/me", UriKind.Relative), PerfilValido, Ct);

        // No existe ninguna ruta con identificador de usuario: el perfil sale siempre de la sesión.
        using var delOtro = await clienteLuis.GetAsync(new Uri("/api/perfiles/me", UriKind.Relative), Ct);
        using var editarOtro = await clienteLuis.PutAsJsonAsync(new Uri("/api/perfiles/me", UriKind.Relative),
            new { nombrePublico = "Intruso" }, Ct);
        using var porId = await clienteLuis.GetAsync(new Uri($"/api/perfiles/{ana.Id}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, delOtro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, editarOtro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, porId.StatusCode);
        var perfilDeAna = await JsonDe(await clienteAna.GetAsync(new Uri("/api/perfiles/me", UriKind.Relative), Ct));
        Assert.Equal("Sakura", perfilDeAna.GetProperty("nombrePublico").GetString());
    }

    [Fact]
    public async Task Enviar_demasiados_intereses_responde_400_sin_ir_a_la_base()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        using var cliente = ClienteDe(usuario.Id);
        using var _ = await cliente.PostAsJsonAsync(new Uri("/api/perfiles/me", UriKind.Relative), PerfilValido, Ct);

        using var respuesta = await cliente.PutAsJsonAsync(new Uri("/api/perfiles/me/intereses", UriKind.Relative),
            new { interesIds = Enumerable.Range(1, 21).ToArray() }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("hasta 20", await respuesta.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
