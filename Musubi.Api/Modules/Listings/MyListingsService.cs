using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OutputCaching;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Platform.Caching;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.Listings;

/// <summary>
/// Editar, pausar, reactivar y eliminar mis publicaciones (HU-07, maqueta <c>Mis-productos</c>). Solo el dueno
/// actua (RNF-03): a cualquier otra persona se le responde como si la publicacion no existiera (OWASP API1).
/// </summary>
internal sealed class MyListingsService(
    IProductRepository products,
    ICategoryRepository categories,
    IMemberDirectory members,
    IMediaStorage media,
    IContentModerator moderator,
    IOutputCacheStore outputCache,
    IEnumerable<IListingPhotoReferences> photoReferences,
    IOpenOrders openOrders,
    TimeProvider time)
{
    /// <summary>Cambia los datos de una publicacion (maqueta <c>Vender-2…4</c>, "Editar publicacion").</summary>
    /// <param name="userId">Vendedor (del token).</param>
    /// <param name="productId">Id de la publicacion.</param>
    /// <param name="request">Datos editables completos.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion ya cambiada o el error.</returns>
    public async Task<Result<ProductResponse>> UpdateAsync(string userId, string productId, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var activeCategories = await categories.ListActiveAsync(member.CommunityId, cancellationToken).ConfigureAwait(false);
        var checkedDetails = ProductRules.Check(
            request.Title, request.Description, request.PriceCents, request.Condition, request.ConditionDetail, request.Category, activeCategories);
        if (checkedDetails.Error is { } invalid)
        {
            return invalid;
        }

        var details = checkedDetails.Value!;
        var current = await products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);
        if (current is null || !IsOwnedBy(current, userId))
        {
            return ListingsErrors.ProductNotFound;
        }

        // 02-07: solo se modera el texto que cambio; el que no cambio ya se modero al publicar.
        if (await ModerateChangesAsync(current, details, cancellationToken).ConfigureAwait(false) is { } rejected)
        {
            return rejected;
        }

        // El dueno vuelve a ir en el filtro: si la borraron entre la lectura y aqui, no se resucita.
        var updated = await products.UpdateDetailsAsync(productId, userId, details, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (updated is null)
        {
            return ListingsErrors.ProductNotFound;
        }

        await EvictShowcaseAsync(cancellationToken).ConfigureAwait(false);
        // NOTE (HU-18): si cambiaron el nombre, la categoria, el precio o la descripcion, volver a buscar
        // coincidencias con las solicitudes "Se busca" en segundo plano (RF-08, RNF-11).
        return Result<ProductResponse>.Ok(ListingService.ToResponse(updated, includeSeller: true));
    }

    /// <summary>Pausa o reactiva una publicacion. Es idempotente.</summary>
    /// <param name="userId">Vendedor (del token).</param>
    /// <param name="productId">Id de la publicacion.</param>
    /// <param name="paused"><c>true</c> para pausar (<c>hidden</c>); <c>false</c> para reactivar.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion ya cambiada o el error.</returns>
    public async Task<Result<ProductResponse>> SetPausedAsync(string userId, string productId, bool paused, CancellationToken cancellationToken)
    {
        if (!await IsActiveMemberAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            return ApiErrors.Forbidden;
        }

        var changed = await products.SetPausedAsync(productId, userId, paused, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (changed is null)
        {
            return ListingsErrors.ProductNotFound;
        }

        await EvictShowcaseAsync(cancellationToken).ConfigureAwait(false);
        return Result<ProductResponse>.Ok(ListingService.ToResponse(changed, includeSeller: true));
    }

    /// <summary>Elimina una publicacion y sus fotos ("Esta accion no se puede deshacer"; borrado real).</summary>
    /// <param name="userId">Vendedor (del token).</param>
    /// <param name="productId">Id de la publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> o el error.</returns>
    public async Task<Result<bool>> DeleteAsync(string userId, string productId, CancellationToken cancellationToken)
    {
        if (!await IsActiveMemberAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            return ApiErrors.Forbidden;
        }

        // RN-07 (HU-23): con pedidos pendientes o confirmados no se elimina. Primero se revisa que sea suya, para no
        // revelarle a otra persona que tiene pedidos (OWASP API1).
        var current = await products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);
        if (current is null || !IsOwnedBy(current, userId))
        {
            return ListingsErrors.ProductNotFound;
        }

        if (await openOrders.ForProductAsync(current.Id, cancellationToken).ConfigureAwait(false))
        {
            return ListingsErrors.ProductHasOrders;
        }

        var deleted = await products.DeleteAsync(productId, userId, cancellationToken).ConfigureAwait(false);
        if (deleted is null)
        {
            return ListingsErrors.ProductNotFound;
        }

        // Primero el documento (deja de verse) y despues las fotos, una por llave: el prefijo es de todas las
        // publicaciones del vendedor. Una foto que otro modulo sigue mostrando (la portada en una oferta, HU-16)
        // se queda. NOTE (02-05): si S3 falla a medias, la regla de ciclo de vida del bucket limpia las huerfanas.
        var inUse = await PhotosInUseAsync(deleted.SellerId, deleted.Photos, cancellationToken).ConfigureAwait(false);
        foreach (var key in deleted.Photos.Where(key => !inUse.Contains(key)))
        {
            await media.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await EvictShowcaseAsync(cancellationToken).ConfigureAwait(false);
        return Result<bool>.Ok(true);
    }

    private static bool IsOwnedBy(ProductDocument product, string userId) =>
        string.Equals(product.SellerId, userId, StringComparison.Ordinal) && product.Status != ProductStatuses.Removed;

    private async Task<ApiError?> ModerateChangesAsync(ProductDocument current, ProductDetails details, CancellationToken cancellationToken)
    {
        var texts = new List<string>(2);
        var fields = new List<string>(2);
        if (!string.Equals(current.Title, details.Title, StringComparison.Ordinal))
        {
            texts.Add(details.Title);
            fields.Add("el nombre del producto");
        }

        if (!string.Equals(current.Description, details.Description, StringComparison.Ordinal))
        {
            texts.Add(details.Description);
            fields.Add("la descripción");
        }

        if (texts.Count == 0)
        {
            return null;
        }

        var verdicts = await moderator.CheckTextsAsync(texts, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < texts.Count; i++)
        {
            if (ModerationErrors.For(verdicts[i], ModerationErrors.TextRejected(fields[i])) is { } rejected)
            {
                return rejected;
            }
        }

        return null;
    }

    private async Task<HashSet<string>> PhotosInUseAsync(string sellerId, IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        var inUse = new HashSet<string>(StringComparer.Ordinal);
        foreach (var references in photoReferences)
        {
            inUse.UnionWith(await references.InUseAsync(sellerId, keys, cancellationToken).ConfigureAwait(false));
        }

        return inUse;
    }

    private async Task<bool> IsActiveMemberAsync(string userId, CancellationToken cancellationToken) =>
        await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false) is { Active: true };

    // La vitrina del visitante (cache de salida, 02-02) refleja el cambio en la siguiente consulta.
    private ValueTask EvictShowcaseAsync(CancellationToken cancellationToken) =>
        outputCache.EvictByTagAsync(CachingServiceCollectionExtensions.ShowcaseTag, cancellationToken);
}
