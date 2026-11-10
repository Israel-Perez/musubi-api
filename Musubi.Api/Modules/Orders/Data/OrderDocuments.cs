using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.Orders.Data;

// Pedidos (HU-23). La validacion del servidor vive en db/014-pedidos.js; fila canonica en docs/modelo-datos.md.

/// <summary>
/// Un pedido: sale de una conversacion (RN-21) entre quien compra y quien vende, copia el producto (R8) y nace
/// <see cref="OrderStatuses.Pending"/> (RN-16). Uno abierto por conversacion (<c>ux_conversation_open</c>).
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class OrderDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Consecutivo del campus: se muestra como <c>MS-NNNN</c>.</summary>
    public int Number { get; init; }

    /// <summary>Quien compra.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string BuyerId { get; init; } = string.Empty;

    /// <summary>Quien vende.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string SellerId { get; init; } = string.Empty;

    /// <summary>Copia del nombre corto de quien compra (R8; "Usuario eliminado" al borrar la cuenta, R11).</summary>
    public string BuyerName { get; init; } = string.Empty;

    /// <summary>Copia del nombre corto de quien vende (R8).</summary>
    public string SellerName { get; init; } = string.Empty;

    /// <summary>La conversacion de la que salio (RN-21).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ConversationId { get; init; } = string.Empty;

    /// <summary>Lo que se compra (copia, R8).</summary>
    public OrderItem Item { get; init; } = new();

    /// <summary>Donde y cuando se entrega (RF-30).</summary>
    public OrderDelivery Delivery { get; init; } = new();

    /// <summary>Como se paga, o <c>null</c> mientras no se elige (RN-18).</summary>
    [BsonIgnoreIfNull]
    public OrderPayment? Payment { get; init; }

    /// <summary>Ver <see cref="OrderStatuses"/>.</summary>
    public string Status { get; init; } = OrderStatuses.Pending;

    /// <summary>Los cambios de estado, del primero al ultimo (hasta 10).</summary>
    public IReadOnlyList<OrderTransition> History { get; init; } = [];

    /// <summary>Alta (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Ultimo cambio (UTC).</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Copia de lo que se compra.</summary>
internal sealed class OrderItem
{
    /// <summary>De donde salio: <see cref="OrderSources"/>.</summary>
    public string Source { get; init; } = OrderSources.Product;

    /// <summary>La publicacion o la oferta.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string SourceId { get; init; } = string.Empty;

    /// <summary>La publicacion (tambien en una oferta vinculada), o <c>null</c> en una oferta directa.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? ProductId { get; init; }

    /// <summary>Nombre del producto.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Ruta de la foto (<c>/media/products/...</c>), o <c>null</c>.</summary>
    [BsonIgnoreIfNull]
    public string? Photo { get; init; }

    /// <summary>Precio unitario en centavos.</summary>
    public long PriceCents { get; init; }

    /// <summary>Piezas (RN-17).</summary>
    public int Quantity { get; init; }

    /// <summary>Precio por piezas, en centavos.</summary>
    public long TotalCents { get; init; }
}

/// <summary>Entrega en un punto del campus.</summary>
internal sealed class OrderDelivery
{
    /// <summary>El punto (<c>meetingPoints.slug</c>).</summary>
    public string PointSlug { get; init; } = string.Empty;

    /// <summary>Copia del nombre del punto (R8).</summary>
    public string PointName { get; init; } = string.Empty;

    /// <summary>Precision libre ("Planta baja, junto a la entrada"), moderada, o <c>null</c>.</summary>
    [BsonIgnoreIfNull]
    public string? Detail { get; init; }

    /// <summary>Fecha y hora (UTC).</summary>
    public DateTime At { get; init; }
}

/// <summary>El pago elegido (RN-18): en HU-23, solo efectivo al entregar.</summary>
internal sealed class OrderPayment
{
    /// <summary>Ver <see cref="PaymentMethods"/>.</summary>
    public string Method { get; init; } = PaymentMethods.Cash;

    /// <summary>Cuando se eligio (UTC).</summary>
    public DateTime ChosenAt { get; init; }
}

/// <summary>Un cambio de estado.</summary>
internal sealed class OrderTransition
{
    /// <summary>El estado nuevo.</summary>
    public string Status { get; init; } = OrderStatuses.Pending;

    /// <summary>Cuando (UTC).</summary>
    public DateTime At { get; init; }

    /// <summary>El motivo de una cancelacion, o <c>null</c>.</summary>
    [BsonIgnoreIfNull]
    public string? Reason { get; init; }
}

/// <summary>Estados de un pedido (RF-31).</summary>
internal static class OrderStatuses
{
    /// <summary>Recien creado: espera que quien vende lo acepte (RN-16).</summary>
    public const string Pending = "pending";

    /// <summary>Quien vende lo acepto (HU-24).</summary>
    public const string Confirmed = "confirmed";

    /// <summary>Entregado (HU-24, RN-19).</summary>
    public const string Completed = "completed";

    /// <summary>Cancelado por una de las partes (RN-20) o al borrar una cuenta (HU-46).</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Los que siguen abiertos: cuentan para <c>ux_conversation_open</c> y RN-07.</summary>
    public static readonly IReadOnlyList<string> Open = [Pending, Confirmed];
}

/// <summary>De donde sale un pedido.</summary>
internal static class OrderSources
{
    /// <summary>Una publicacion (conversacion <c>product</c>).</summary>
    public const string Product = "product";

    /// <summary>Una oferta aceptada (conversacion <c>offer</c>, HU-17).</summary>
    public const string Offer = "offer";
}

/// <summary>Formas de pago (D-01).</summary>
internal static class PaymentMethods
{
    /// <summary>Efectivo al entregar (RN-18 del Entregable 1).</summary>
    public const string Cash = "cash";

    /// <summary>Transferencia a la cuenta del vendedor (HU-55).</summary>
    public const string Transfer = "transfer";
}

/// <summary>Motivos de cancelacion que pone el sistema.</summary>
internal static class CancelReasons
{
    /// <summary>Una de las partes borro su cuenta (HU-46).</summary>
    public const string AccountDeleted = "account_deleted";
}

/// <summary>Un punto de entrega del campus (los administra HU-44).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class MeetingPointDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Identificador legible (<c>biblioteca</c>).</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Nombre ("Biblioteca").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Las carreras de ese edificio: se recomienda a quien estudia ahi.</summary>
    public IReadOnlyList<string> CareerSlugs { get; init; } = [];

    /// <summary>Orden en la lista.</summary>
    public int Order { get; init; }

    /// <summary>Se puede elegir.</summary>
    public bool Active { get; init; } = true;

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}
