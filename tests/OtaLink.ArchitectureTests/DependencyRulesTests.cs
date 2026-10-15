using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace OtaLink.ArchitectureTests;

/// <summary>
/// Hace cumplir la arquitectura de OtaLink. Si alguien añade una referencia o una dependencia
/// prohibida, estas pruebas fallan.
///
///   PETICIÓN:   Controllers (Api) → interfaces (Application) → servicios (Infrastructure)
///   RESPUESTA:  servicios (Infrastructure) → interfaces (Application) → Controllers (Api)
///
///   Application     DTOs e interfaces. No conoce ninguna otra capa.
///   Domain          Entidades, DbContext y el archivo que llama a los procedimientos. No conoce ninguna otra capa.
///   Infrastructure  Servicios (usan Domain para hablar con la base, e implementan interfaces de Application).
///   Api             Controllers. Solo usan interfaces y DTOs de Application.
///   Web             Front. Solo conoce los DTOs de Application; habla con la API por HTTP.
/// </summary>
public class DependencyRulesTests
{
    private static readonly Assembly DomainAssembly = typeof(OtaLink.Domain.AssemblyReference).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(OtaLink.Application.AssemblyReference).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(OtaLink.Infrastructure.AssemblyReference).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    // ---- Reglas sobre el código compilado (tipos) ----

    [Fact]
    public void Application_no_conoce_ninguna_otra_capa_ni_la_base_de_datos()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "OtaLink.Domain",
                "OtaLink.Infrastructure",
                "OtaLink.Api",
                "OtaLink.Web",
                "Microsoft.EntityFrameworkCore",
                "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Domain_no_conoce_ninguna_otra_capa()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOnAny(
                "OtaLink.Application",
                "OtaLink.Infrastructure",
                "OtaLink.Api",
                "OtaLink.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_no_conoce_Api_ni_Web()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot().HaveDependencyOnAny("OtaLink.Api", "OtaLink.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Los_controladores_solo_dependen_de_interfaces_y_DTOs_de_Application()
    {
        // Un controlador nunca toca la implementación (Infrastructure), la base de datos (Domain) ni EF Core.
        var result = Types.InAssembly(ApiAssembly)
            .That().ResideInNamespace("OtaLink.Api.Controllers")
            .ShouldNot().HaveDependencyOnAny(
                "OtaLink.Infrastructure",
                "OtaLink.Domain",
                "Microsoft.EntityFrameworkCore",
                "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Los_servicios_viven_en_Infrastructure_y_cada_uno_implementa_una_interfaz_de_Application()
    {
        var servicios = Types.InAssembly(InfrastructureAssembly)
            .That().HaveNameEndingWith("Service")
            .GetTypes()
            .ToList();

        foreach (var servicio in servicios)
        {
            var implementaInterfazDeApplication = servicio
                .GetInterfaces()
                .Any(i => i.Namespace?.StartsWith("OtaLink.Application", StringComparison.Ordinal) == true);

            Assert.True(
                implementaInterfazDeApplication,
                $"{servicio.FullName} debe implementar una interfaz de OtaLink.Application.");
        }
    }

    [Fact]
    public void No_hay_repositorios()
    {
        // Decisión de arquitectura: los servicios hablan con la base a través de OtaLinkRutinas, sin repositorios.
        var repositorios = new[] { DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly }
            .SelectMany(a => Types.InAssembly(a).That().HaveNameEndingWith("Repository").GetTypes())
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(repositorios);
    }

    // ---- Reglas sobre las referencias entre proyectos (.csproj) ----

    [Theory]
    [InlineData("OtaLink.Application", new string[0])]
    [InlineData("OtaLink.Domain", new string[0])]
    [InlineData("OtaLink.Infrastructure", new[] { "OtaLink.Application", "OtaLink.Domain" })]
    [InlineData("OtaLink.Api", new[] { "OtaLink.Application", "OtaLink.Infrastructure" })]
    // La web solo puede conocer los DTOs: nunca el dominio ni la infraestructura.
    [InlineData("OtaLink.Web", new[] { "OtaLink.Application" })]
    public void Cada_proyecto_solo_referencia_lo_permitido(string project, string[] allowed)
    {
        var actual = ProjectReferencesOf(project);

        Assert.Equal(allowed.OrderBy(x => x, StringComparer.Ordinal), actual.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static string[] ProjectReferencesOf(string project)
    {
        var csproj = Path.Combine(SolutionRoot(), "src", project, $"{project}.csproj");
        return [.. XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension((string?)e.Attribute("Include") ?? string.Empty))];
    }

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OtaLink.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró OtaLink.sln.");
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Tipos que violan la regla: " + string.Join(", ", result.FailingTypeNames ?? []);
}
