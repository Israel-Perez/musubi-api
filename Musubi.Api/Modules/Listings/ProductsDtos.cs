using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Musubi.Api.Modules.Listings;

// Contrato de /api/v1/products y /api/v1/categories (filas en docs/endpoints.md; formas en ContractTest/Fixtures/).
// Las reglas de negocio (RN-03, RN-04) las valida ListingService con codigos propios; aqui solo topes gruesos.

/// <summary>Categoria que se puede elegir al publicar o filtrar.</summary>
/// <param name="Slug">Identificador estable (<c>calculadoras</c>).</param>
/// <param name="Name">Nombre visible.</param>
public sealed record CategoryResponse(string Slug, string Name);

/// <summary>Foto subida y lista para publicarse.</summary>
/// <param name="Key">Llave que se manda al publicar.</param>
/// <param name="Path">Ruta de la imagen, relativa a la URL base de la API (CloudFront sirve <c>/media/*</c>).</param>
public sealed record PhotoResponse(string Key, string Path);

/// <summary>Publicar un producto (maqueta <c>Vender-1…4</c>, HU-06 y HU-50).</summary>
/// <param name="Title">Nombre del producto (3 a 80).</param>
/// <param name="Description">Descripcion (hasta 500; opcional).</param>
/// <param name="Category">Slug de la categoria.</param>
/// <param name="PriceCents">Precio en centavos (mayor que cero).</param>
/// <param name="Condition"><c>new</c> o <c>used</c>.</param>
/// <param name="ConditionDetail">Solo usado: <c>likeNew</c>, <c>good</c> o <c>fair</c>.</param>
/// <param name="Stock">Piezas disponibles (1 a 10).</param>
/// <param name="Photos">Llaves de las fotos subidas (1 a 8; la primera es la portada).</param>
public sealed record CreateProductRequest(
    [Required, MaxLength(200)] string Title,
    [MaxLength(2000)] string? Description,
    [Required, MaxLength(40)] string Category,
    [Required] long? PriceCents,
    [Required, MaxLength(10)] string Condition,
    [MaxLength(10)] string? ConditionDetail,
    [Required] int? Stock,
    [Required, MaxLength(20)] IReadOnlyList<string> Photos);

/// <summary>Vendedor visible en la publicacion (solo con sesion; RNF-05).</summary>
/// <param name="Name">Nombre corto ("Ana R.").</param>
public sealed record SellerResponse(string Name);

/// <summary>Publicacion.</summary>
/// <param name="Id">Id.</param>
/// <param name="Title">Nombre.</param>
/// <param name="Description">Descripcion.</param>
/// <param name="Category">Slug de la categoria.</param>
/// <param name="PriceCents">Precio en centavos.</param>
/// <param name="Condition"><c>new</c> o <c>used</c>.</param>
/// <param name="ConditionDetail"><c>likeNew</c>, <c>good</c>, <c>fair</c> o <c>null</c>.</param>
/// <param name="Stock">Piezas disponibles.</param>
/// <param name="Status"><c>active</c>, <c>soldOut</c> o <c>hidden</c>.</param>
/// <param name="Photos">Rutas de las fotos (la primera es la portada).</param>
/// <param name="Seller">Vendedor; <c>null</c> para visitantes.</param>
/// <param name="PublishedAt">Publicacion (UTC).</param>
public sealed record ProductResponse(
    string Id,
    string Title,
    string Description,
    string Category,
    long PriceCents,
    string Condition,
    string? ConditionDetail,
    int Stock,
    string Status,
    IReadOnlyList<string> Photos,
    SellerResponse? Seller,
    DateTime PublishedAt);

/// <summary>Pagina de publicaciones.</summary>
/// <param name="Items">Publicaciones, de la mas nueva a la mas vieja.</param>
/// <param name="NextCursor">Cursor para la siguiente pagina; <c>null</c> si no hay mas.</param>
/// <param name="Total">Total de la consulta sin paginar.</param>
public sealed record ProductPage(IReadOnlyList<ProductResponse> Items, string? NextCursor, long Total);

/// <summary>Rango de precios de productos parecidos (maqueta <c>Vender-3</c>).</summary>
/// <param name="LowCents">Percentil 25.</param>
/// <param name="HighCents">Percentil 75.</param>
/// <param name="Count">Publicaciones consideradas.</param>
public sealed record PriceRangeResponse(long LowCents, long HighCents, int Count);
