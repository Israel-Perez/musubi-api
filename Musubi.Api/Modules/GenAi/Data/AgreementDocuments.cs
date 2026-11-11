using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.GenAi.Data;

// Documentos de persistencia de M-10 (HU-32/33/34). La validacion del servidor vive en db/016-acuerdos.js; fila
// canonica en docs/modelo-datos.md. Del chat no se guarda nada: solo la propuesta (decision de la usuaria,
// 10-Nov-2026), que se borra al crear el pedido, al descartarla o a los 30 dias (indice TTL, RN-44).

/// <summary>El acuerdo de una conversacion: la propuesta de la IA, corregida y confirmada por los dos (RN-41).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class AgreementDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>La conversacion (uno por conversacion: <c>ux_conversation</c>).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ConversationId { get; init; } = string.Empty;

    /// <summary>Quien compra.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string BuyerId { get; init; } = string.Empty;

    /// <summary>Quien vende.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string SellerId { get; init; } = string.Empty;

    /// <summary>Quien pidio el resumen (RN-39: siempre una persona).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string RequestedBy { get; init; } = string.Empty;

    /// <summary>La propuesta.</summary>
    public AgreementProposal Proposal { get; init; } = new();

    /// <summary>
    /// Lo que la IA no identifico (RN-42), en los nombres de <see cref="AgreementFields"/>. No cambia al editar: asi la
    /// app marca "Agregada" lo que despues lleno una persona (maqueta <c>Acuerdo-confirmado</c>).
    /// </summary>
    public IReadOnlyList<string> NotIdentified { get; init; } = [];

    /// <summary>Sube con cada cambio: una confirmacion vale solo para la version que vio la persona (RN-41).</summary>
    public int Revision { get; init; } = 1;

    /// <summary>Ver <see cref="AgreementStatuses"/>.</summary>
    public string Status { get; init; } = AgreementStatuses.Proposed;

    /// <summary>Confirmacion de quien compra (UTC), o <c>null</c>.</summary>
    public DateTime? BuyerConfirmedAt { get; init; }

    /// <summary>Confirmacion de quien vende (UTC), o <c>null</c>.</summary>
    public DateTime? SellerConfirmedAt { get; init; }

    /// <summary>Creacion (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Ultimo cambio (UTC).</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>Cuando se borra solo (RN-44: 30 dias; indice TTL <c>ttl_expires_at</c>).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Los datos del acuerdo. <c>null</c> es "No identificado" (RN-42).</summary>
[BsonNoId]
internal sealed class AgreementProposal
{
    /// <summary>Lo que se compra (hasta 80).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Precio acordado por pieza, en centavos.</summary>
    public long? PriceCents { get; init; }

    /// <summary>Piezas.</summary>
    public int? Quantity { get; init; }

    /// <summary>Punto de entrega (<c>meetingPoints.slug</c>).</summary>
    public string? PointSlug { get; init; }

    /// <summary>Copia del nombre del punto (R8).</summary>
    public string? PointName { get; init; }

    /// <summary>Precision libre del lugar (hasta 120), moderada.</summary>
    public string? Detail { get; init; }

    /// <summary>Fecha y hora de la entrega (UTC).</summary>
    public DateTime? At { get; init; }

    /// <summary>Otras condiciones acordadas ("Incluye la funda"), hasta 200, moderadas.</summary>
    public string? Conditions { get; init; }
}

/// <summary>Estados del acuerdo.</summary>
internal static class AgreementStatuses
{
    /// <summary>"Esperando confirmacion" (<c>Acuerdo-esperando</c>): 0 o 1 de 2.</summary>
    public const string Proposed = "proposed";

    /// <summary>"Acuerdo confirmado" (<c>Acuerdo-confirmado</c>): los dos, sobre la misma version (RN-41).</summary>
    public const string Confirmed = "confirmed";
}

/// <summary>Los datos que tiene que tener un acuerdo para confirmarse (nombres del wire).</summary>
internal static class AgreementFields
{
    /// <summary>Producto.</summary>
    public const string Title = "title";

    /// <summary>Precio acordado.</summary>
    public const string Price = "priceCents";

    /// <summary>Cantidad.</summary>
    public const string Quantity = "quantity";

    /// <summary>Punto de entrega.</summary>
    public const string MeetingPoint = "meetingPoint";

    /// <summary>Fecha y hora.</summary>
    public const string DeliveryAt = "deliveryAt";

    /// <summary>Los cinco, en el orden del resumen.</summary>
    public static readonly IReadOnlyList<string> Required = [Title, Price, Quantity, MeetingPoint, DeliveryAt];

    /// <summary>Los que faltan de una propuesta.</summary>
    /// <param name="p">La propuesta.</param>
    /// <returns>Los nombres.</returns>
    public static IReadOnlyList<string> Missing(AgreementProposal p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var missing = new List<string>(Required.Count);
        if (string.IsNullOrWhiteSpace(p.Title))
        {
            missing.Add(Title);
        }

        if (p.PriceCents is null)
        {
            missing.Add(Price);
        }

        if (p.Quantity is null)
        {
            missing.Add(Quantity);
        }

        if (p.PointSlug is null)
        {
            missing.Add(MeetingPoint);
        }

        if (p.At is null)
        {
            missing.Add(DeliveryAt);
        }

        return missing;
    }
}
