namespace OtaLink.Domain.Entidades;

/// <summary>Tabla smart.ciudades (tabla fuerte: no depende de otras).</summary>
public sealed class Ciudad
{
    public short Id { get; set; }

    public string Codigo { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public bool Activa { get; set; } = true;
}

/// <summary>Tabla smart.intereses: catálogo cerrado de intereses de la comunidad otaku.</summary>
public sealed class Interes
{
    public int Id { get; set; }

    /// <summary>Identificador estable en minúsculas, sin espacios.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    /// <summary>1 géneros de anime, 2 manga, 3 franquicias, 4 videojuegos, 5 cosplay, 6 música, 7 cultura japonesa, 8 coleccionismo, 9 comunidad.</summary>
    public short Categoria { get; set; }

    public bool Activo { get; set; } = true;
}
