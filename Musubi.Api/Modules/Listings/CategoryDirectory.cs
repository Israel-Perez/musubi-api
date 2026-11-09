using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.Api.Modules.Listings;

/// <summary>Una categoria activa de un campus.</summary>
/// <param name="Slug">Identificador (<c>calculadoras</c>).</param>
/// <param name="Name">Nombre visible ("Calculadoras").</param>
internal sealed record CategorySummary(string Slug, string Name);

/// <summary>
/// Contrato publico de M-03 para los demas modulos (Modules/README: un modulo no lee las colecciones de otro).
/// Lo usan las solicitudes de compra (HU-13) para validar la categoria.
/// </summary>
internal interface ICategoryDirectory
{
    /// <summary>La categoria activa con ese slug en el campus, o <c>null</c>.</summary>
    /// <param name="communityId">Comunidad.</param>
    /// <param name="slug">Slug.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La categoria o <c>null</c>.</returns>
    Task<CategorySummary?> FindAsync(string communityId, string slug, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de categorias (con su cache, 02-02).</summary>
internal sealed class CategoryDirectory(ICategoryRepository categories) : ICategoryDirectory
{
    /// <inheritdoc />
    public async Task<CategorySummary?> FindAsync(string communityId, string slug, CancellationToken cancellationToken)
    {
        var list = await categories.ListActiveAsync(communityId, cancellationToken).ConfigureAwait(false);
        var category = list.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.Ordinal));
        return category is null ? null : new CategorySummary(category.Slug, category.Name);
    }
}
