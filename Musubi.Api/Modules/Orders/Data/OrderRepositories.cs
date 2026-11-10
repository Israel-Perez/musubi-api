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
                    updatedAt: cancel ? now : null,
                    buyerName: buyer ? replacementName : null,
                    sellerName: seller ? replacementName : null);
            }

            return Task.FromResult(cancelled);
        }
    }

    private static bool IsOpen(OrderDocument order) => OrderStatuses.Open.Contains(order.Status);

    private static OrderDocument Copy(
        OrderDocument o,
        OrderPayment? payment = null,
        string? status = null,
        OrderTransition? transition = null,
        DateTime? updatedAt = null,
        string? buyerName = null,
        string? sellerName = null) => new()
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
