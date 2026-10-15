using System.Xml.Linq;
using NetArchTest.Rules;

namespace OtaLink.ArchitectureTests;

/// <summary>
/// Hace cumplir las reglas de dependencias entre capas. Si alguien añade una referencia
/// prohibida, estas pruebas fallan y la compilación de integración no pasa.
/// </summary>
public class DependencyRulesTests
{
    // ---- Reglas sobre el código compilado (tipos) ----

    [Fact]
    public void Domain_no_depende_de_EF_Core_ni_de_ASP_NET_ni_de_otras_capas()
    {
        var result = Types.InAssembly(Domain.AssemblyReference.Assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.AspNetCore",
                "OtaLink.Application",
                "OtaLink.Infrastructure",
                "OtaLink.Contracts",
                "OtaLink.Api",
                "OtaLink.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_no_depende_de_Infrastructure_ni_de_EF_Core()
    {
        var result = Types.InAssembly(Application.AssemblyReference.Assembly)
            .ShouldNot().HaveDependencyOnAny(
                "OtaLink.Infrastructure",
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "OtaLink.Api",
                "OtaLink.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Contracts_no_expone_el_dominio()
    {
        var result = Types.InAssembly(Contracts.AssemblyReference.Assembly)
            .ShouldNot().HaveDependencyOnAny("OtaLink.Domain", "OtaLink.Application", "OtaLink.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    // ---- Reglas sobre las referencias entre proyectos (.csproj) ----

    [Theory]
    [InlineData("OtaLink.Domain", new string[0])]
    [InlineData("OtaLink.Contracts", new string[0])]
    [InlineData("OtaLink.Application", new[] { "OtaLink.Domain" })]
    [InlineData("OtaLink.Infrastructure", new[] { "OtaLink.Application", "OtaLink.Domain" })]
    [InlineData("OtaLink.Api", new[] { "OtaLink.Application", "OtaLink.Contracts", "OtaLink.Infrastructure" })]
    // La web solo puede conocer los contratos: nunca el dominio, la aplicación ni la infraestructura.
    [InlineData("OtaLink.Web", new[] { "OtaLink.Contracts" })]
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
