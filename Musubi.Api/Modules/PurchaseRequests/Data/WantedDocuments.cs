using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.PurchaseRequests.Data;

// Documentos de persistencia de M-05 (HU-13). La validacion del servidor vive en db/009-solicitudes.js;
// fila canonica en docs/modelo-datos.md.

/// <summary>Solicitud de compra "Se busca" (HU-13): lo que alguien necesita, con presupuesto.</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class WantedRequestDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Quien busca.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string RequesterId { get; init; } = string.Empty;

    /// <summary>Copia del nombre corto de quien busca ("Ana R."; R8: el nombre no se edita).</summary>
    public string RequesterName { get; init; } = string.Empty;

    /// <summary>"Producto que buscas" (3 a 80 caracteres).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Slug de la categoria, o <c>otro</c>.</summary>
    public string CategorySlug { get; init; } = string.Empty;

    /// <summary>Copia del nombre de la categoria ("Calculadoras", "Otro").</summary>
    public string CategoryName { get; init; } = string.Empty;

    /// <summary>"Cuentanos mas" (opcional, hasta 300 caracteres).</summary>
    public string? Description { get; init; }

    /// <summary>"Caracteristicas deseadas" (opcional, hasta 5).</summary>
    public IReadOnlyList<string> Features { get; init; } = [];

    /// <summary>Presupuesto maximo en centavos.</summary>
    public long BudgetCents { get; init; }

    /// <summary>Piezas que necesita (1 a 10).</summary>
    public int Quantity { get; init; } = 1;

    /// <summary>"Para cuando la necesitas" (dia del campus a medianoche UTC), o <c>null</c>.</summary>
    public DateTime? NeededBy { get; init; }

    /// <summary>Cuando deja de estar activa (RN-10): al terminar <see cref="NeededBy"/> o 30 dias despues de publicarse.</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary><c>active</c>; HU-14 agrega cerrada y cancelada. Vencida se calcula con <see cref="ExpiresAt"/>.</summary>
    public string Status { get; init; } = WantedStatuses.Active;

    /// <summary>Ofertas recibidas ("Yo tengo este producto", HU-16; <c>$inc</c>).</summary>
    public int OffersCount { get; init; }

    /// <summary>Creacion (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Estados de una solicitud (RN-11).</summary>
internal static class WantedStatuses
{
    /// <summary>Recibe ofertas.</summary>
    public const string Active = "active";

    /// <summary>Ya consiguio el producto (HU-14).</summary>
    public const string Closed = "closed";

    /// <summary>La cancelo quien la publico (HU-14).</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Paso su fecha (RN-10). No se guarda: se calcula al leer con <c>expiresAt</c>.</summary>
    public const string Expired = "expired";
}
