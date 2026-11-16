using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Listings;

/// <summary>"Avísame" de una publicacion (HU-51): si la persona ya lo pidio.</summary>
/// <param name="Active"><c>true</c> = "Te avisaremos".</param>
public sealed record StockAlertResponse(bool Active);

/// <summary>Un aviso tomado para avisar (HU-51 → M-06).</summary>
/// <param name="Id">Aviso (para no repetir el push).</param>
/// <param name="UserId">A quien.</param>
internal sealed record TakenStockAlert(string Id, string UserId);

/// <summary>
/// Contrato de M-03 hacia M-06 (HU-51): una publicacion pudo volver a tener piezas (quien vende repuso, o se cancelo un
/// pedido o un apartado). La revision va en segundo plano (RNF-11) y nunca falla hacia quien llama (RNF-15).
/// </summary>
internal interface IProductRestocks
{
    /// <summary>Encola la revision sin esperar.</summary>
    /// <param name="productId">La publicacion.</param>
    /// <returns><c>true</c> si entro a la cola.</returns>
    bool Restocked(string productId);
}

/// <summary>Contrato de M-03 para M-06 (HU-51): los avisos de una publicacion que volvio.</summary>
internal interface IStockAlertDirectory
{
    /// <summary>Toma (y borra: se usan una vez) los avisos vigentes de una publicacion.</summary>
    /// <param name="productId">La publicacion.</param>
    /// <param name="excludedUsers">Quien vende y las personas bloqueadas (HU-57).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los avisos.</returns>
    Task<IReadOnlyList<TakenStockAlert>> TakeAsync(string productId, IReadOnlyCollection<string> excludedUsers, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// "Avísame" / "Te avisaremos" (maqueta <c>Producto</c>, HU-51): solo en una publicacion agotada que no es mia, uno por
/// publicacion, hasta 20 por persona y 90 dias. Privados (RNF-07): nadie ve quien los pidio; se borran con la cuenta.
/// </summary>
internal sealed class StockAlertService(
    IStockAlertRepository alerts,
    ListingService listings,
    TimeProvider time) : IStockAlertDirectory, IAccountDataEraser
{
    /// <summary>Cuanto dura un aviso sin que la publicacion vuelva.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    /// <summary>Avisos vigentes por persona como maximo (OWASP API4).</summary>
    public const int MaxPerUser = 20;

    /// <summary>Si ya pedi el aviso de esa publicacion.</summary>
    /// <param name="userId">Quien (del token).</param>
    /// <param name="communityId">Su campus (del token).</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El estado, o <c>listings.product_not_found</c>.</returns>
    public async Task<Result<StockAlertResponse>> StatusAsync(string userId, string? communityId, string productId, CancellationToken cancellationToken)
    {
        if (await listings.FindVisibleAsync(userId, communityId, productId, cancellationToken).ConfigureAwait(false) is null)
        {
            return ListingsErrors.ProductNotFound;
        }

        var active = await alerts.ExistsAsync(userId, productId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        return Result<StockAlertResponse>.Ok(new StockAlertResponse(active));
    }

    /// <summary>"Avísame": registra el interes en una publicacion agotada (pedirlo otra vez solo lo renueva).</summary>
    /// <param name="userId">Quien (del token).</param>
    /// <param name="communityId">Su campus (del token).</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El aviso activo, o el error.</returns>
    public async Task<Result<StockAlertResponse>> SubscribeAsync(string userId, string? communityId, string productId, CancellationToken cancellationToken)
    {
        if (await listings.FindVisibleAsync(userId, communityId, productId, cancellationToken).ConfigureAwait(false) is not { } product)
        {
            return ListingsErrors.ProductNotFound;
        }

        if (string.Equals(product.SellerId, userId, StringComparison.Ordinal))
        {
            return ListingsErrors.OwnProduct;
        }

        if (product.Status != ProductStatuses.SoldOut)
        {
            return ListingsErrors.ProductAvailable;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (!await alerts.ExistsAsync(userId, productId, now, cancellationToken).ConfigureAwait(false)
            && await alerts.CountByUserAsync(userId, now, cancellationToken).ConfigureAwait(false) >= MaxPerUser)
        {
            return ListingsErrors.TooManyStockAlerts;
        }

        await alerts.SaveAsync(
            new StockAlertDocument
            {
                Id = MongoConventions.NewId(),
                UserId = userId,
                CommunityId = product.CommunityId,
                ProductId = product.Id,
                CreatedAt = now,
                ExpiresAt = now + Lifetime,
            },
            cancellationToken).ConfigureAwait(false);
        return Result<StockAlertResponse>.Ok(new StockAlertResponse(true));
    }

    /// <summary>Quita mi aviso (idempotente: si no existe, no pasa nada).</summary>
    /// <param name="userId">Quien (del token).</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public Task UnsubscribeAsync(string userId, string productId, CancellationToken cancellationToken) =>
        alerts.DeleteAsync(userId, productId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TakenStockAlert>> TakeAsync(
        string productId,
        IReadOnlyCollection<string> excludedUsers,
        int limit,
        CancellationToken cancellationToken) =>
        [.. (await alerts.TakeAsync(productId, excludedUsers, time.GetUtcNow().UtcDateTime, limit, cancellationToken).ConfigureAwait(false))
            .Select(a => new TakenStockAlert(a.Id, a.UserId))];

    /// <inheritdoc />
    public Task EraseAsync(string userId, CancellationToken cancellationToken) => alerts.DeleteByUserAsync(userId, cancellationToken);
}
