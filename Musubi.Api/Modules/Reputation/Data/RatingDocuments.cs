using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.Reputation.Data;

/// <summary>
/// Una calificacion de un pedido completado (HU-27 quien compra califica a quien vende; HU-45 al reves). Coleccion
/// <c>ratings</c> (<c>db/020-calificaciones.js</c>): una por pedido y por quien califica (<c>ux_order_rater</c>, RN-24).
/// </summary>
internal sealed class RatingDocument
{
    /// <summary>Id.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Campus del pedido.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>El pedido.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string OrderId { get; init; } = string.Empty;

    /// <summary>Quien califica.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string RaterId { get; init; } = string.Empty;

    /// <summary>Nombre corto de quien califica ("Luis M."; "Usuario eliminado" al borrar su cuenta, R11).</summary>
    public string RaterName { get; init; } = string.Empty;

    /// <summary>Quien recibe la calificacion.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string RatedId { get; init; } = string.Empty;

    /// <summary>Como participo quien la recibe: ver <see cref="RatedRoles"/>.</summary>
    public string RatedRole { get; init; } = RatedRoles.Seller;

    /// <summary>De 1 a 5 (RN-25).</summary>
    public int Stars { get; init; }

    /// <summary>"¿Que destacas?": codigos de <see cref="RatingTags"/> que aplican a <see cref="RatedRole"/>.</summary>
    public List<string> Tags { get; init; } = [];

    /// <summary>"Cuentale a la comunidad" (opcional, moderado), o <c>null</c>.</summary>
    [BsonIgnoreIfNull]
    public string? Comment { get; init; }

    /// <summary>Alta (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Como participo en el pedido quien recibe la calificacion.</summary>
internal static class RatedRoles
{
    /// <summary>Quien vende (la califica quien compra, HU-27).</summary>
    public const string Seller = "seller";

    /// <summary>Quien compra (la califica quien vende, HU-45).</summary>
    public const string Buyer = "buyer";
}

/// <summary>
/// "¿Que destacas?" (maqueta <c>Calificar</c>). Quien compra ve los cinco de la maqueta; quien vende, los que aplican a
/// una persona que compra (decision de la usuaria del 12-Nov-2026).
/// </summary>
internal static class RatingTags
{
    /// <summary>"Puntual": de aqui sale "A tiempo" del perfil.</summary>
    public const string OnTime = "on_time";

    /// <summary>"Como se describio".</summary>
    public const string AsDescribed = "as_described";

    /// <summary>"Buena comunicacion".</summary>
    public const string GoodCommunication = "good_communication";

    /// <summary>"Precio justo".</summary>
    public const string FairPrice = "fair_price";

    /// <summary>"Amable".</summary>
    public const string Friendly = "friendly";

    /// <summary>"Pago lo acordado" (solo para quien compra).</summary>
    public const string PaidAsAgreed = "paid_as_agreed";

    /// <summary>Los que se le ponen a quien vende.</summary>
    public static readonly IReadOnlyList<string> ForSeller = [OnTime, AsDescribed, GoodCommunication, FairPrice, Friendly];

    /// <summary>Los que se le ponen a quien compra.</summary>
    public static readonly IReadOnlyList<string> ForBuyer = [OnTime, GoodCommunication, Friendly, PaidAsAgreed];

    /// <summary>La lista que aplica a un rol.</summary>
    /// <param name="ratedRole">Ver <see cref="RatedRoles"/>.</param>
    /// <returns>Los codigos.</returns>
    public static IReadOnlyList<string> For(string ratedRole) =>
        string.Equals(ratedRole, RatedRoles.Buyer, StringComparison.Ordinal) ? ForBuyer : ForSeller;
}
