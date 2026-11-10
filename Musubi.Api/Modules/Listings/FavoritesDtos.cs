using System;
using System.Collections.Generic;

namespace Musubi.Api.Modules.Listings;

// Contratos HTTP de HU-53 (filas en docs/endpoints.md). La app arma el corazon con GET /favorites;
// los DTOs de productos no cambian (enfoque B).

/// <summary>Un producto guardado y si sigue disponible.</summary>
/// <param name="ProductId">Publicacion guardada.</param>
/// <param name="SavedAt">Cuando se guardo (UTC).</param>
/// <param name="Status"><c>available</c>, <c>soldOut</c> o <c>unavailable</c>.</param>
/// <param name="Product">La publicacion, o <c>null</c> si ya no se debe mostrar.</param>
public sealed record FavoriteItemResponse(string ProductId, DateTime SavedAt, string Status, ProductResponse? Product);

/// <summary>"Favoritos": lo guardado por la persona del token, del mas reciente al mas viejo.</summary>
/// <param name="Items">Los favoritos.</param>
public sealed record FavoritesResponse(IReadOnlyList<FavoriteItemResponse> Items);
