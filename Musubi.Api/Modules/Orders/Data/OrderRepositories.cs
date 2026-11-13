using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Orders.Data;

// Persistencia de los pedidos (HU-23). Mongo e InMemory pasan la misma bateria (TestShared/Orders).

/// <summary>Pedidos.</summary>
internal interface IOrderRepository
{
    /// <summary>El siguiente consecutivo del campus (MS-1001, MS-1002...), atomico.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El numero.</returns>
    Task<int> NextNumberAsync(string communityId, CancellationToken cancellationToken);

    /// <summary>Guarda un pedido si su conversacion no tiene otro abierto (<c>ux_conversation_open</c>).</summary>
    /// <param name="order">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>false</c> si ya habia uno abierto (dos toques a la vez).</returns>
    Task<bool> TryInsertAsync(OrderDocument order, CancellationToken cancellationToken);

    /// <summary>Un pedido por id, o <c>null</c>.</summary>
    /// <param name="id">Id.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido o <c>null</c>.</returns>
    Task<OrderDocument?> FindByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Los pedidos de una conversacion, del mas nuevo al mas viejo.</summary>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los pedidos.</returns>
    Task<IReadOnlyList<OrderDocument>> FindByConversationAsync(string conversationId, int limit, CancellationToken cancellationToken);

