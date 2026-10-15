using Microsoft.AspNetCore.Identity;
using OtaLink.Domain.Datos;
using OtaLink.Domain.Entidades;

namespace OtaLink.Infrastructure.Identity;

/// <summary>
/// Almacén de usuarios de ASP.NET Core Identity. Reemplaza al almacén de EF Core: en lugar de generar
/// SQL, lee y escribe a través de los procedimientos y funciones de la base de datos (OtaLinkRutinas).
/// Identity sigue haciendo toda la lógica de seguridad (hash de contraseñas, bloqueo, tokens); este
/// almacén solo decide DÓNDE se guardan los datos.
///
/// Los métodos de "Get/Set" trabajan sobre el objeto en memoria; solo Create, Update, Delete y Find*
/// llegan a la base. Identity llama a UpdateAsync después de modificar el objeto.
/// </summary>
internal sealed class UsuarioStore(OtaLinkRutinas rutinas) :
    IUserStore<Usuario>,
    IUserPasswordStore<Usuario>,
    IUserEmailStore<Usuario>,
    IUserSecurityStampStore<Usuario>,
    IUserLockoutStore<Usuario>
{
    // ------------------------------------------------------------ IUserStore --

    public Task<string> GetUserIdAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NombreUsuario);

    public Task SetUserNameAsync(Usuario user, string? userName, CancellationToken cancellationToken)
    {
        user.NombreUsuario = userName ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NombreUsuarioNormalizado);

    public Task SetNormalizedUserNameAsync(Usuario user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NombreUsuarioNormalizado = normalizedName ?? string.Empty;
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(Usuario user, CancellationToken cancellationToken)
    {
        if (user.Id == Guid.Empty)
        {
            user.Id = Guid.NewGuid();
        }

        if (string.IsNullOrEmpty(user.SelloConcurrencia))
        {
            user.SelloConcurrencia = Guid.NewGuid().ToString();
        }

        if (string.IsNullOrEmpty(user.SelloSeguridad))
        {
            user.SelloSeguridad = Guid.NewGuid().ToString();
        }

        return Convertir(await rutinas.CrearUsuarioAsync(user, cancellationToken));
    }

    public async Task<IdentityResult> UpdateAsync(Usuario user, CancellationToken cancellationToken)
    {
        // Concurrencia optimista: la base solo actualiza si el sello guardado sigue siendo el que cargamos.
        var selloAnterior = user.SelloConcurrencia;
        user.SelloConcurrencia = Guid.NewGuid().ToString();

        var resultado = await rutinas.ActualizarUsuarioAsync(user, selloAnterior, cancellationToken);
        if (!resultado.Exitoso)
        {
            user.SelloConcurrencia = selloAnterior;
        }

        return Convertir(resultado);
    }

    public async Task<IdentityResult> DeleteAsync(Usuario user, CancellationToken cancellationToken) =>
        Convertir(await rutinas.EliminarUsuarioAsync(user.Id, cancellationToken));

    public Task<Usuario?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Guid.TryParse(userId, out var id)
            ? rutinas.UsuarioPorIdAsync(id, cancellationToken)
            : Task.FromResult<Usuario?>(null);

    public Task<Usuario?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        rutinas.UsuarioPorNombreNormalizadoAsync(normalizedUserName, cancellationToken);

    // ------------------------------------------------------ IUserPasswordStore --

    public Task SetPasswordHashAsync(Usuario user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.HashContrasena = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult(user.HashContrasena);

    public Task<bool> HasPasswordAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrEmpty(user.HashContrasena));

    // --------------------------------------------------------- IUserEmailStore --

    public Task SetEmailAsync(Usuario user, string? email, CancellationToken cancellationToken)
    {
        user.Correo = email ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.Correo);

    public Task<bool> GetEmailConfirmedAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult(user.CorreoConfirmado);

    public Task SetEmailConfirmedAsync(Usuario user, bool confirmed, CancellationToken cancellationToken)
    {
        user.CorreoConfirmado = confirmed;
        return Task.CompletedTask;
    }

    public Task<Usuario?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        rutinas.UsuarioPorCorreoNormalizadoAsync(normalizedEmail, cancellationToken);

    public Task<string?> GetNormalizedEmailAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.CorreoNormalizado);

    public Task SetNormalizedEmailAsync(Usuario user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.CorreoNormalizado = normalizedEmail ?? string.Empty;
        return Task.CompletedTask;
    }

    // ---------------------------------------------------- IUserSecurityStampStore --

    public Task SetSecurityStampAsync(Usuario user, string stamp, CancellationToken cancellationToken)
    {
        user.SelloSeguridad = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.SelloSeguridad);

    // ------------------------------------------------------- IUserLockoutStore --

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult(user.FinBloqueo);

    public Task SetLockoutEndDateAsync(Usuario user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    {
        user.FinBloqueo = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(Usuario user, CancellationToken cancellationToken)
    {
        user.IntentosFallidos++;
        return Task.FromResult(user.IntentosFallidos);
    }

    public Task ResetAccessFailedCountAsync(Usuario user, CancellationToken cancellationToken)
    {
        user.IntentosFallidos = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult(user.IntentosFallidos);

    public Task<bool> GetLockoutEnabledAsync(Usuario user, CancellationToken cancellationToken) =>
        Task.FromResult(user.BloqueoHabilitado);

    public Task SetLockoutEnabledAsync(Usuario user, bool enabled, CancellationToken cancellationToken)
    {
        user.BloqueoHabilitado = enabled;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // Nada que liberar: la conexión la gestiona el DbContext de la petición.
    }

    // ---------------------------------------------------------------- mecánica --

    /// <summary>Traduce el código del procedimiento a un resultado de Identity (con los códigos que Identity espera).</summary>
    private static IdentityResult Convertir(ResultadoRutina resultado) => resultado.Codigo switch
    {
        ResultadoRutina.CodigoOk => IdentityResult.Success,
        "usuario.correo_duplicado" => Fallo("DuplicateEmail", resultado.Mensaje),
        "usuario.nombre_duplicado" => Fallo("DuplicateUserName", resultado.Mensaje),
        "usuario.conflicto_concurrencia" => Fallo("ConcurrencyFailure", resultado.Mensaje),
        _ => Fallo("DefaultError", resultado.Mensaje),
    };

    private static IdentityResult Fallo(string codigo, string? descripcion) =>
        IdentityResult.Failed(new IdentityError { Code = codigo, Description = descripcion ?? "No se pudo completar la operación." });
}
