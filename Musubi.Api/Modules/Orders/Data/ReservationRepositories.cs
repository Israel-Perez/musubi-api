using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Orders.Data;

// Apartados (HU-52, maqueta Apartar, Apartado-listo, Mis-apartados, Verificar-comprador y Apartado-verificado). La
// validacion del servidor vive en db/028-apartados.js; fila canonica en docs/modelo-datos.md. Mongo e InMemory pasan
// la misma bateria (TestShared/Orders).

/// <summary>
/// Un apartado: quien compra aparta una pieza por 24 h para recogerla en un punto del campus. Quien vende lo verifica
/// con el QR o el codigo de 6 digitos y, al entregar, nace un pedido completado (decision de la usuaria del
/// 15-Nov-2026).
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class ReservationDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Consecutivo del campus: se muestra como <c>AP-NNNN</c>.</summary>
    public int Number { get; init; }

    /// <summary>Quien aparta.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string BuyerId { get; init; } = string.Empty;

    /// <summary>Quien vende.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string SellerId { get; init; } = string.Empty;

    /// <summary>Copia del nombre de quien aparta (R8).</summary>
    public string BuyerName { get; init; } = string.Empty;

    /// <summary>Copia del nombre de quien vende (R8).</summary>
    public string SellerName { get; init; } = string.Empty;

    /// <summary>La conversacion de los dos sobre la publicacion ("Mensaje"; el pedido que nace la usa).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ConversationId { get; init; } = string.Empty;

    /// <summary>La publicacion.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ProductId { get; init; } = string.Empty;

    /// <summary>Copia del nombre (R8).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Ruta de la portada (<c>/media/products/...</c>), o <c>null</c>.</summary>
    [BsonIgnoreIfNull]
    public string? Photo { get; init; }

    /// <summary>Precio de la pieza (copia, R8).</summary>
    public long PriceCents { get; init; }

    /// <summary>El punto de entrega (<c>meetingPoints.slug</c>).</summary>
    public string PointSlug { get; init; } = string.Empty;

    /// <summary>Copia del nombre del punto (R8).</summary>
    public string PointName { get; init; } = string.Empty;

    /// <summary>Cuando pasa a recogerlo (UTC).</summary>
    public DateTime PickupAt { get; init; }

    /// <summary>Codigo de 6 digitos de un solo uso; solo lo ve quien aparta (y va en su QR).</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Intentos de quien vende al escribir o escanear el codigo.</summary>
    [BsonIgnoreIfDefault]
    public int CodeAttempts { get; init; }

    /// <summary>Cuando quien vende verifico el codigo (UTC), o <c>null</c>.</summary>
    [BsonIgnoreIfNull]
    public DateTime? VerifiedAt { get; init; }

    /// <summary>El pedido completado que nacio al entregar, o <c>null</c>.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? OrderId { get; init; }

    /// <summary>Ver <see cref="ReservationStatuses"/>.</summary>
    public string Status { get; init; } = ReservationStatuses.Active;

    /// <summary>Quien cancelo (<see cref="OrderRoles"/>), o <c>null</c>.</summary>
    [BsonIgnoreIfNull]
    public string? CancelledBy { get; init; }

    /// <summary>Alta (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Vence a las 24 h del alta (UTC).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Ultimo cambio (UTC): cuando se recogio, se cancelo o vencio.</summary>
    public DateTime UpdatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Estados de un apartado.</summary>
internal static class ReservationStatuses
{
    /// <summary>Por recoger: la pieza esta guardada.</summary>
    public const string Active = "active";

    /// <summary>Recogido: nacio el pedido completado.</summary>
    public const string Collected = "collected";

    /// <summary>Cancelado por una de las partes (la pieza regresa).</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Vencio a las 24 h sin recogerse (la pieza regresa).</summary>
    public const string Expired = "expired";
}

/// <summary>Los numeros de los apartados.</summary>
internal static class ReservationNumbers
{
    /// <summary>El primero de cada campus.</summary>
    public const int First = 1001;