    /// <summary>Registra como se paga un pedido pendiente de quien compra (RN-18). Elegir de nuevo lo reemplaza.</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="buyerId">Quien compra (va en el filtro, OWASP API1).</param>
    /// <param name="method">Ver <see cref="PaymentMethods"/>.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido cambiado, o <c>null</c> si no es suyo o ya no esta pendiente.</returns>
    Task<OrderDocument?> SetPaymentAsync(string id, string buyerId, string method, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Quien vende acepta un pedido pendiente con el pago ya elegido (HU-24, RF-34). El vendedor, el estado y el pago
    /// van en el filtro (OWASP API1; dos toques a la vez no lo aceptan dos veces).
    /// </summary>
    /// <param name="id">El pedido.</param>
    /// <param name="sellerId">Quien vende (del token).</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido confirmado, o <c>null</c> si no cambio.</returns>
    Task<OrderDocument?> AcceptAsync(string id, string sellerId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Una de las partes cancela (o rechaza) un pedido pendiente o confirmado, con su motivo (HU-24, RN-20).</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="userId">Quien cancela (del token).</param>
    /// <param name="role">Su papel en el pedido (<see cref="OrderRoles"/>): va en el filtro.</param>
    /// <param name="reason">Ver <see cref="CancelReasons"/>.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido cancelado (su <c>history</c> dice de que estado venia), o <c>null</c> si no cambio.</returns>
    Task<OrderDocument?> CancelAsync(string id, string userId, string role, string reason, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Una de las partes confirma su lado de la entrega en un pedido confirmado (HU-24, RN-19). Repetirlo conserva la
    /// primera hora.
    /// </summary>
    /// <param name="id">El pedido.</param>
    /// <param name="userId">Quien confirma (del token).</param>
    /// <param name="role">Su papel (<see cref="OrderRoles"/>).</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido, o <c>null</c> si no es suyo o no esta confirmado.</returns>
    Task<OrderDocument?> ConfirmHandoverAsync(string id, string userId, string role, DateTime now, CancellationToken cancellationToken);

    /// <summary>Completa un pedido confirmado cuando ya confirmaron la entrega los dos (HU-24, RN-19).</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido completado, o <c>null</c> si falta alguno o ya se completo.</returns>
    Task<OrderDocument?> CompleteAsync(string id, DateTime now, CancellationToken cancellationToken);

    /// <summary>Los pedidos confirmados de una persona (las piezas que se liberan al borrar su cuenta, HU-24).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los pedidos.</returns>
    Task<IReadOnlyList<OrderDocument>> FindConfirmedByUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Marca que una parte califico un pedido completado (HU-27, HU-45). Atomico: si ya califico, o el pedido no esta
    /// completado o no es suyo, no cambia nada (RN-24).
    /// </summary>
    /// <param name="id">El pedido.</param>
    /// <param name="userId">Quien califica.</param>
    /// <param name="role">Su parte (<see cref="OrderRoles"/>).</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si se marco.</returns>
    Task<bool> MarkRatedAsync(string id, string userId, string role, DateTime now, CancellationToken cancellationToken);

    /// <summary>"Mis pedidos" (HU-25): los de quien compra, del mas nuevo al mas viejo (indice <c>ix_buyer_id</c>).</summary>
    /// <param name="buyerId">Quien compra (del token).</param>
    /// <param name="statuses">Los estados de la pestana, o <c>null</c> para todos.</param>
    /// <param name="beforeId">Cursor: solo los anteriores a este id, o <c>null</c> desde el principio.</param>
    /// <param name="limit">Cuantos.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los pedidos.</returns>
    Task<IReadOnlyList<OrderDocument>> FindByBuyerAsync(
        string buyerId,
        IReadOnlyList<string>? statuses,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>"Vendidos" (HU-26): las ventas completadas de quien vende, de la mas nueva a la mas vieja (<c>ix_seller_id</c>).</summary>
    /// <param name="sellerId">Quien vende (del token).</param>
    /// <param name="beforeId">Cursor: solo las anteriores a este id, o <c>null</c>.</param>
    /// <param name="limit">Cuantas.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los pedidos completados.</returns>
    Task<IReadOnlyList<OrderDocument>> FindSalesAsync(string sellerId, string? beforeId, int limit, CancellationToken cancellationToken);

    /// <summary>Cuantas ventas completadas tiene quien vende y su total en centavos (la tarjeta de "Vendidos").</summary>
    /// <param name="sellerId">Quien vende.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El numero y el total.</returns>
    Task<(long Count, long TotalCents)> SalesSummaryAsync(string sellerId, CancellationToken cancellationToken);

    /// <summary>Las publicaciones de quien vende con al menos un pedido completado (HU-26).</summary>
    /// <param name="sellerId">Quien vende.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los ids, sin repetir.</returns>
    Task<IReadOnlyCollection<string>> SoldProductIdsAsync(string sellerId, CancellationToken cancellationToken);

    /// <summary>Cuantos pedidos tiene quien compra en cada estado (los numeros de las pestanas de HU-25).</summary>
    /// <param name="buyerId">Quien compra.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Estado → cuantos.</returns>
    Task<IReadOnlyDictionary<string, long>> CountByBuyerAsync(string buyerId, CancellationToken cancellationToken);

    /// <summary>Si una publicacion tiene pedidos pendientes o confirmados (RN-07; indice <c>ix_product_status</c>).</summary>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si tiene.</returns>
    Task<bool> HasOpenForProductAsync(string productId, CancellationToken cancellationToken);

    /// <summary>
    /// Borrado de una cuenta (HU-46, R11): cancela sus pedidos abiertos y reemplaza su nombre en los que conserva la
    /// otra parte.
    /// </summary>
    /// <param name="userId">La persona.</param>
    /// <param name="replacementName">El nombre que queda.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos pedidos abiertos se cancelaron.</returns>
    Task<long> ReleaseAccountAsync(string userId, string replacementName, DateTime now, CancellationToken cancellationToken);
}

/// <summary>Puntos de entrega del campus.</summary>
internal interface IMeetingPointRepository
{
    /// <summary>Los puntos activos del campus, en su orden.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los puntos.</returns>
    Task<IReadOnlyList<MeetingPointDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken);
}

/// <summary>Pedidos en MongoDB (indices de db/014-pedidos.js).</summary>
internal sealed class MongoOrderRepository(IMongoDatabase database) : IOrderRepository
{
    private static readonly FilterDefinitionBuilder<OrderDocument> Filter = Builders<OrderDocument>.Filter;

    private readonly IMongoCollection<OrderDocument> orders = database.GetCollection<OrderDocument>(MongoSchema.Orders);
    private readonly IMongoCollection<BsonDocument> counters = database.GetCollection<BsonDocument>(MongoSchema.Counters);

    /// <inheritdoc />
    public async Task<int> NextNumberAsync(string communityId, CancellationToken cancellationToken)
    {
        var counter = await counters.FindOneAndUpdateAsync(
            new BsonDocument("_id", OrderNumbers.CounterId(communityId)),
            new BsonDocument("$inc", new BsonDocument("seq", 1)),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);
        return OrderNumbers.First - 1 + counter["seq"].ToInt32();
    }

    /// <inheritdoc />
    public async Task<bool> TryInsertAsync(OrderDocument order, CancellationToken cancellationToken)
    {
        try
        {
            await orders.InsertOneAsync(order, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // ux_conversation_open: gana el primero de dos toques a la vez.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<OrderDocument?> FindByIdAsync(string id, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await orders.Find(o => o.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderDocument>> FindByConversationAsync(string conversationId, int limit, CancellationToken cancellationToken) =>
        ObjectId.TryParse(conversationId, out _)
            ? await orders.Find(o => o.ConversationId == conversationId)
                .SortByDescending(o => o.Id)
                .Limit(limit)
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

    /// <inheritdoc />
    public async Task<OrderDocument?> SetPaymentAsync(string id, string buyerId, string method, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _) && ObjectId.TryParse(buyerId, out _)
            ? await orders.FindOneAndUpdateAsync(
                Filter.Eq(o => o.Id, id) & Filter.Eq(o => o.BuyerId, buyerId) & Filter.Eq(o => o.Status, OrderStatuses.Pending),
                Builders<OrderDocument>.Update
                    .Set(o => o.Payment, new OrderPayment { Method = method, ChosenAt = now })
                    .Set(o => o.UpdatedAt, now),
                new FindOneAndUpdateOptions<OrderDocument> { ReturnDocument = ReturnDocument.After },
                cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<OrderDocument?> AcceptAsync(string id, string sellerId, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _) && ObjectId.TryParse(sellerId, out _)
            ? await orders.FindOneAndUpdateAsync(
                Filter.Eq(o => o.Id, id) & Filter.Eq(o => o.SellerId, sellerId) & Filter.Eq(o => o.Status, OrderStatuses.Pending)
                    & Filter.Exists(o => o.Payment),
                Builders<OrderDocument>.Update
                    .Set(o => o.Status, OrderStatuses.Confirmed)
                    .Set(o => o.UpdatedAt, now)
                    .Push(o => o.History, new OrderTransition { Status = OrderStatuses.Confirmed, At = now }),
                After,
                cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<OrderDocument?> CancelAsync(string id, string userId, string role, string reason, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _) && ObjectId.TryParse(userId, out _)
            ? await orders.FindOneAndUpdateAsync(
                Filter.Eq(o => o.Id, id) & Party(userId, role) & Filter.In(o => o.Status, OrderStatuses.Open),
                Builders<OrderDocument>.Update
                    .Set(o => o.Status, OrderStatuses.Cancelled)
                    .Set(o => o.Cancellation, new OrderCancellation { By = role, Reason = reason, At = now })
                    .Set(o => o.UpdatedAt, now)
                    .Push(o => o.History, new OrderTransition { Status = OrderStatuses.Cancelled, At = now, Reason = reason }),
                After,
                cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<OrderDocument?> ConfirmHandoverAsync(string id, string userId, string role, DateTime now, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out _) || !ObjectId.TryParse(userId, out _))
        {
            return null;
        }

        // $ifNull conserva la primera hora: confirmar dos veces no cambia nada.
        var field = string.Equals(role, OrderRoles.Seller, StringComparison.Ordinal) ? "handover.sellerAt" : "handover.buyerAt";
        var stage = new BsonDocument("$set", new BsonDocument
        {
            { field, new BsonDocument("$ifNull", new BsonArray { "$" + field, new BsonDateTime(now) }) },
            { "updatedAt", new BsonDateTime(now) },
        });
        return await orders.FindOneAndUpdateAsync(
            Filter.Eq(o => o.Id, id) & Party(userId, role) & Filter.Eq(o => o.Status, OrderStatuses.Confirmed),
            new PipelineUpdateDefinition<OrderDocument>(new BsonDocumentStagePipelineDefinition<OrderDocument, OrderDocument>([stage])),
            After,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OrderDocument?> CompleteAsync(string id, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await orders.FindOneAndUpdateAsync(
                Filter.Eq(o => o.Id, id) & Filter.Eq(o => o.Status, OrderStatuses.Confirmed)
                    & Filter.Exists("handover.buyerAt") & Filter.Exists("handover.sellerAt"),
                Builders<OrderDocument>.Update
                    .Set(o => o.Status, OrderStatuses.Completed)
                    .Set(o => o.UpdatedAt, now)
                    .Push(o => o.History, new OrderTransition { Status = OrderStatuses.Completed, At = now }),
                After,
                cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderDocument>> FindConfirmedByUserAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? await orders.Find((Filter.Eq(o => o.BuyerId, userId) | Filter.Eq(o => o.SellerId, userId)) & Filter.Eq(o => o.Status, OrderStatuses.Confirmed))
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

    /// <inheritdoc />
    public async Task<bool> MarkRatedAsync(string id, string userId, string role, DateTime now, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out _) || !ObjectId.TryParse(userId, out _))
        {
            return false;
        }

        var field = string.Equals(role, OrderRoles.Seller, StringComparison.Ordinal) ? "ratings.sellerAt" : "ratings.buyerAt";
        var result = await orders.UpdateOneAsync(
            Filter.Eq(o => o.Id, id) & Party(userId, role) & Filter.Eq(o => o.Status, OrderStatuses.Completed) & Filter.Exists(field, exists: false),
            Builders<OrderDocument>.Update.Set(field, now),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderDocument>> FindByBuyerAsync(
        string buyerId,
        IReadOnlyList<string>? statuses,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(o => o.BuyerId, buyerId);
        if (statuses is not null)
        {
            filter &= Filter.In(o => o.Status, statuses);
        }

        if (beforeId is not null)
        {
            filter &= Filter.Lt("_id", ObjectId.Parse(beforeId));
        }

        return await orders.Find(filter)
            .SortByDescending(o => o.Id)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderDocument>> FindSalesAsync(string sellerId, string? beforeId, int limit, CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(o => o.SellerId, sellerId) & Filter.Eq(o => o.Status, OrderStatuses.Completed);
        if (beforeId is not null)
        {
            filter &= Filter.Lt("_id", ObjectId.Parse(beforeId));
        }

        return await orders.Find(filter)
            .SortByDescending(o => o.Id)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<(long Count, long TotalCents)> SalesSummaryAsync(string sellerId, CancellationToken cancellationToken)
    {
        // Un solo viaje: $match con ix_seller_id y $group con el numero y la suma.
        var groups = await orders.Aggregate()
            .Match(Filter.Eq(o => o.SellerId, sellerId) & Filter.Eq(o => o.Status, OrderStatuses.Completed))
            .Group(o => o.SellerId, g => new { Count = g.LongCount(), Total = g.Sum(o => o.Item.TotalCents) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return groups.Count == 0 ? (0, 0) : (groups[0].Count, groups[0].Total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> SoldProductIdsAsync(string sellerId, CancellationToken cancellationToken)
    {
        var ids = await orders.Find(
                Filter.Eq(o => o.SellerId, sellerId) & Filter.Eq(o => o.Status, OrderStatuses.Completed) & Filter.Ne(o => o.Item.ProductId, null))
            .Project(o => o.Item.ProductId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.OfType<string>().ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, long>> CountByBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        // Un solo viaje: $match con ix_buyer_id y $group por estado (como "Mis productos", HU-08).
        var groups = await orders.Aggregate()
            .Match(Filter.Eq(o => o.BuyerId, buyerId))
            .Group(o => o.Status, g => new { Status = g.Key, Count = g.LongCount() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return groups.ToDictionary(g => g.Status, g => g.Count, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<bool> HasOpenForProductAsync(string productId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(productId, out _)
            && await orders.Find(Filter.Eq(o => o.Item.ProductId, productId) & Filter.In(o => o.Status, OrderStatuses.Open))
                .Limit(1)
                .AnyAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<long> ReleaseAccountAsync(string userId, string replacementName, DateTime now, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return 0;
        }

        var cancelled = await orders.UpdateManyAsync(
            (Filter.Eq(o => o.BuyerId, userId) | Filter.Eq(o => o.SellerId, userId)) & Filter.In(o => o.Status, OrderStatuses.Open),
            Builders<OrderDocument>.Update
                .Set(o => o.Status, OrderStatuses.Cancelled)
                .Set(o => o.Cancellation, new OrderCancellation { By = OrderRoles.System, Reason = CancelReasons.AccountDeleted, At = now })
                .Set(o => o.UpdatedAt, now)
                .Push(o => o.History, new OrderTransition { Status = OrderStatuses.Cancelled, At = now, Reason = CancelReasons.AccountDeleted }),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await orders.UpdateManyAsync(
            o => o.BuyerId == userId,
            Builders<OrderDocument>.Update.Set(o => o.BuyerName, replacementName),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await orders.UpdateManyAsync(
            o => o.SellerId == userId,
            Builders<OrderDocument>.Update.Set(o => o.SellerName, replacementName),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return cancelled.ModifiedCount;
    }

    private static FindOneAndUpdateOptions<OrderDocument> After => new() { ReturnDocument = ReturnDocument.After };

    // La parte va en el filtro (OWASP API1): con el id de otra persona no cambia nada.
    private static FilterDefinition<OrderDocument> Party(string userId, string role) =>
        string.Equals(role, OrderRoles.Seller, StringComparison.Ordinal)
            ? Filter.Eq(o => o.SellerId, userId)
            : Filter.Eq(o => o.BuyerId, userId);
}

/// <summary>Puntos de entrega en MongoDB (<c>ux_community_slug</c>).</summary>
internal sealed class MongoMeetingPointRepository(IMongoDatabase database) : IMeetingPointRepository
{
    private readonly IMongoCollection<MeetingPointDocument> points = database.GetCollection<MeetingPointDocument>(MongoSchema.MeetingPoints);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MeetingPointDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(communityId, out _)
            ? await points.Find(p => p.CommunityId == communityId && p.Active)
                .SortBy(p => p.Order)
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];
}

/// <summary>Pedidos en memoria. Los documentos son inmutables: cada cambio reemplaza la entrada bajo el candado.</summary>
internal sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly Lock gate = new();
    private readonly List<OrderDocument> orders = [];
    private readonly Dictionary<string, int> counters = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<int> NextNumberAsync(string communityId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var seq = counters.GetValueOrDefault(OrderNumbers.CounterId(communityId)) + 1;
            counters[OrderNumbers.CounterId(communityId)] = seq;
            return Task.FromResult(OrderNumbers.First - 1 + seq);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryInsertAsync(OrderDocument order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);
        lock (gate)
        {
            if (orders.Exists(o => string.Equals(o.ConversationId, order.ConversationId, StringComparison.Ordinal) && IsOpen(o)))
            {
                return Task.FromResult(false);
            }

            orders.Add(order);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<OrderDocument?> FindByIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(orders.Find(o => string.Equals(o.Id, id, StringComparison.Ordinal)));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OrderDocument>> FindByConversationAsync(string conversationId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<OrderDocument> found = orders
                .Where(o => string.Equals(o.ConversationId, conversationId, StringComparison.Ordinal))
                .OrderByDescending(o => o.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<OrderDocument?> SetPaymentAsync(string id, string buyerId, string method, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var i = orders.FindIndex(o => string.Equals(o.Id, id, StringComparison.Ordinal)
                && string.Equals(o.BuyerId, buyerId, StringComparison.Ordinal)
                && o.Status == OrderStatuses.Pending);
            if (i < 0)
            {
                return Task.FromResult<OrderDocument?>(null);
            }

            orders[i] = Copy(orders[i], payment: new OrderPayment { Method = method, ChosenAt = now }, updatedAt: now);
            return Task.FromResult<OrderDocument?>(orders[i]);
        }
    }

    /// <inheritdoc />
    public Task<OrderDocument?> AcceptAsync(string id, string sellerId, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            o => Is(o, id) && string.Equals(o.SellerId, sellerId, StringComparison.Ordinal) && o.Status == OrderStatuses.Pending && o.Payment is not null,
            o => Copy(o, status: OrderStatuses.Confirmed, transition: new OrderTransition { Status = OrderStatuses.Confirmed, At = now }, updatedAt: now)));

    /// <inheritdoc />
    public Task<OrderDocument?> CancelAsync(string id, string userId, string role, string reason, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            o => Is(o, id) && IsParty(o, userId, role) && IsOpen(o),
            o => Copy(
                o,
                status: OrderStatuses.Cancelled,
                transition: new OrderTransition { Status = OrderStatuses.Cancelled, At = now, Reason = reason },
                updatedAt: now,
                cancellation: new OrderCancellation { By = role, Reason = reason, At = now })));

    /// <inheritdoc />
    public Task<OrderDocument?> ConfirmHandoverAsync(string id, string userId, string role, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            o => Is(o, id) && IsParty(o, userId, role) && o.Status == OrderStatuses.Confirmed,
            o =>
            {
                var seller = string.Equals(role, OrderRoles.Seller, StringComparison.Ordinal);
                var handover = new OrderHandover
                {
                    BuyerAt = o.Handover?.BuyerAt ?? (seller ? null : now),
                    SellerAt = o.Handover?.SellerAt ?? (seller ? now : null),
                };
                return Copy(o, updatedAt: now, handover: handover);
            }));

    /// <inheritdoc />
    public Task<OrderDocument?> CompleteAsync(string id, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            o => Is(o, id) && o.Status == OrderStatuses.Confirmed && o.Handover is { BuyerAt: not null, SellerAt: not null },
            o => Copy(o, status: OrderStatuses.Completed, transition: new OrderTransition { Status = OrderStatuses.Completed, At = now }, updatedAt: now)));

    /// <inheritdoc />
    public Task<IReadOnlyList<OrderDocument>> FindConfirmedByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<OrderDocument> found = orders
                .Where(o => (string.Equals(o.BuyerId, userId, StringComparison.Ordinal) || string.Equals(o.SellerId, userId, StringComparison.Ordinal))
                    && o.Status == OrderStatuses.Confirmed)
                .ToList();
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<bool> MarkRatedAsync(string id, string userId, string role, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            o => Is(o, id) && IsParty(o, userId, role) && o.Status == OrderStatuses.Completed && !OrderRatingWindow.Rated(o, role),
            o => Copy(o, ratings: string.Equals(role, OrderRoles.Seller, StringComparison.Ordinal)
                ? new OrderRatings { BuyerAt = o.Ratings?.BuyerAt, SellerAt = now }
                : new OrderRatings { BuyerAt = now, SellerAt = o.Ratings?.SellerAt })) is not null);

    /// <inheritdoc />
    public Task<IReadOnlyList<OrderDocument>> FindByBuyerAsync(
        string buyerId,
        IReadOnlyList<string>? statuses,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<OrderDocument> found = orders
                .Where(o => string.Equals(o.BuyerId, buyerId, StringComparison.Ordinal)
                    && (statuses is null || statuses.Contains(o.Status, StringComparer.Ordinal))
                    && (beforeId is null || string.CompareOrdinal(o.Id, beforeId) < 0))
                .OrderByDescending(o => o.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OrderDocument>> FindSalesAsync(string sellerId, string? beforeId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<OrderDocument> found = orders
                .Where(o => string.Equals(o.SellerId, sellerId, StringComparison.Ordinal)
                    && o.Status == OrderStatuses.Completed
                    && (beforeId is null || string.CompareOrdinal(o.Id, beforeId) < 0))
                .OrderByDescending(o => o.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<(long Count, long TotalCents)> SalesSummaryAsync(string sellerId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var sales = orders
                .Where(o => string.Equals(o.SellerId, sellerId, StringComparison.Ordinal) && o.Status == OrderStatuses.Completed)
                .ToList();
            return Task.FromResult(((long)sales.Count, sales.Sum(o => o.Item.TotalCents)));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<string>> SoldProductIdsAsync(string sellerId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyCollection<string> ids = orders
                .Where(o => string.Equals(o.SellerId, sellerId, StringComparison.Ordinal) && o.Status == OrderStatuses.Completed)
                .Select(o => o.Item.ProductId)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            return Task.FromResult(ids);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, long>> CountByBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyDictionary<string, long> counts = orders
                .Where(o => string.Equals(o.BuyerId, buyerId, StringComparison.Ordinal))
                .GroupBy(o => o.Status, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.LongCount(), StringComparer.Ordinal);
            return Task.FromResult(counts);
        }
    }

    /// <inheritdoc />
    public Task<bool> HasOpenForProductAsync(string productId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(orders.Exists(o => string.Equals(o.Item.ProductId, productId, StringComparison.Ordinal) && IsOpen(o)));
        }
    }

    /// <inheritdoc />
    public Task<long> ReleaseAccountAsync(string userId, string replacementName, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            long cancelled = 0;
            for (var i = 0; i < orders.Count; i++)
            {
                var o = orders[i];
                var buyer = string.Equals(o.BuyerId, userId, StringComparison.Ordinal);
                var seller = string.Equals(o.SellerId, userId, StringComparison.Ordinal);
                if (!buyer && !seller)
                {
                    continue;
                }

                var cancel = IsOpen(o);
                cancelled += cancel ? 1 : 0;
                orders[i] = Copy(
                    o,
                    status: cancel ? OrderStatuses.Cancelled : null,
                    transition: cancel ? new OrderTransition { Status = OrderStatuses.Cancelled, At = now, Reason = CancelReasons.AccountDeleted } : null,
                    cancellation: cancel ? new OrderCancellation { By = OrderRoles.System, Reason = CancelReasons.AccountDeleted, At = now } : null,
                    updatedAt: cancel ? now : null,
                    buyerName: buyer ? replacementName : null,
                    sellerName: seller ? replacementName : null);
            }

            return Task.FromResult(cancelled);
        }
    }

    private static bool IsOpen(OrderDocument order) => OrderStatuses.Open.Contains(order.Status);

    private static bool Is(OrderDocument order, string id) => string.Equals(order.Id, id, StringComparison.Ordinal);

    private static bool IsParty(OrderDocument order, string userId, string role) =>
        string.Equals(role, OrderRoles.Seller, StringComparison.Ordinal)
            ? string.Equals(order.SellerId, userId, StringComparison.Ordinal)
            : string.Equals(order.BuyerId, userId, StringComparison.Ordinal);

    // Un cambio con su condicion bajo el candado (como el filtro de un FindOneAndUpdate).
    private OrderDocument? Change(Func<OrderDocument, bool> when, Func<OrderDocument, OrderDocument> change)
    {
        lock (gate)
        {
            var i = orders.FindIndex(o => when(o));
            if (i < 0)
            {
                return null;
            }

            orders[i] = change(orders[i]);
            return orders[i];
        }
    }

    private static OrderDocument Copy(
        OrderDocument o,
        OrderPayment? payment = null,
        string? status = null,
        OrderTransition? transition = null,
        DateTime? updatedAt = null,
        string? buyerName = null,
        string? sellerName = null,
        OrderHandover? handover = null,
        OrderCancellation? cancellation = null,
        OrderRatings? ratings = null) => new()
        {
            Id = o.Id,
            CommunityId = o.CommunityId,
            Number = o.Number,
            BuyerId = o.BuyerId,
            SellerId = o.SellerId,
            BuyerName = buyerName ?? o.BuyerName,
            SellerName = sellerName ?? o.SellerName,
            ConversationId = o.ConversationId,
            Item = o.Item,
            Delivery = o.Delivery,
            Payment = payment ?? o.Payment,
            Handover = handover ?? o.Handover,
            Ratings = ratings ?? o.Ratings,
            Cancellation = cancellation ?? o.Cancellation,
            Status = status ?? o.Status,
            History = transition is null ? o.History : [.. o.History, transition],
            CreatedAt = o.CreatedAt,
            UpdatedAt = updatedAt ?? o.UpdatedAt,
            SchemaVersion = o.SchemaVersion,
        };
}

/// <summary>Puntos de entrega en memoria, con la misma semilla que db/014-pedidos.js.</summary>
internal sealed class InMemoryMeetingPointRepository : IMeetingPointRepository
{
    /// <summary>Los edificios de la UTTT, en su orden, con las carreras de cada uno.</summary>
    public static readonly IReadOnlyList<(string Slug, string Name, string[] Careers)> Seed =
    [
        ("biblioteca", "Biblioteca", []),
        ("cafeteria", "Cafetería", []),
        ("contaduria", "Contaduría", ["tsu-contaduria", "tsu-administracion-capital-humano", "tsu-administracion-proyectos", "lic-contaduria", "lic-capital-humano", "lic-negocios-proyectos"]),
        ("desarrollo-negocios", "Desarrollo de Negocios", ["tsu-negocios-ventas", "tsu-negocios-mercadotecnia", "tsu-logistica-transporte", "tsu-logistica-suministros", "lic-mercadotecnia", "lic-redes-logisticas"]),
        ("idiomas", "Idiomas", []),
        ("mantenimiento", "Mantenimiento", ["tsu-mantenimiento-industrial", "tsu-mantenimiento-maquinaria", "tsu-mantenimiento-soldadura", "ing-mantenimiento-industrial"]),
        ("mecatronica", "Mecatrónica", ["tsu-mecatronica-instalaciones", "tsu-mecatronica-robotica", "tsu-mecatronica-automatizacion", "ing-mecatronica"]),
        ("multimedios", "Multimedios", []),
        ("procesos-industriales", "Procesos Industriales", ["tsu-procesos-manufactura", "tsu-plantas-hidrocarburos", "ing-procesos-industriales", "ing-plantas-industriales"]),
        ("quimica", "Química", ["tsu-energias-renovables-solar", "tsu-quimica-industrial", "tsu-quimica-ambiental", "tsu-nanotecnologia-materiales", "ing-energias-renovables", "ing-quimica", "ing-nanotecnologia", "ing-ambiental"]),
        ("rectoria", "Rectoría", []),
        ("tecnologias-informacion", "Tecnologías de la Información", ["tsu-ti-software", "tsu-ti-entornos-virtuales", "tsu-ti-redes", "ing-software", "ing-entornos-virtuales", "ing-redes-ciberseguridad"]),
        ("villas-universitarias", "Villas Universitarias", []),
        ("vinculacion", "Vinculación", []),
    ];

    private readonly MeetingPointDocument[] points = Seed
        .Select((p, i) => new MeetingPointDocument
        {
            Id = (i + 1).ToString("x24", System.Globalization.CultureInfo.InvariantCulture),
            CommunityId = InMemoryCommunityRepository.UtttId,
            Slug = p.Slug,
            Name = p.Name,
            CareerSlugs = p.Careers,
            Order = i + 1,
        })
        .ToArray();

    /// <inheritdoc />
    public Task<IReadOnlyList<MeetingPointDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MeetingPointDocument>>(
            points.Where(p => p.Active && string.Equals(p.CommunityId, communityId, StringComparison.Ordinal)).OrderBy(p => p.Order).ToList());
}

/// <summary>El numero visible de los pedidos (<c>MS-NNNN</c>).</summary>
internal static class OrderNumbers
{
    /// <summary>El primer numero de cada campus.</summary>
    public const int First = 1001;

    /// <summary>El documento de <c>counters</c> de un campus.</summary>
    /// <param name="communityId">Campus.</param>
    /// <returns>El id.</returns>
    public static string CounterId(string communityId) => "orders:" + communityId;

    /// <summary>Como se muestra.</summary>
    /// <param name="number">El consecutivo.</param>
    /// <returns><c>MS-1001</c>.</returns>
    public static string Display(int number) => "MS-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
