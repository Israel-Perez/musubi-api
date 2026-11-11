using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OutputCaching;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Caching;

namespace Musubi.Api.Modules.Listings;

/// <summary>
/// Contrato publico de M-03 para que los pedidos (HU-24) muevan las piezas de una publicacion sin leer la coleccion
/// <c>products</c>: apartar al aceptar (RF-34), regresar al cancelar (RN-20) y marcar vendida al completar (RN-05).
/// </summary>
internal interface IProductStock
{
    /// <summary>Aparta piezas de una publicacion disponible; si quedan cero, pasa a agotada.</summary>
    /// <param name="productId">La publicacion.</param>
    /// <param name="quantity">Piezas.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>false</c> si no alcanzan o la publicacion no esta disponible.</returns>
    Task<bool> TryReserveAsync(string productId, int quantity, CancellationToken cancellationToken);

    /// <summary>Regresa piezas apartadas; una agotada vuelve a estar disponible.</summary>
    /// <param name="productId">La publicacion.</param>
    /// <param name="quantity">Piezas.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Una tarea.</returns>
    Task ReleaseAsync(string productId, int quantity, CancellationToken cancellationToken);

    /// <summary>Una agotada sin piezas pasa a vendida (no vuelve a la vitrina).</summary>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si paso a vendida.</returns>
    Task<bool> MarkSoldIfEmptyAsync(string productId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de M-03; cada cambio refresca la vitrina del visitante (02-02).</summary>
internal sealed class ProductStock(IProductRepository products, IOutputCacheStore outputCache, TimeProvider time) : IProductStock
{
    /// <inheritdoc />
    public async Task<bool> TryReserveAsync(string productId, int quantity, CancellationToken cancellationToken)
    {
        var reserved = await products.TryReserveStockAsync(productId, quantity, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (reserved)
        {
            await EvictAsync(cancellationToken).ConfigureAwait(false);
        }

        return reserved;
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string productId, int quantity, CancellationToken cancellationToken)
    {
        if (await products.ReleaseStockAsync(productId, quantity, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false))
        {
            await EvictAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> MarkSoldIfEmptyAsync(string productId, CancellationToken cancellationToken)
    {
        var sold = await products.MarkSoldIfEmptyAsync(productId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (sold)
        {
            await EvictAsync(cancellationToken).ConfigureAwait(false);
        }

        return sold;
    }

    private ValueTask EvictAsync(CancellationToken cancellationToken) =>
        outputCache.EvictByTagAsync(CachingServiceCollectionExtensions.ShowcaseTag, cancellationToken);
}
