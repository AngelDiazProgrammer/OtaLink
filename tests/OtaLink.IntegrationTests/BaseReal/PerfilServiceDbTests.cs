using Microsoft.Extensions.DependencyInjection;
using OtaLink.Application.Common;
using OtaLink.Application.Intereses;
using OtaLink.Application.Perfiles;

namespace OtaLink.IntegrationTests.BaseReal;

/// <summary>
/// Verifica contra la base real las REGLAS DE NEGOCIO de perfiles, que viven en los procedimientos
/// (script 0009). Se llama al servicio directamente, sin pasar por la validación de los DTOs, para
/// comprobar que la base rechaza por sí misma lo que un cliente malicioso intentaría enviar.
/// </summary>
public class PerfilServiceDbTests(BaseRealFixture fx) : IClassFixture<BaseRealFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Hoy => HoraNegocio.HoyEnColombia(DateTimeOffset.UtcNow);

    private static CrearPerfilRequest Valido() => new()
    {
        NombrePublico = "Sakura",
        FechaNacimiento = new DateOnly(1998, 3, 12),
        Biografia = "Fan de Frieren y de los JRPG",
        CiudadCodigo = "bogota",
    };

    private async Task<T> EnAlcanceAsync<T>(Func<IServiceProvider, Task<T>> accion)
    {
        using var alcance = fx.Requerir().Services.CreateScope();
        return await accion(alcance.ServiceProvider);
    }

    private Task<Result<PerfilResponse>> CrearAsync(Guid usuario, CrearPerfilRequest peticion) =>
        EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>().CrearAsync(usuario, peticion, Ct));

    private Task<Result<PerfilResponse>> EstablecerAsync(Guid usuario, params int[] ids) =>
        EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>()
            .EstablecerInteresesAsync(usuario, new EstablecerInteresesRequest { InteresIds = ids }, Ct));

    private async Task<int[]> IdsDelCatalogoAsync(int cantidad)
    {
        var todos = await EnAlcanceAsync(s => s.GetRequiredService<IInteresService>().ListarAsync(Ct));
        return [.. todos.Select(i => i.Id).Take(cantidad)];
    }

    // ---------------------------------------------------------------- crear ---

    [Fact]
    public async Task Crear_un_perfil_valido_lo_guarda_y_calcula_la_edad_en_la_base()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var resultado = await CrearAsync(usuario.Id, Valido());

        Assert.True(resultado.IsSuccess, resultado.IsFailure ? resultado.Error.Message : null);
        var perfil = resultado.Value;
        Assert.Equal("Sakura", perfil.NombrePublico);
        Assert.Equal("bogota", perfil.CiudadCodigo);
        Assert.False(perfil.ModoAmistad);   // los dos modos nacen desactivados
        Assert.False(perfil.ModoCitas);
        Assert.Empty(perfil.InteresIds);
        // La edad la calcula la base (fn_edad_cumplida); debe coincidir con la regla equivalente en C#.
        Assert.Equal(ReglasEdad.EdadCumplida(new DateOnly(1998, 3, 12), Hoy), perfil.Edad);
    }

    [Fact]
    public async Task Crear_quita_los_espacios_y_trata_una_biografia_en_blanco_como_vacia()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var resultado = await CrearAsync(usuario.Id, Valido() with { NombrePublico = "  Sakura  ", Biografia = "   " });

        Assert.True(resultado.IsSuccess);
        Assert.Equal("Sakura", resultado.Value.NombrePublico);
        Assert.Null(resultado.Value.Biografia);
    }

    [Fact]
    public async Task Crear_acepta_nombres_con_caracteres_japoneses_y_emoji()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var resultado = await CrearAsync(usuario.Id, Valido() with { NombrePublico = "さくら 🌸" });

        Assert.True(resultado.IsSuccess);
        Assert.Equal("さくら 🌸", resultado.Value.NombrePublico);
    }

    [Fact]
    public async Task Crear_dos_veces_devuelve_conflicto_y_conserva_el_primero()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());

        var segundo = await CrearAsync(usuario.Id, Valido() with { NombrePublico = "Otro nombre" });

        Assert.Equal("perfil.ya_existe", segundo.Error.Code);
        Assert.Equal(ErrorType.Conflict, segundo.Error.Type);
        var actual = await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>().ObtenerAsync(usuario.Id, Ct));
        Assert.Equal("Sakura", actual.Value.NombrePublico);
    }

    [Fact]
    public async Task Dos_peticiones_simultaneas_de_crear_solo_dejan_pasar_una()
    {
        // La transaccionalidad es de la base: aunque dos peticiones lleguen a la vez, la clave primaria
        // garantiza que solo una gana y la otra recibe 'ya existe', sin errores 500.
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var resultados = await Task.WhenAll(CrearAsync(usuario.Id, Valido()), CrearAsync(usuario.Id, Valido()));

        Assert.Single(resultados, r => r.IsSuccess);
        var perdedor = Assert.Single(resultados, r => r.IsFailure);
        Assert.Equal("perfil.ya_existe", perdedor.Error.Code);
    }

    [Fact]
    public async Task Crear_para_un_usuario_que_no_existe_se_rechaza()
    {
        var resultado = await CrearAsync(Guid.NewGuid(), Valido());

        Assert.Equal("perfil.usuario_invalido", resultado.Error.Code);
    }

    // ---------------------------------------------- reglas de la base (sin DTOs) ---

    [Fact]
    public async Task La_base_rechaza_a_un_menor_de_edad_aunque_el_cliente_se_salte_la_validacion()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        var cumple18Manana = Hoy.AddYears(-18).AddDays(1);

        var resultado = await CrearAsync(usuario.Id, Valido() with { FechaNacimiento = cumple18Manana });

        Assert.Equal("perfil.menor_de_edad", resultado.Error.Code);
        Assert.False(await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>().ExisteAsync(usuario.Id, Ct)));
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task La_regla_de_edad_en_SQL_coincide_con_la_de_C_en_los_dias_limite_de_los_18(int desplazamientoEnDias)
    {
        // Paridad entre las dos implementaciones de la regla (la de los DTOs y la del procedimiento).
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        var nacimiento = Hoy.AddYears(-18).AddDays(desplazamientoEnDias);

        var resultado = await CrearAsync(usuario.Id, Valido() with { FechaNacimiento = nacimiento });

        var esperadoAdulto = ReglasEdad.EsMayorDeEdad(nacimiento, Hoy);
        Assert.Equal(esperadoAdulto, resultado.IsSuccess);
        if (!esperadoAdulto)
        {
            Assert.Equal("perfil.menor_de_edad", resultado.Error.Code);
        }
    }

    [Theory]
    [InlineData(1)]    // mañana
    [InlineData(400)]  // en el futuro lejano
    public async Task La_base_rechaza_fechas_de_nacimiento_en_el_futuro(int diasEnElFuturo)
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var resultado = await CrearAsync(usuario.Id, Valido() with { FechaNacimiento = Hoy.AddDays(diasEnElFuturo) });

        Assert.Equal("perfil.fecha_nacimiento_invalida", resultado.Error.Code);
    }

    [Fact]
    public async Task La_base_rechaza_fechas_de_hace_mas_de_120_anios_y_la_ausencia_de_fecha()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var muyVieja = await CrearAsync(usuario.Id, Valido() with { FechaNacimiento = new DateOnly(1850, 1, 1) });
        var sinFecha = await CrearAsync(usuario.Id, Valido() with { FechaNacimiento = null });

        Assert.Equal("perfil.fecha_nacimiento_invalida", muyVieja.Error.Code);
        Assert.Equal("perfil.fecha_nacimiento_invalida", sinFecha.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("Con\tcontrol")]
    public async Task La_base_rechaza_nombres_invalidos(string? nombre)
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var resultado = await CrearAsync(usuario.Id, Valido() with { NombrePublico = nombre });

        Assert.Equal("perfil.nombre_invalido", resultado.Error.Code);
    }

    [Fact]
    public async Task La_base_acepta_un_nombre_de_40_caracteres_y_rechaza_uno_de_41()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var largo = await CrearAsync(usuario.Id, Valido() with { NombrePublico = new string('a', 41) });
        var limite = await CrearAsync(usuario.Id, Valido() with { NombrePublico = new string('a', 40) });

        Assert.Equal("perfil.nombre_invalido", largo.Error.Code);
        Assert.True(limite.IsSuccess);
    }

    [Fact]
    public async Task La_base_rechaza_una_biografia_de_mas_de_500_caracteres()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var resultado = await CrearAsync(usuario.Id, Valido() with { Biografia = new string('x', 501) });

        Assert.Equal("perfil.biografia_larga", resultado.Error.Code);
    }

    [Theory]
    [InlineData("medellin")]
    [InlineData("")]
    [InlineData(null)]
    public async Task La_base_rechaza_ciudades_que_no_estan_disponibles(string? ciudad)
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var resultado = await CrearAsync(usuario.Id, Valido() with { CiudadCodigo = ciudad });

        Assert.Equal("perfil.ciudad_invalida", resultado.Error.Code);
    }

    [Fact]
    public async Task El_codigo_de_la_ciudad_no_distingue_mayusculas_ni_espacios()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var resultado = await CrearAsync(usuario.Id, Valido() with { CiudadCodigo = "  BOGOTA " });

        Assert.True(resultado.IsSuccess);
        Assert.Equal("bogota", resultado.Value.CiudadCodigo);
    }

    // --------------------------------------------------------- consultar y editar ---

    [Fact]
    public async Task Sin_perfil_consultar_o_modificar_devuelve_no_encontrado()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        var ids = await IdsDelCatalogoAsync(1);

        var obtener = await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>().ObtenerAsync(usuario.Id, Ct));
        var datos = await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>()
            .ActualizarDatosAsync(usuario.Id, new ActualizarPerfilRequest { NombrePublico = "Sakura" }, Ct));
        var modos = await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>()
            .CambiarModosAsync(usuario.Id, new CambiarModosRequest { ModoAmistad = true, ModoCitas = true }, Ct));
        var intereses = await EstablecerAsync(usuario.Id, ids);

        foreach (var resultado in new[] { obtener, datos, modos, intereses })
        {
            Assert.Equal("perfil.no_encontrado", resultado.Error.Code);
            Assert.Equal(ErrorType.NotFound, resultado.Error.Type);
        }
    }

    [Fact]
    public async Task Actualizar_datos_cambia_nombre_y_biografia_y_la_fecha_de_actualizacion()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        var creado = (await CrearAsync(usuario.Id, Valido())).Value;

        var resultado = await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>().ActualizarDatosAsync(
            usuario.Id, new ActualizarPerfilRequest { NombrePublico = "Sakura-chan", Biografia = "Ahora con más anime" }, Ct));

        Assert.True(resultado.IsSuccess);
        Assert.Equal("Sakura-chan", resultado.Value.NombrePublico);
        Assert.Equal("Ahora con más anime", resultado.Value.Biografia);
        Assert.True(resultado.Value.FechaActualizacion > creado.FechaActualizacion);   // lo hace el trigger
        Assert.Equal(creado.FechaCreacion, resultado.Value.FechaCreacion);
    }

    [Fact]
    public async Task Actualizar_con_un_nombre_invalido_no_cambia_nada()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());

        var resultado = await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>()
            .ActualizarDatosAsync(usuario.Id, new ActualizarPerfilRequest { NombrePublico = "A" }, Ct));

        Assert.Equal("perfil.nombre_invalido", resultado.Error.Code);
        var actual = await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>().ObtenerAsync(usuario.Id, Ct));
        Assert.Equal("Sakura", actual.Value.NombrePublico);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Los_modos_Amistad_y_Citas_son_independientes(bool amistad, bool citas)
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());

        var resultado = await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>().CambiarModosAsync(
            usuario.Id, new CambiarModosRequest { ModoAmistad = amistad, ModoCitas = citas }, Ct));

        Assert.Equal(amistad, resultado.Value.ModoAmistad);
        Assert.Equal(citas, resultado.Value.ModoCitas);
    }

    // ------------------------------------------------------------------ intereses ---

    [Fact]
    public async Task Elegir_intereses_los_guarda_ordenados_y_sin_repetidos()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());
        var ids = await IdsDelCatalogoAsync(3);

        var resultado = await EstablecerAsync(usuario.Id, ids[2], ids[0], ids[2], ids[1], ids[0]);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(ids.Order(), resultado.Value.InteresIds);
    }

    [Fact]
    public async Task Elegir_de_nuevo_reemplaza_la_seleccion_aplicando_solo_la_diferencia()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());
        var ids = await IdsDelCatalogoAsync(4);
        await EstablecerAsync(usuario.Id, ids[0], ids[1], ids[2]);

        var resultado = await EstablecerAsync(usuario.Id, ids[1], ids[2], ids[3]);

        Assert.Equal(new[] { ids[1], ids[2], ids[3] }.Order(), resultado.Value.InteresIds);
    }

    [Fact]
    public async Task Se_puede_quedar_sin_intereses()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());
        await EstablecerAsync(usuario.Id, (await IdsDelCatalogoAsync(2)));

        var resultado = await EstablecerAsync(usuario.Id);

        Assert.True(resultado.IsSuccess);
        Assert.Empty(resultado.Value.InteresIds);
    }

    [Fact]
    public async Task Hasta_20_intereses_se_aceptan_y_con_21_se_rechaza_conservando_la_seleccion_anterior()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());
        var veintiuno = await IdsDelCatalogoAsync(21);

        var conVeinte = await EstablecerAsync(usuario.Id, veintiuno[..20]);
        var conVeintiuno = await EstablecerAsync(usuario.Id, veintiuno);

        Assert.True(conVeinte.IsSuccess);
        Assert.Equal(20, conVeinte.Value.InteresIds.Count);
        Assert.Equal("perfil.demasiados_intereses", conVeintiuno.Error.Code);

        // Atomicidad: el intento fallido no tocó la selección anterior.
        var actual = await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>().ObtenerAsync(usuario.Id, Ct));
        Assert.Equal(20, actual.Value.InteresIds.Count);
    }

    [Fact]
    public async Task Los_repetidos_no_ocupan_cupo()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());
        var veinte = await IdsDelCatalogoAsync(20);

        var resultado = await EstablecerAsync(usuario.Id, [.. veinte, .. veinte[..10]]);

        Assert.True(resultado.IsSuccess);
        Assert.Equal(20, resultado.Value.InteresIds.Count);
    }

    [Fact]
    public async Task Un_interes_que_no_existe_se_rechaza_y_no_cambia_la_seleccion()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());
        var ids = await IdsDelCatalogoAsync(2);
        await EstablecerAsync(usuario.Id, ids[0], ids[1]);

        var resultado = await EstablecerAsync(usuario.Id, ids[0], 99_999_999);

        Assert.Equal("perfil.interes_inexistente", resultado.Error.Code);
        var actual = await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>().ObtenerAsync(usuario.Id, Ct));
        Assert.Equal(ids.Order(), actual.Value.InteresIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task Los_identificadores_no_positivos_se_rechazan(int identificador)
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());

        var resultado = await EstablecerAsync(usuario.Id, 1, identificador);

        Assert.Equal("perfil.interes_invalido", resultado.Error.Code);
    }

    // -------------------------------------------------------- borrado en cascada ---

    [Fact]
    public async Task Al_eliminar_la_cuenta_se_elimina_su_perfil()
    {
        var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        await CrearAsync(usuario.Id, Valido());
        await EstablecerAsync(usuario.Id, await IdsDelCatalogoAsync(3));

        await usuario.DisposeAsync();

        Assert.False(await EnAlcanceAsync(s => s.GetRequiredService<IPerfilService>().ExisteAsync(usuario.Id, Ct)));
    }
}
