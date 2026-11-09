using System;

namespace Musubi.Api.Modules.PurchaseRequests;

// Contratos HTTP de las ofertas (HU-16). Filas canonicas en docs/endpoints.md.

/// <summary>
/// "Enviar oferta" (maquetas <c>Yo-tengo-vincular</c> y <c>Yo-tengo-oferta</c>). Con <paramref name="ProductId"/>
/// vincula una publicacion propia (precio, estado y foto salen de ella); sin el es una oferta directa.
/// </summary>
/// <param name="ProductId">Publicacion propia disponible ("Vincular publicación"), o <c>null</c>.</param>
/// <param name="Description">"Descripción" de la oferta directa (3 a 300 caracteres).</param>
/// <param name="PriceCents">"Precio" de la oferta directa ($1 a $999,999).</param>
/// <param name="Condition">"Estado" de la oferta directa: <c>new</c> o <c>used</c>.</param>
/// <param name="Photo">Llave de una foto subida por <c>POST /products/photos</c> (opcional, oferta directa).</param>
/// <param name="Message">"Mensaje para Fernanda" (opcional, hasta 300 caracteres).</param>
public sealed record OfferRequest(
    string? ProductId,
    string? Description,
    long? PriceCents,
    string? Condition,
    string? Photo,
    string? Message);

/// <summary>Una oferta (lo que ve quien la envio; HU-17 la muestra a quien busca).</summary>
/// <param name="Id">Id.</param>
/// <param name="RequestId">La solicitud.</param>
/// <param name="Kind"><c>listing</c> o <c>direct</c>.</param>
/// <param name="ProductId">Publicacion vinculada, o <c>null</c>.</param>
/// <param name="Title">Nombre de la publicacion o descripcion de la oferta directa.</param>
/// <param name="PriceCents">Precio en centavos.</param>
/// <param name="Condition"><c>new</c> o <c>used</c>.</param>
/// <param name="Photo">Ruta de la foto (<c>/media/...</c>), o <c>null</c>.</param>
/// <param name="Message">Mensaje, o <c>null</c>.</param>
/// <param name="Status"><c>sent</c>.</param>
/// <param name="OverBudget">El precio pasa el presupuesto de la solicitud (se permite, la app lo advierte).</param>
/// <param name="CreatedAt">Creacion (UTC).</param>
/// <param name="UpdatedAt">Ultima edicion (UTC).</param>
public sealed record OfferResponse(
    string Id,
    string RequestId,
    string Kind,
    string? ProductId,
    string Title,
    long PriceCents,
    string Condition,
    string? Photo,
    string? Message,
    string Status,
    bool OverBudget,
    DateTime CreatedAt,
    DateTime UpdatedAt);
