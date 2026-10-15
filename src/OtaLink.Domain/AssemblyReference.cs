using System.Reflection;

namespace OtaLink.Domain;

/// <summary>Marcador para que las pruebas de arquitectura localicen este ensamblado.</summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
