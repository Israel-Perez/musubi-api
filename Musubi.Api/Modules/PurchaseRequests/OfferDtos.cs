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

/// <summary>Una oferta recibida (HU-17, "Compara las ofertas" de <c>Mis-solicitudes</c>).</summary>
/// <param name="Id">Id.</param>
/// <param name="Kind"><c>listing</c> ("Su publicación") o <c>direct</c> ("Oferta directa").</param>
/// <param name="ProductId">Publicacion vinculada, o <c>null</c>.</param>
/// <param name="Title">Nombre de la publicacion o descripcion de la oferta directa.</param>
/// <param name="PriceCents">Precio en centavos.</param>
/// <param name="Condition"><c>new</c> o <c>used</c>.</param>
/// <param name="ConditionDetail">Detalle del usado de la publicacion ("como nueva"), o <c>null</c>.</param>
/// <param name="Photo">Ruta de la foto, o <c>null</c>.</param>
/// <param name="Message">Mensaje de quien ofrece, o <c>null</c>.</param>
/// <param name="Status"><c>sent</c> o <c>accepted</c> (las rechazadas no se listan).</param>
/// <param name="Offerer">Quien ofrece.</param>
/// <param name="CreatedAt">Envio (UTC).</param>
public sealed record ReceivedOfferResponse(
    string Id,
    string Kind,
    string? ProductId,
    string Title,
    long PriceCents,
    string Condition,
    string? ConditionDetail,
    string? Photo,
    string? Message,
    string Status,
    OffererResponse Offerer,
    DateTime CreatedAt);

/// <summary>Quien ofrece, con su reputacion (HU-27) y carrera visible (HU-04).</summary>
/// <param name="Id">Id del usuario.</param>
/// <param name="Name">Nombre completo.</param>
/// <param name="Photo">Ruta de la foto, o <c>null</c>.</param>
/// <param name="Career">Carrera corta si la muestra, o <c>null</c>.</param>
/// <param name="RatingAvg">Promedio de calificaciones, o <c>null</c> sin calificaciones.</param>
/// <param name="RatingCount">Numero de calificaciones.</param>
public sealed record OffererResponse(string Id, string Name, string? Photo, string? Career, double? RatingAvg, int RatingCount);

/// <summary>Las ofertas recibidas en una solicitud, de la mas nueva a la mas vieja.</summary>
/// <param name="Items">Las ofertas pendientes y aceptadas.</param>
public sealed record ReceivedOffersResponse(System.Collections.Generic.IReadOnlyList<ReceivedOfferResponse> Items);

/// <summary>La conversacion con quien ofrece ("Abrir chat" o "Aceptar", HU-17).</summary>
/// <param name="ConversationId">Id de la conversacion (<c>GET /conversations/{id}</c>).</param>
public sealed record OfferChatResponse(string ConversationId);
