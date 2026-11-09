using System;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.PurchaseRequests.Data;

// Ofertas "Yo tengo este producto" (HU-16). La validacion del servidor vive en db/011-ofertas.js; fila canonica
// en docs/modelo-datos.md.

/// <summary>
/// Una oferta a una solicitud: vincula una publicacion propia o describe un producto sin publicar (oferta directa,
/// RN-13: solo la ve quien busca). Una por persona y solicitud (RN-14, <c>ux_request_seller</c>).
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class OfferDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>La solicitud.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string RequestId { get; init; } = string.Empty;

    /// <summary>Quien busca (copia de la solicitud: HU-17 lista las ofertas de sus solicitudes).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string RequesterId { get; init; } = string.Empty;

    /// <summary>Quien ofrece.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string SellerId { get; init; } = string.Empty;

    /// <summary>Copia del nombre corto de quien ofrece ("Ana R.", R8).</summary>
    public string SellerName { get; init; } = string.Empty;

    /// <summary><see cref="OfferKinds.Listing"/> o <see cref="OfferKinds.Direct"/>.</summary>
    public string Kind { get; init; } = OfferKinds.Direct;

    /// <summary>Publicacion vinculada, o <c>null</c> en una oferta directa.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string? ProductId { get; init; }

    /// <summary>
    /// Lo que se ofrece: copia del nombre de la publicacion vinculada (R8) o la descripcion de la oferta directa.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Precio en centavos (el de la publicacion al vincularla, o el que se escribe).</summary>
    public long PriceCents { get; init; }

    /// <summary><c>new</c> o <c>used</c>.</summary>
    public string Condition { get; init; } = "used";

    /// <summary>Llave de la foto (<c>media/products/...</c>): la portada de la publicacion o la de la oferta directa.</summary>
    public string? PhotoKey { get; init; }

    /// <summary>"Mensaje para Fernanda" (opcional, hasta 300 caracteres).</summary>
    public string? Message { get; init; }

    /// <summary><see cref="OfferStatuses.Sent"/>; HU-17 la acepta o la rechaza.</summary>
    public string Status { get; init; } = OfferStatuses.Sent;

    /// <summary>Creacion (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Ultima edicion (UTC; RN-14 deja editarla).</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Como se responde a una solicitud.</summary>
internal static class OfferKinds
{
    /// <summary>"Vincular publicación": una publicacion propia disponible.</summary>
    public const string Listing = "listing";

    /// <summary>"Oferta directa": un producto sin publicar, que solo ve quien busca (RN-13).</summary>
    public const string Direct = "direct";
}

/// <summary>Estados de una oferta.</summary>
internal static class OfferStatuses
{
    /// <summary>Enviada; quien busca aun no decide (HU-17).</summary>
    public const string Sent = "sent";
}
