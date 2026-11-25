using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.Api.Modules.Listings;

/// <summary>HU-42 (oferta): las publicaciones del campus.</summary>
/// <param name="Created">Las publicadas en el periodo.</param>
/// <param name="Active">Las que estan a la venta ahora.</param>
/// <param name="CreatedByCategory">Las publicadas en el periodo, por categoria (slug).</param>
/// <param name="CategoryNames">El nombre de cada categoria del campus, tambien de las desactivadas.</param>
internal sealed record Supply(long Created, long Active, IReadOnlyDictionary<string, long> CreatedByCategory, IReadOnlyDictionary<string, string> CategoryNames);

/// <summary>Contrato publico de M-03 para M-14 (HU-42).</summary>
internal interface IListingIndicators
{
    /// <summary>La oferta del campus en el periodo.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="from">Inicio (UTC, inclusivo).</param>
    /// <param name="to">Fin (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los conteos.</returns>
    Task<Supply> SupplyAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre los repositorios de M-03.</summary>
internal sealed class ListingIndicators(IProductRepository products, ICategoryRepository categories) : IListingIndicators
{
    /// <inheritdoc />
    public async Task<Supply> SupplyAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var usage = await products.UsageAsync(communityId, from, to, cancellationToken).ConfigureAwait(false);
        var all = await categories.ListAllAsync(communityId, cancellationToken).ConfigureAwait(false);
        return new Supply(usage.Created, usage.Active, usage.CreatedByCategory, all.ToDictionary(c => c.Slug, c => c.Name, StringComparer.Ordinal));
    }
}
