using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using OtaLink.Domain.Entidades;

namespace OtaLink.Domain.Datos;

/// <summary>Resultado de un procedimiento almacenado: 'ok' o un código de error estable.</summary>
public sealed record ResultadoRutina(string Codigo, string? Mensaje)
{
    public const string CodigoOk = "ok";

    public bool Exitoso => Codigo == CodigoOk;
}

/// <summary>
/// EL archivo dedicado a llamar a los procedimientos y funciones de la base de datos.
/// Separa las entidades (qué es cada tabla) de la transaccionalidad (qué se hace con ellas).
///
///   - Procedimientos (sp_*): ESCRIBEN. Cada llamada es una transacción completa dentro de la base.
///   - Funciones (fn_*): CONSULTAN. Devuelven filas que se mapean a las entidades.
///
/// Es el único lugar del código que conoce los nombres de las rutinas y cómo se invocan; los
/// servicios solo llaman a estos métodos. Los scripts de esas rutinas están en deploy/postgres/migrations.
/// </summary>
public sealed class OtaLinkRutinas(OtaLinkDbContext db)
{
    // ------------------------------------------------------------- seguridad --

    public Task<Usuario?> UsuarioPorIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Usuarios.FromSql($"SELECT * FROM seguridad.fn_usuario_por_id({id})")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    public Task<Usuario?> UsuarioPorNombreNormalizadoAsync(string nombreNormalizado, CancellationToken cancellationToken) =>
        db.Usuarios.FromSql($"SELECT * FROM seguridad.fn_usuario_por_nombre_normalizado({nombreNormalizado})")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    public Task<Usuario?> UsuarioPorCorreoNormalizadoAsync(string correoNormalizado, CancellationToken cancellationToken) =>
        db.Usuarios.FromSql($"SELECT * FROM seguridad.fn_usuario_por_correo_normalizado({correoNormalizado})")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    public Task<ResultadoRutina> CrearUsuarioAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        return LlamarAsync(
            "seguridad.sp_usuario_crear",
            [
                Uuid(usuario.Id),
                Texto(usuario.NombreUsuario),
                Texto(usuario.NombreUsuarioNormalizado),
                Texto(usuario.Correo),
                Texto(usuario.CorreoNormalizado),
                Texto(usuario.HashContrasena),
                Texto(usuario.SelloSeguridad),
                Texto(usuario.SelloConcurrencia),
                Booleano(usuario.BloqueoHabilitado),
            ],
            cancellationToken);
    }

    /// <summary>Actualiza la cuenta solo si <paramref name="selloConcurrenciaActual"/> sigue siendo el guardado (concurrencia optimista).</summary>
    public Task<ResultadoRutina> ActualizarUsuarioAsync(
        Usuario usuario, string selloConcurrenciaActual, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        return LlamarAsync(
            "seguridad.sp_usuario_actualizar",
            [
                Uuid(usuario.Id),
                Texto(selloConcurrenciaActual),
                Texto(usuario.NombreUsuario),
                Texto(usuario.NombreUsuarioNormalizado),
                Texto(usuario.Correo),
                Texto(usuario.CorreoNormalizado),
                Booleano(usuario.CorreoConfirmado),
                Texto(usuario.HashContrasena),
                Texto(usuario.SelloSeguridad),
                Texto(usuario.SelloConcurrencia),
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)usuario.FinBloqueo ?? DBNull.Value },
                Booleano(usuario.BloqueoHabilitado),
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = usuario.IntentosFallidos },
            ],
            cancellationToken);
    }

    public Task<ResultadoRutina> EliminarUsuarioAsync(Guid id, CancellationToken cancellationToken) =>
        LlamarAsync("seguridad.sp_usuario_eliminar", [Uuid(id)], cancellationToken);

    // ----------------------------------------------------------------- smart --

    public async Task<IReadOnlyList<Ciudad>> ListarCiudadesAsync(CancellationToken cancellationToken) =>
        await db.Ciudades.FromSql($"SELECT * FROM smart.fn_ciudades_listar()")
            .AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Interes>> ListarInteresesAsync(CancellationToken cancellationToken) =>
        await db.Intereses.FromSql($"SELECT * FROM smart.fn_intereses_listar()")
            .AsNoTracking().ToListAsync(cancellationToken);

    // --------------------------------------------------------------- proceso --

    public Task<bool> ExistePerfilAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<bool>($"SELECT proceso.fn_perfil_existe({usuarioId}) AS \"Value\"")
            .SingleAsync(cancellationToken);

    public Task<PerfilDetalleFila?> ObtenerPerfilAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        db.PerfilesDetalle.FromSql($"SELECT * FROM proceso.fn_perfil_obtener({usuarioId})")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    public Task<ResultadoRutina> CrearPerfilAsync(
        Guid usuarioId,
        string? nombrePublico,
        DateOnly fechaNacimiento,
        string? biografia,
        string? ciudadCodigo,
        CancellationToken cancellationToken) =>
        LlamarAsync(
            "proceso.sp_perfil_crear",
            [
                Uuid(usuarioId),
                Texto(nombrePublico),
                new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Date, Value = fechaNacimiento },
                Texto(biografia),
                Texto(ciudadCodigo),
            ],
            cancellationToken);

    public Task<ResultadoRutina> ActualizarDatosPerfilAsync(
        Guid usuarioId, string? nombrePublico, string? biografia, CancellationToken cancellationToken) =>
        LlamarAsync(
            "proceso.sp_perfil_actualizar_datos",
            [Uuid(usuarioId), Texto(nombrePublico), Texto(biografia)],
            cancellationToken);

    public Task<ResultadoRutina> CambiarModosPerfilAsync(
        Guid usuarioId, bool modoAmistad, bool modoCitas, CancellationToken cancellationToken) =>
        LlamarAsync(
            "proceso.sp_perfil_cambiar_modos",
            [Uuid(usuarioId), Booleano(modoAmistad), Booleano(modoCitas)],
            cancellationToken);

    public Task<ResultadoRutina> EstablecerInteresesPerfilAsync(
        Guid usuarioId, int[] interesIds, CancellationToken cancellationToken) =>
        LlamarAsync(
            "proceso.sp_perfil_establecer_intereses",
            [Uuid(usuarioId), new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer, Value = interesIds }],
            cancellationToken);

    // ------------------------------------------------------------ mecánica ----

    private static NpgsqlParameter Uuid(Guid valor) => new() { NpgsqlDbType = NpgsqlDbType.Uuid, Value = valor };

    private static NpgsqlParameter Booleano(bool valor) => new() { NpgsqlDbType = NpgsqlDbType.Boolean, Value = valor };

    private static NpgsqlParameter Texto(string? valor) =>
        new() { NpgsqlDbType = NpgsqlDbType.Varchar, Value = (object?)valor ?? DBNull.Value };

    /// <summary>
    /// Invoca un procedimiento. Todos terminan con dos parámetros de salida (p_codigo y p_mensaje);
    /// en la llamada se pasan como NULL y PostgreSQL devuelve sus valores en una fila.
    /// El nombre del procedimiento SIEMPRE es una constante de este archivo, nunca un dato externo.
    /// Los valores de las entradas viajan como parámetros, no concatenados.
    /// </summary>
    private async Task<ResultadoRutina> LlamarAsync(
        string procedimiento, NpgsqlParameter[] entradas, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var conexion = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var comando = conexion.CreateCommand();

            var marcadores = new List<string>(entradas.Length + 2);
            for (var i = 0; i < entradas.Length; i++)
            {
                entradas[i].ParameterName = $"p{i}";
                comando.Parameters.Add(entradas[i]);
                marcadores.Add($"@p{i}");
            }

            // Los dos NULL ocupan el lugar de los parámetros de salida (p_codigo, p_mensaje).
#pragma warning disable CA2100 // El nombre es una constante de este archivo; los valores van como parámetros.
            comando.CommandText = $"CALL {procedimiento}({string.Join(", ", marcadores)}{(marcadores.Count > 0 ? ", " : string.Empty)}NULL, NULL)";
#pragma warning restore CA2100

            await using var lector = await comando.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"El procedimiento {procedimiento} no devolvió resultado.");
            }

            return new ResultadoRutina(lector.GetString(0), lector.IsDBNull(1) ? null : lector.GetString(1));
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
