using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Listings;

/// <summary>Estados publicos de un favorito (HU-53).</summary>
internal static class FavoriteStatuses
{
    /// <summary>La publicacion sigue disponible.</summary>
    public const string Available = "available";

    /// <summary>La publicacion se agoto, pero se conserva para volver a verla.</summary>
    public const string SoldOut = "soldOut";

    /// <summary>Ya no se debe mostrar: quitada, pausada, inexistente, de otro campus o bloqueada.</summary>
    public const string Unavailable = "unavailable";
}

/// <summary>
/// Guardar, quitar y listar favoritos (HU-53). Solo se administran los del usuario del token; los bloqueos de
/// HU-57 se aplican en la lectura y en la escritura. La app arma el corazon con GET /favorites.
/// </summary>
internal sealed class FavoriteService(
    IFavoriteRepository favorites,
    IProductRepository products,
    IMemberDirectory members,
    IBlockDirectory blocks,
    TimeProvider time)
{
    /// <summary>Favoritos que devuelve la lista como maximo (sin paginar; mismo patron que HU-57).</summary>
    // NOTE (HU-53): la lista no pagina; si una persona supera este tope, se agrega paginacion.
    public const int MaxListed = 200;

    /// <summary>Guarda un producto. Idempotente: si ya estaba guardado responde igual.</summary>
    /// <param name="userId">Quien guarda (del token).</param>
    /// <param name="productId">Publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Exito o el error.</returns>
    public async Task<Result<bool>> AddAsync(string userId, string productId, CancellationToken cancellationToken)
    {
        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var product = string.IsNullOrEmpty(productId)
            ? null
            : await products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null
            || !string.Equals(product.CommunityId, member.CommunityId, StringComparison.Ordinal)
            || product.Status is not (ProductStatuses.Active or ProductStatuses.SoldOut)
            || await blocks.BetweenAsync(userId, product.SellerId, cancellationToken).ConfigureAwait(false))
        {
            return ListingsErrors.ProductNotFound;
        }

        await favorites.InsertIfAbsentAsync(
            new FavoriteDocument
            {
                Id = MongoConventions.NewId(),
                UserId = userId,
                ProductId = product.Id,
                CommunityId = product.CommunityId,
                CreatedAt = time.GetUtcNow().UtcDateTime,
            },
            cancellationToken).ConfigureAwait(false);
        return Result<bool>.Ok(true);
    }

    /// <summary>Quita un favorito. Idempotente: si no estaba, no pasa nada.</summary>
    /// <param name="userId">Quien quita (del token).</param>
    /// <param name="productId">Publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public Task RemoveAsync(string userId, string productId, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(productId)
            ? Task.CompletedTask
            : favorites.DeleteAsync(userId, productId, cancellationToken);

    /// <summary>"Favoritos": lo guardado por la persona, con el estado actual de cada publicacion.</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="communityId">Comunidad del token (RNF-21).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La lista, del favorito mas reciente al mas viejo.</returns>
    public async Task<FavoritesResponse> ListAsync(string userId, string communityId, CancellationToken cancellationToken)
    {
        var mine = await favorites.FindByUserAsync(userId, MaxListed, cancellationToken).ConfigureAwait(false);
        if (mine.Count == 0)
        {
            return new FavoritesResponse([]);
        }

        var found = await products.FindByIdsAsync([.. mine.Select(f => f.ProductId)], cancellationToken).ConfigureAwait(false);
        var byId = found.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var hidden = new HashSet<string>(
            await blocks.HiddenForAsync(userId, cancellationToken).ConfigureAwait(false),
            StringComparer.Ordinal);
        var items = new List<FavoriteItemResponse>(mine.Count);
        foreach (var favorite in mine)
        {
            var status = FavoriteStatuses.Unavailable;
            ProductResponse? product = null;
            if (byId.TryGetValue(favorite.ProductId, out var document)
                && string.Equals(document.CommunityId, communityId, StringComparison.Ordinal)
                && !hidden.Contains(document.SellerId)
                && document.Status is ProductStatuses.Active or ProductStatuses.SoldOut)
            {
                status = string.Equals(document.Status, ProductStatuses.SoldOut, StringComparison.Ordinal)
                    ? FavoriteStatuses.SoldOut
                    : FavoriteStatuses.Available;
                product = ListingService.ToResponse(document, includeSeller: true);
            }

            items.Add(new FavoriteItemResponse(favorite.ProductId, favorite.CreatedAt, status, product));
        }

        return new FavoritesResponse(items);
    }
}