    /// <summary><c>AP-1001</c>.</summary>
    /// <param name="number">El consecutivo.</param>
    /// <returns>Como se muestra.</returns>
    public static string Display(int number) => "AP-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>El contador del campus en <c>counters</c>.</summary>
    /// <param name="communityId">Campus.</param>
    /// <returns>El id del contador.</returns>
    public static string CounterId(string communityId) => "reservations:" + communityId;
}

/// <summary>Apartados.</summary>
internal interface IReservationRepository
{
    /// <summary>El siguiente consecutivo del campus (AP-1001, AP-1002...), atomico.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El numero.</returns>
    Task<int> NextNumberAsync(string communityId, CancellationToken cancellationToken);

    /// <summary>Guarda un apartado si quien aparta no tiene otro activo (<c>ux_buyer_active</c>, 1 a la vez).</summary>
    /// <param name="reservation">El apartado.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>false</c> si ya tenia uno activo.</returns>
    Task<bool> TryInsertAsync(ReservationDocument reservation, CancellationToken cancellationToken);

    /// <summary>Un apartado por id, o <c>null</c>.</summary>
    /// <param name="id">Id.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado o <c>null</c>.</returns>
    Task<ReservationDocument?> FindByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Los apartados de una persona como quien compra o como quien vende, del mas nuevo al mas viejo.</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="asSeller"><c>true</c> para "Me apartaron".</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los apartados.</returns>
    Task<IReadOnlyList<ReservationDocument>> FindByUserAsync(string userId, bool asSeller, int limit, CancellationToken cancellationToken);

