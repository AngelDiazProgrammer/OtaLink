namespace OtaLink.Infrastructure.Persistence;

/// <summary>
/// Configuración tipada de la base de datos. Se enlaza con la sección "Database".
/// La cadena de conexión NUNCA se guarda en Git: en desarrollo viene de User Secrets y
/// en el servidor de una variable de entorno (Database__ConnectionString).
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; set; } = string.Empty;
}
