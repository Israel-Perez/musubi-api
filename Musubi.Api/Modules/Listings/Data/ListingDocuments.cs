using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.Listings.Data;

// Documentos de persistencia de M-03 (HU-06, HU-50). La validacion del servidor vive en
// db/002-publicaciones.js; fila canonica en docs/modelo-datos.md.

/// <summary>Categoria de una comunidad (la administra HU-41; por ahora la siembra el script).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class CategoryDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Identificador estable (<c>calculadoras</c>); lo usan la app y los productos.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Nombre visible ("Calculadoras").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Orden en la app.</summary>
    public int Order { get; init; }

    /// <summary>Se puede elegir al publicar.</summary>
    public bool Active { get; init; } = true;

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Publicacion de un producto (HU-06).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class ProductDocument
{
    /// <summary>Id (ObjectId: ordenado por tiempo, sirve de cursor; R2).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Vendedor.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string SellerId { get; init; } = string.Empty;

    /// <summary>Copia para leer sin joins (R8): nombre corto ("Ana R.").</summary>
    public ProductSeller Seller { get; init; } = new();

    /// <summary>Nombre del producto.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Descripcion (opcional, hasta 500).</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Categoria (<see cref="CategoryDocument.Slug"/>).</summary>
    public string CategorySlug { get; init; } = string.Empty;

    /// <summary>Precio en centavos (entero; nunca punto flotante).</summary>
    public long PriceCents { get; init; }

    /// <summary><c>new</c> o <c>used</c>.</summary>
    public string Condition { get; init; } = ProductConditions.Used;

    /// <summary>Solo usado: <c>likeNew</c>, <c>good</c> o <c>fair</c>.</summary>
    public string? ConditionDetail { get; init; }

    /// <summary>Piezas disponibles (HU-50). Se descuenta con <c>$inc</c> condicionado (R7).</summary>
    public int Stock { get; init; }

    /// <summary>Ver <see cref="ProductStatuses"/>.</summary>
    public string Status { get; init; } = ProductStatuses.Active;

    /// <summary>Llaves de S3 (1 a 8; la primera es la portada).</summary>
    public IReadOnlyList<string> Photos { get; init; } = [];

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Publicacion (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Ultimo cambio (UTC).</summary>
    public DateTime UpdatedAt { get; init; }
}

/// <summary>Copia de lectura del vendedor dentro de la publicacion.</summary>
internal sealed class ProductSeller
{
    /// <summary>Nombre corto ("Ana R.").</summary>
    public string Name { get; init; } = string.Empty;
}

/// <summary>Estados de una publicacion.</summary>
internal static class ProductStatuses
{
    /// <summary>Disponible (con piezas).</summary>
    public const string Active = "active";

    /// <summary>Sin piezas: sigue visible como "Agotado" (HU-50).</summary>
    public const string SoldOut = "soldOut";

    /// <summary>Pausada por su vendedor (HU-07).</summary>
    public const string Hidden = "hidden";

    /// <summary>Quitada por moderacion (HU-39).</summary>
    public const string Removed = "removed";
}

/// <summary>Estado del producto (maqueta <c>Vender-4</c>).</summary>
internal static class ProductConditions
{
    /// <summary>Sin usar, en su empaque.</summary>
    public const string New = "new";

    /// <summary>Ya tuvo uso.</summary>
    public const string Used = "used";

    /// <summary>Detalle de un producto usado.</summary>
    public static readonly IReadOnlyList<string> UsedDetails = ["likeNew", "good", "fair"];
}
