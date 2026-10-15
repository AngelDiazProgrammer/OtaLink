using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OtaLink.IntegrationTests;

/// <summary>
/// Arranca la API en memoria con una cadena de conexión que apunta a un puerto cerrado:
/// sirve para probar todo lo que NO necesita base de datos y para comprobar cómo falla sin ella.
/// Las pruebas contra la base real de la VM se añaden en la etapa del modelo de datos.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting(
            "Database:ConnectionString",
            "Host=127.0.0.1;Port=1;Database=prueba;Username=prueba;Password=prueba;Timeout=1;Command Timeout=1");
    }
}
