namespace OtaLink.Application.Intereses;

/// <summary>Categorías del catálogo de intereses de la comunidad otaku. Coinciden con smart.intereses.categoria.</summary>
public enum CategoriaInteres : short
{
    GenerosDeAnime = 1,
    MangaYLectura = 2,
    SeriesYFranquicias = 3,
    Videojuegos = 4,
    Cosplay = 5,
    Musica = 6,
    CulturaJaponesa = 7,
    Coleccionismo = 8,
    ComunidadYActividades = 9,
}

/// <summary>Un interés del catálogo, listo para mostrarse.</summary>
public sealed record InteresResponse(int Id, string Slug, string Nombre, CategoriaInteres Categoria);

public interface IInteresService
{
    Task<IReadOnlyList<InteresResponse>> ListarAsync(CancellationToken cancellationToken);
}
