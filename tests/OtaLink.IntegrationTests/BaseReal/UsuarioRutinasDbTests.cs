using Microsoft.Extensions.DependencyInjection;
using OtaLink.Domain.Datos;
using OtaLink.Domain.Entidades;

namespace OtaLink.IntegrationTests.BaseReal;

/// <summary>Verifica contra la base real los scripts 0006 y 0007 (seguridad.usuarios y sus rutinas).</summary>
public class UsuarioRutinasDbTests(BaseRealFixture fx) : IClassFixture<BaseRealFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<T> ConRutinasAsync<T>(Func<OtaLinkRutinas, Task<T>> accion)
    {
        using var alcance = fx.Requerir().Services.CreateScope();
        return await accion(alcance.ServiceProvider.GetRequiredService<OtaLinkRutinas>());
    }

    [Fact]
    public async Task Se_puede_buscar_un_usuario_por_id_por_nombre_y_por_correo()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        var normalizado = usuario.Correo.ToUpperInvariant();

        var porId = await ConRutinasAsync(r => r.UsuarioPorIdAsync(usuario.Id, Ct));
        var porNombre = await ConRutinasAsync(r => r.UsuarioPorNombreNormalizadoAsync(normalizado, Ct));
        var porCorreo = await ConRutinasAsync(r => r.UsuarioPorCorreoNormalizadoAsync(normalizado, Ct));

        Assert.Equal(usuario.Id, porId?.Id);
        Assert.Equal(usuario.Id, porNombre?.Id);
        Assert.Equal(usuario.Id, porCorreo?.Id);
        Assert.True(porId!.BloqueoHabilitado);
        Assert.False(porId.CorreoConfirmado);
        Assert.Equal(0, porId.IntentosFallidos);
    }

    [Fact]
    public async Task Buscar_un_usuario_que_no_existe_devuelve_nulo()
    {
        var usuario = await ConRutinasAsync(r => r.UsuarioPorIdAsync(Guid.NewGuid(), Ct));

        Assert.Null(usuario);
    }

    [Fact]
    public async Task Un_correo_repetido_se_rechaza_con_su_codigo()
    {
        await using var existente = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        var repetido = new Usuario
        {
            Id = Guid.NewGuid(),
            NombreUsuario = "otro-nombre@otalink.test",
            NombreUsuarioNormalizado = "OTRO-NOMBRE@OTALINK.TEST",
            Correo = existente.Correo,
            CorreoNormalizado = existente.Correo.ToUpperInvariant(),
            SelloSeguridad = "s",
            SelloConcurrencia = "c",
        };

        var resultado = await ConRutinasAsync(r => r.CrearUsuarioAsync(repetido, Ct));

        Assert.Equal("usuario.correo_duplicado", resultado.Codigo);
    }

    [Fact]
    public async Task Actualizar_con_el_sello_correcto_guarda_y_con_uno_viejo_avisa_del_conflicto()
    {
        await using var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);
        var actual = (await ConRutinasAsync(r => r.UsuarioPorIdAsync(usuario.Id, Ct)))!;
        var selloViejo = actual.SelloConcurrencia;

        actual.CorreoConfirmado = true;
        actual.IntentosFallidos = 3;
        actual.SelloConcurrencia = Guid.NewGuid().ToString();
        var primero = await ConRutinasAsync(r => r.ActualizarUsuarioAsync(actual, selloViejo, Ct));

        // Una segunda operación que todavía trae el sello anterior pisaría la primera: debe rechazarse.
        var segundo = await ConRutinasAsync(r => r.ActualizarUsuarioAsync(actual, selloViejo, Ct));

        Assert.True(primero.Exitoso, primero.Mensaje);
        Assert.Equal("usuario.conflicto_concurrencia", segundo.Codigo);

        var guardado = (await ConRutinasAsync(r => r.UsuarioPorIdAsync(usuario.Id, Ct)))!;
        Assert.True(guardado.CorreoConfirmado);
        Assert.Equal(3, guardado.IntentosFallidos);
    }

    [Fact]
    public async Task Actualizar_o_eliminar_un_usuario_inexistente_devuelve_no_encontrado()
    {
        var fantasma = new Usuario
        {
            Id = Guid.NewGuid(), NombreUsuario = "x", NombreUsuarioNormalizado = "X",
            Correo = "x", CorreoNormalizado = "X", SelloSeguridad = "s", SelloConcurrencia = "c",
        };

        var actualizar = await ConRutinasAsync(r => r.ActualizarUsuarioAsync(fantasma, "c", Ct));
        var eliminar = await ConRutinasAsync(r => r.EliminarUsuarioAsync(fantasma.Id, Ct));

        Assert.Equal("usuario.no_encontrado", actualizar.Codigo);
        Assert.Equal("usuario.no_encontrado", eliminar.Codigo);
    }

    [Fact]
    public async Task Eliminar_un_usuario_lo_borra()
    {
        var usuario = await UsuarioDePrueba.CrearAsync(fx.Requerir().Services);

        var eliminar = await ConRutinasAsync(r => r.EliminarUsuarioAsync(usuario.Id, Ct));
        var despues = await ConRutinasAsync(r => r.UsuarioPorIdAsync(usuario.Id, Ct));

        Assert.True(eliminar.Exitoso);
        Assert.Null(despues);
    }
}