    /// <summary>Cuantos apartados tiene una persona como quien compra y como quien vende.</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los dos numeros.</returns>
    Task<(long Bought, long Sold)> CountByUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gasta un intento de quien vende con un apartado activo (atomico: con los intentos agotados no cambia nada).
    /// </summary>
    /// <param name="id">El apartado.</param>
    /// <param name="sellerId">Quien vende (va en el filtro).</param>
    /// <param name="maxAttempts">Intentos permitidos.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado con el intento contado, o <c>null</c>.</returns>
    Task<ReservationDocument?> UseAttemptAsync(string id, string sellerId, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>Marca el codigo como verificado (repetirlo conserva la primera hora).</summary>
    /// <param name="id">El apartado.</param>
    /// <param name="sellerId">Quien vende.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado, o <c>null</c> si ya no esta activo.</returns>
    Task<ReservationDocument?> MarkVerifiedAsync(string id, string sellerId, DateTime now, CancellationToken cancellationToken);

    /// <summary>"Confirmar entrega": de activo y verificado a recogido, con el pedido que nacio.</summary>
    /// <param name="id">El apartado.</param>
    /// <param name="sellerId">Quien vende.</param>
    /// <param name="orderId">El pedido completado.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado recogido, o <c>null</c> si no cambio (dos toques a la vez).</returns>
    Task<ReservationDocument?> MarkCollectedAsync(string id, string sellerId, string orderId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Una de las partes cancela un apartado activo.</summary>
    /// <param name="id">El apartado.</param>
    /// <param name="userId">Quien cancela.</param>
    /// <param name="role">Su papel (<see cref="OrderRoles"/>): va en el filtro.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado cancelado, o <c>null</c> si no cambio.</returns>
    Task<ReservationDocument?> CancelAsync(string id, string userId, string role, DateTime now, CancellationToken cancellationToken);

    /// <summary>Los apartados activos que ya vencieron (para regresar la pieza), del mas viejo al mas nuevo.</summary>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los apartados.</returns>
    Task<IReadOnlyList<ReservationDocument>> FindExpiredAsync(DateTime now, int limit, CancellationToken cancellationToken);

    /// <summary>Marca un apartado activo como vencido (solo una vez, aunque corran dos instancias).</summary>
    /// <param name="id">El apartado.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si lo marco esta llamada.</returns>
    Task<bool> MarkExpiredAsync(string id, DateTime now, CancellationToken cancellationToken);

    /// <summary>Los apartados activos de una persona (para regresar las piezas al borrar su cuenta, HU-46).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los apartados.</returns>
    Task<IReadOnlyList<ReservationDocument>> FindActiveByUserAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre MongoDB.</summary>
internal sealed class MongoReservationRepository(IMongoDatabase database) : IReservationRepository
{
    private static readonly FilterDefinitionBuilder<ReservationDocument> Filter = Builders<ReservationDocument>.Filter;

    private readonly IMongoCollection<ReservationDocument> reservations = database.GetCollection<ReservationDocument>(MongoSchema.Reservations);
    private readonly IMongoCollection<BsonDocument> counters = database.GetCollection<BsonDocument>(MongoSchema.Counters);

    private static FindOneAndUpdateOptions<ReservationDocument> After => new() { ReturnDocument = ReturnDocument.After };

    // La parte va en el filtro (OWASP API1).
    private static FilterDefinition<ReservationDocument> Party(string userId, string role) =>
        string.Equals(role, OrderRoles.Seller, StringComparison.Ordinal)
            ? Filter.Eq(r => r.SellerId, userId)
            : Filter.Eq(r => r.BuyerId, userId);

    /// <inheritdoc />
    public async Task<int> NextNumberAsync(string communityId, CancellationToken cancellationToken)
    {
        var counter = await counters.FindOneAndUpdateAsync(
            new BsonDocument("_id", ReservationNumbers.CounterId(communityId)),
            new BsonDocument("$inc", new BsonDocument("seq", 1)),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);
        return ReservationNumbers.First - 1 + counter["seq"].ToInt32();
    }

    /// <inheritdoc />
    public async Task<bool> TryInsertAsync(ReservationDocument reservation, CancellationToken cancellationToken)
    {
        try
        {
            await reservations.InsertOneAsync(reservation, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // ux_buyer_active: uno activo por persona (decision de la usuaria del 15-Nov-2026).
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<ReservationDocument?> FindByIdAsync(string id, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await reservations.Find(r => r.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReservationDocument>> FindByUserAsync(string userId, bool asSeller, int limit, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return [];
        }

        var role = asSeller ? OrderRoles.Seller : OrderRoles.Buyer;
        return await reservations.Find(Party(userId, role))
            .SortByDescending(r => r.Id)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<(long Bought, long Sold)> CountByUserAsync(string userId, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return (0, 0);
        }

        var bought = await reservations.CountDocumentsAsync(Filter.Eq(r => r.BuyerId, userId), cancellationToken: cancellationToken).ConfigureAwait(false);
        var sold = await reservations.CountDocumentsAsync(Filter.Eq(r => r.SellerId, userId), cancellationToken: cancellationToken).ConfigureAwait(false);
        return (bought, sold);
    }

    /// <inheritdoc />
    public async Task<ReservationDocument?> UseAttemptAsync(string id, string sellerId, int maxAttempts, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _) && ObjectId.TryParse(sellerId, out _)
            ? await reservations.FindOneAndUpdateAsync(
                Filter.Eq(r => r.Id, id) & Filter.Eq(r => r.SellerId, sellerId) & Filter.Eq(r => r.Status, ReservationStatuses.Active)
                    & (Filter.Exists(r => r.CodeAttempts, exists: false) | Filter.Lt(r => r.CodeAttempts, maxAttempts)),
                Builders<ReservationDocument>.Update.Inc(r => r.CodeAttempts, 1),
                After,
                cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<ReservationDocument?> MarkVerifiedAsync(string id, string sellerId, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _) && ObjectId.TryParse(sellerId, out _)
            ? await reservations.FindOneAndUpdateAsync(
                Filter.Eq(r => r.Id, id) & Filter.Eq(r => r.SellerId, sellerId) & Filter.Eq(r => r.Status, ReservationStatuses.Active),
                Builders<ReservationDocument>.Update.Min("verifiedAt", now),
                After,
                cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<ReservationDocument?> MarkCollectedAsync(string id, string sellerId, string orderId, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _) && ObjectId.TryParse(sellerId, out _)
            ? await reservations.FindOneAndUpdateAsync(
                Filter.Eq(r => r.Id, id) & Filter.Eq(r => r.SellerId, sellerId) & Filter.Eq(r => r.Status, ReservationStatuses.Active)
                    & Filter.Exists(r => r.VerifiedAt),
                Builders<ReservationDocument>.Update
                    .Set(r => r.Status, ReservationStatuses.Collected)
                    .Set(r => r.OrderId, orderId)
                    .Set(r => r.UpdatedAt, now),
                After,
                cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<ReservationDocument?> CancelAsync(string id, string userId, string role, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _) && ObjectId.TryParse(userId, out _)
            ? await reservations.FindOneAndUpdateAsync(
                Filter.Eq(r => r.Id, id)
                    & Party(userId, role)
                    & Filter.Eq(r => r.Status, ReservationStatuses.Active),
                Builders<ReservationDocument>.Update
                    .Set(r => r.Status, ReservationStatuses.Cancelled)
                    .Set(r => r.CancelledBy, role)
                    .Set(r => r.UpdatedAt, now),
                After,
                cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReservationDocument>> FindExpiredAsync(DateTime now, int limit, CancellationToken cancellationToken) =>
        await reservations.Find(Filter.Eq(r => r.Status, ReservationStatuses.Active) & Filter.Lte(r => r.ExpiresAt, now))
            .SortBy(r => r.ExpiresAt)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> MarkExpiredAsync(string id, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
        && (await reservations.UpdateOneAsync(
            Filter.Eq(r => r.Id, id) & Filter.Eq(r => r.Status, ReservationStatuses.Active),
            Builders<ReservationDocument>.Update.Set(r => r.Status, ReservationStatuses.Expired).Set(r => r.UpdatedAt, now),
            cancellationToken: cancellationToken).ConfigureAwait(false)).ModifiedCount == 1;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReservationDocument>> FindActiveByUserAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? await reservations.Find((Filter.Eq(r => r.BuyerId, userId) | Filter.Eq(r => r.SellerId, userId)) & Filter.Eq(r => r.Status, ReservationStatuses.Active))
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];
}

/// <summary>Apartados en memoria (Development y pruebas).</summary>
internal sealed class InMemoryReservationRepository : IReservationRepository
{
    private readonly Lock gate = new();
    private readonly List<ReservationDocument> reservations = [];
    private readonly Dictionary<string, int> counters = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<int> NextNumberAsync(string communityId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var seq = counters.GetValueOrDefault(communityId) + 1;
            counters[communityId] = seq;
            return Task.FromResult(ReservationNumbers.First - 1 + seq);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryInsertAsync(ReservationDocument reservation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        lock (gate)
        {
            if (reservations.Exists(r => string.Equals(r.BuyerId, reservation.BuyerId, StringComparison.Ordinal) && r.Status == ReservationStatuses.Active))
            {
                return Task.FromResult(false);
            }

            reservations.Add(reservation);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<ReservationDocument?> FindByIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(reservations.Find(r => string.Equals(r.Id, id, StringComparison.Ordinal)));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReservationDocument>> FindByUserAsync(string userId, bool asSeller, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<ReservationDocument> found = reservations
                .Where(r => string.Equals(asSeller ? r.SellerId : r.BuyerId, userId, StringComparison.Ordinal))
                .OrderByDescending(r => r.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<(long Bought, long Sold)> CountByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((
                (long)reservations.Count(r => string.Equals(r.BuyerId, userId, StringComparison.Ordinal)),
                (long)reservations.Count(r => string.Equals(r.SellerId, userId, StringComparison.Ordinal))));
        }
    }

    /// <inheritdoc />
    public Task<ReservationDocument?> UseAttemptAsync(string id, string sellerId, int maxAttempts, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            r => IsSellers(r, id, sellerId) && r.Status == ReservationStatuses.Active && r.CodeAttempts < maxAttempts,
            r => Copy(r, codeAttempts: r.CodeAttempts + 1)));

    /// <inheritdoc />
    public Task<ReservationDocument?> MarkVerifiedAsync(string id, string sellerId, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            r => IsSellers(r, id, sellerId) && r.Status == ReservationStatuses.Active,
            r => Copy(r, verifiedAt: r.VerifiedAt ?? now)));

    /// <inheritdoc />
    public Task<ReservationDocument?> MarkCollectedAsync(string id, string sellerId, string orderId, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            r => IsSellers(r, id, sellerId) && r.Status == ReservationStatuses.Active && r.VerifiedAt is not null,
            r => Copy(r, status: ReservationStatuses.Collected, orderId: orderId, updatedAt: now)));

    /// <inheritdoc />
    public Task<ReservationDocument?> CancelAsync(string id, string userId, string role, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            r => string.Equals(r.Id, id, StringComparison.Ordinal)
                && string.Equals(string.Equals(role, OrderRoles.Seller, StringComparison.Ordinal) ? r.SellerId : r.BuyerId, userId, StringComparison.Ordinal)
                && r.Status == ReservationStatuses.Active,
            r => Copy(r, status: ReservationStatuses.Cancelled, cancelledBy: role, updatedAt: now)));

    /// <inheritdoc />
    public Task<IReadOnlyList<ReservationDocument>> FindExpiredAsync(DateTime now, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<ReservationDocument> found = reservations
                .Where(r => r.Status == ReservationStatuses.Active && r.ExpiresAt <= now)
                .OrderBy(r => r.ExpiresAt)
                .Take(limit)
                .ToList();
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<bool> MarkExpiredAsync(string id, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            r => string.Equals(r.Id, id, StringComparison.Ordinal) && r.Status == ReservationStatuses.Active,
            r => Copy(r, status: ReservationStatuses.Expired, updatedAt: now)) is not null);

    /// <inheritdoc />
    public Task<IReadOnlyList<ReservationDocument>> FindActiveByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<ReservationDocument> found = reservations
                .Where(r => (string.Equals(r.BuyerId, userId, StringComparison.Ordinal) || string.Equals(r.SellerId, userId, StringComparison.Ordinal))
                    && r.Status == ReservationStatuses.Active)
                .ToList();
            return Task.FromResult(found);
        }
    }

    private static bool IsSellers(ReservationDocument r, string id, string sellerId) =>
        string.Equals(r.Id, id, StringComparison.Ordinal) && string.Equals(r.SellerId, sellerId, StringComparison.Ordinal);

    private static ReservationDocument Copy(
        ReservationDocument r,
        string? status = null,
        int? codeAttempts = null,
        DateTime? verifiedAt = null,
        string? orderId = null,
        string? cancelledBy = null,
        DateTime? updatedAt = null) => new()
        {
            Id = r.Id,
            CommunityId = r.CommunityId,
            Number = r.Number,
            BuyerId = r.BuyerId,
            SellerId = r.SellerId,
            BuyerName = r.BuyerName,
            SellerName = r.SellerName,
            ConversationId = r.ConversationId,
            ProductId = r.ProductId,
            Title = r.Title,
            Photo = r.Photo,
            PriceCents = r.PriceCents,
            PointSlug = r.PointSlug,
            PointName = r.PointName,
            PickupAt = r.PickupAt,
            Code = r.Code,
            CodeAttempts = codeAttempts ?? r.CodeAttempts,
            VerifiedAt = verifiedAt ?? r.VerifiedAt,
            OrderId = orderId ?? r.OrderId,
            Status = status ?? r.Status,
            CancelledBy = cancelledBy ?? r.CancelledBy,
            CreatedAt = r.CreatedAt,
            ExpiresAt = r.ExpiresAt,
            UpdatedAt = updatedAt ?? r.UpdatedAt,
            SchemaVersion = r.SchemaVersion,
        };

    private ReservationDocument? Change(Func<ReservationDocument, bool> when, Func<ReservationDocument, ReservationDocument> change)
    {
        lock (gate)
        {
            var i = reservations.FindIndex(r => when(r));
            if (i < 0)
            {
                return null;
            }

            return reservations[i] = change(reservations[i]);
        }
    }
}
