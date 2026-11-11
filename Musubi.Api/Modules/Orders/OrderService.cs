using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.Orders;

/// <summary>
/// "Confirmar la compra" (HU-23, RF-30, CU-17): quien compra crea el pedido desde su conversacion (RN-21), con
/// una publicacion o una oferta aceptada (HU-17). Copia el producto (R8), registra cantidad, punto, fecha y hora,
/// y nace <see cref="OrderStatuses.Pending"/> (RN-16). Uno abierto por conversacion. El pago es en efectivo al
/// entregar (RN-18 del Entregable 1).
/// NOTE (HU-24): quien vende acepta o rechaza; las piezas se apartan al aceptar (RF-34).
/// HU-34 (RF-47): con <c>agreementId</c>, el pedido sale del acuerdo con IA que confirmaron los dos (RN-41), con su
/// precio acordado, y el acuerdo se borra.
/// HU-31: quien vende recibe el aviso del pedido nuevo.
/// </summary>
internal sealed class OrderService(
    IOrderRepository orders,
    IMeetingPointRepository points,
    IConversationDirectory conversations,
    IProductDirectory products,
    IOfferDirectory offers,
    IBlockDirectory blocks,
    IMemberDirectory members,
    IProfileDirectory profiles,
    IContentModerator moderator,
    IAgreementDirectory agreements,
    INotificationCenter center,
    TimeProvider time)
{
    /// <summary>Piezas por pedido.</summary>
    public const int MaxQuantity = 10;

    /// <summary>Caracteres del detalle del lugar.</summary>
    public const int MaxDetailLength = 120;

    /// <summary>Pedidos que se leen por conversacion.</summary>
    public const int MaxPerConversation = 20;

    /// <summary>El nombre que queda en los pedidos de una cuenta borrada (HU-46, R11).</summary>
    public const string DeletedName = "Usuario eliminado";

    /// <summary>Lo minimo para acordar la entrega.</summary>
    public static readonly TimeSpan MinLead = TimeSpan.FromMinutes(30);

    /// <summary>Lo maximo hacia adelante.</summary>
    public static readonly TimeSpan MaxLead = TimeSpan.FromDays(14);

    /// <summary>Primera hora de entrega del campus.</summary>
    public static readonly TimeOnly FirstHour = new(7, 0);

    /// <summary>Ultima hora de entrega del campus.</summary>
    public static readonly TimeOnly LastHour = new(21, 0);

    // El campus esta en UTC-6 (como las fechas de las solicitudes, HU-13).
    private static readonly TimeSpan CampusOffset = WantedService.CampusOffset;

    /// <summary>
    /// Los puntos de entrega del campus de quien consulta, en su orden, con el edificio de su carrera marcado
    /// (decision de la usuaria, 10-Nov-2026).
    /// </summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los puntos.</returns>
    public async Task<Result<MeetingPointsResponse>> MeetingPointsAsync(string userId, CancellationToken cancellationToken)
    {
        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var all = await points.ListActiveAsync(member.CommunityId, cancellationToken).ConfigureAwait(false);
        var career = await profiles.FindCareerSlugAsync(userId, cancellationToken).ConfigureAwait(false);
        return Result<MeetingPointsResponse>.Ok(new MeetingPointsResponse(all
            .Select(p => new MeetingPointResponse(p.Slug, p.Name, career is not null && p.CareerSlugs.Contains(career, StringComparer.Ordinal)))
            .ToList()));
    }

    /// <summary>"Crear pedido" (maqueta <c>Acuerdo-confirmado</c>).</summary>
    /// <param name="userId">Quien compra (del token).</param>
    /// <param name="body">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido o el error.</returns>
    public async Task<Result<OrderResponse>> CreateAsync(string userId, CreateOrderRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        // HU-34: el acuerdo confirmado manda; lo demas del cuerpo no se usa.
        AgreementForOrder? agreement = null;
        if (body.AgreementId is { } agreementId)
        {
            agreement = await agreements.FindAsync(agreementId, cancellationToken).ConfigureAwait(false);
            if (agreement is null || !IsParty(agreement.BuyerId, agreement.SellerId, userId))
            {
                return OrdersErrors.AgreementNotFound;
            }

            if (!agreement.Confirmed)
            {
                return OrdersErrors.AgreementNotConfirmed;
            }

            body = new CreateOrderRequest(agreement.ConversationId, agreement.Quantity, agreement.PointSlug, agreement.Detail, agreement.At, agreement.Id);
        }

        var conversation = body.ConversationId is { } conversationId && ObjectId.TryParse(conversationId, out _)
            ? await conversations.FindAsync(conversationId, cancellationToken).ConfigureAwait(false)
            : null;
        if (conversation is null || !IsParty(conversation.BuyerId, conversation.SellerId, userId))
        {
            return OrdersErrors.ConversationNotFound;
        }

        if (!string.Equals(conversation.BuyerId, userId, StringComparison.Ordinal))
        {
            return OrdersErrors.OnlyBuyer;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var (delivery, invalid) = await ValidateAsync(body, member.CommunityId, now, cancellationToken).ConfigureAwait(false);
        if (invalid is not null)
        {
            return invalid;
        }

        if (await blocks.BetweenAsync(userId, conversation.SellerId, cancellationToken).ConfigureAwait(false))
        {
            return OrdersErrors.Blocked;
        }

        var (item, unavailable) = await ItemAsync(conversation, body.Quantity!.Value, cancellationToken).ConfigureAwait(false);
        if (unavailable is not null)
        {
            return unavailable;
        }

        if (agreement is not null)
        {
            // El precio y el nombre que acordaron los dos (RN-41).
            var price = agreement.PriceCents ?? item!.PriceCents;
            item = Item(item!.Source, item.SourceId, item.ProductId, agreement.Title, item.Photo, price, item.Quantity);
        }

        if ((await orders.FindByConversationAsync(conversation.Id, MaxPerConversation, cancellationToken).ConfigureAwait(false))
            .Any(o => OrderStatuses.Open.Contains(o.Status)))
        {
            return OrdersErrors.AlreadyOpen;
        }

        // 02-07: se modera lo que escribio la persona.
        if (delivery!.Detail is { } detail)
        {
            var verdicts = await moderator.CheckTextsAsync([detail], cancellationToken).ConfigureAwait(false);
            if (ModerationErrors.For(verdicts[0], ModerationErrors.TextRejected("el detalle del lugar")) is { } rejected)
            {
                return rejected;
            }
        }

        var names = await members.FindMembersAsync([conversation.BuyerId, conversation.SellerId], cancellationToken).ConfigureAwait(false);
        var order = new OrderDocument
        {
            Id = ObjectId.GenerateNewId().ToString(),
            CommunityId = member.CommunityId,
            Number = await orders.NextNumberAsync(member.CommunityId, cancellationToken).ConfigureAwait(false),
            BuyerId = conversation.BuyerId,
            SellerId = conversation.SellerId,
            BuyerName = names.GetValueOrDefault(conversation.BuyerId)?.FullName ?? conversation.BuyerName ?? member.FullName,
            SellerName = names.GetValueOrDefault(conversation.SellerId)?.FullName ?? conversation.SellerName ?? DeletedName,
            ConversationId = conversation.Id,
            Item = item!,
            Delivery = delivery,
            Status = OrderStatuses.Pending,
            History = [new OrderTransition { Status = OrderStatuses.Pending, At = now }],
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (!await orders.TryInsertAsync(order, cancellationToken).ConfigureAwait(false))
        {
            return OrdersErrors.AlreadyOpen;
        }

        if (agreement is not null)
        {
            await agreements.ConsumeAsync(agreement, cancellationToken).ConfigureAwait(false);
        }

        await center.NotifyAsync(
            new Notice(
                order.SellerId,
                NotificationTypes.OrderCreated,
                "Tienes un pedido nuevo",
                $"{ChatService.FirstName(order.BuyerName)} pidió {order.Item.Quantity} × {order.Item.Title}. Pedido #{OrderNumbers.Display(order.Number)}.",
                NotificationTargets.Order,
                order.Id,
                NotificationCategories.Orders),
            cancellationToken).ConfigureAwait(false);

        return Result<OrderResponse>.Ok(ToResponse(order, userId));
    }

    /// <summary>Un pedido, solo para sus partes (RNF-03, OWASP API1).</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="orderId">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido o <c>orders.not_found</c>.</returns>
    public async Task<Result<OrderResponse>> GetAsync(string userId, string orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        return order is not null && IsParty(order.BuyerId, order.SellerId, userId)
            ? Result<OrderResponse>.Ok(ToResponse(order, userId))
            : OrdersErrors.NotFound;
    }

    /// <summary>Los pedidos de una conversacion propia (el aviso del chat).</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="conversationId">La conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los pedidos o el error.</returns>
    public async Task<Result<OrderListResponse>> ByConversationAsync(string userId, string? conversationId, CancellationToken cancellationToken)
    {
        if (conversationId is null || !ObjectId.TryParse(conversationId, out _))
        {
            return OrdersErrors.InvalidConversation;
        }

        var conversation = await conversations.FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null || !IsParty(conversation.BuyerId, conversation.SellerId, userId))
        {
            return OrdersErrors.ConversationNotFound;
        }

        var found = await orders.FindByConversationAsync(conversationId, MaxPerConversation, cancellationToken).ConfigureAwait(false);
        return Result<OrderListResponse>.Ok(new OrderListResponse(found.Select(o => ToResponse(o, userId)).ToList()));
    }

    /// <summary>"¿Como quieres pagar?" (maqueta <c>Pedido-pago</c>): solo quien compra y solo un pedido pendiente.</summary>
    /// <param name="userId">Quien compra (del token).</param>
    /// <param name="orderId">El pedido.</param>
    /// <param name="body">La forma de pago.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido o el error.</returns>
    public async Task<Result<OrderResponse>> SetPaymentAsync(string userId, string orderId, SetPaymentRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false) is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var order = await orders.FindByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || !IsParty(order.BuyerId, order.SellerId, userId))
        {
            return OrdersErrors.NotFound;
        }

        if (!string.Equals(order.BuyerId, userId, StringComparison.Ordinal))
        {
            return OrdersErrors.OnlyBuyer;
        }

        switch (body.Method)
        {
            case PaymentMethods.Cash:
                break;
            case PaymentMethods.Transfer:
                // NOTE (HU-55): transferencia a la cuenta CLABE verificada de quien vende.
                return OrdersErrors.PaymentUnavailable;
            default:
                return OrdersErrors.InvalidPayment;
        }

        var changed = await orders.SetPaymentAsync(order.Id, userId, PaymentMethods.Cash, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        return changed is null ? OrdersErrors.NotPending : Result<OrderResponse>.Ok(ToResponse(changed, userId));
    }

    /// <summary>La respuesta de un pedido para una de sus partes.</summary>
    /// <param name="order">El pedido.</param>
    /// <param name="userId">Quien consulta.</param>
    /// <returns>La respuesta.</returns>
    internal static OrderResponse ToResponse(OrderDocument order, string userId) => new(
        order.Id,
        OrderNumbers.Display(order.Number),
        order.Status,
        string.Equals(order.BuyerId, userId, StringComparison.Ordinal) ? "buyer" : "seller",
        order.ConversationId,
        new OrderItemResponse(
            order.Item.Source,
            order.Item.SourceId,
            order.Item.ProductId,
            order.Item.Title,
            order.Item.Photo,
            order.Item.PriceCents,
            order.Item.Quantity,
            order.Item.TotalCents),
        new OrderPartyResponse(order.BuyerId, order.BuyerName),
        new OrderPartyResponse(order.SellerId, order.SellerName),
        new OrderDeliveryResponse(order.Delivery.PointSlug, order.Delivery.PointName, order.Delivery.Detail, order.Delivery.At),
        order.Payment is null ? null : new OrderPaymentResponse(order.Payment.Method, order.Payment.ChosenAt),
        TransferAvailable: false,
        order.CreatedAt);

    private static bool IsParty(string buyerId, string sellerId, string userId) =>
        string.Equals(buyerId, userId, StringComparison.Ordinal) || string.Equals(sellerId, userId, StringComparison.Ordinal);

    /// <summary>
    /// Si una entrega cae en la ventana del pedido: de 30 minutos a 14 dias de ahora, entre las 7:00 y las 21:00 del
    /// campus. La usa tambien el acuerdo con IA (HU-33/34) para no proponer una fecha que el pedido rechazaria.
    /// </summary>
    /// <param name="at">Fecha y hora (UTC).</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <returns><c>true</c> si es valida.</returns>
    internal static bool IsValidDeliveryTime(DateTime at, DateTime now) =>
        at >= now + MinLead && at <= now + MaxLead && IsCampusHour(at);

    private static bool IsCampusHour(DateTime utc)
    {
        var local = TimeOnly.FromDateTime(utc + CampusOffset);
        return local >= FirstHour && local <= LastHour;
    }

    // Cantidad, punto, detalle y fecha (en ese orden, como los ve la persona en el resumen).
    private async Task<(OrderDelivery? Delivery, ApiError? Error)> ValidateAsync(
        CreateOrderRequest body,
        string communityId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (body.Quantity is not (>= 1 and <= MaxQuantity))
        {
            return (null, OrdersErrors.InvalidQuantity);
        }

        var point = body.MeetingPoint is null
            ? null
            : (await points.ListActiveAsync(communityId, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(p => string.Equals(p.Slug, body.MeetingPoint, StringComparison.Ordinal));
        if (point is null)
        {
            return (null, OrdersErrors.InvalidMeetingPoint);
        }

        var detail = string.IsNullOrWhiteSpace(body.MeetingDetail) ? null : body.MeetingDetail.Trim();
        if (detail is { Length: > MaxDetailLength })
        {
            return (null, OrdersErrors.InvalidDetail);
        }

        if (body.DeliveryAt is not { } requested)
        {
            return (null, OrdersErrors.InvalidDeliveryTime);
        }

        var at = requested.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(requested, DateTimeKind.Utc) : requested.ToUniversalTime();
        if (!IsValidDeliveryTime(at, now))
        {
            return (null, OrdersErrors.InvalidDeliveryTime);
        }

        return (new OrderDelivery { PointSlug = point.Slug, PointName = point.Name, Detail = detail, At = at }, null);
    }

    // Lo que se compra segun la conversacion: la publicacion (RN-17 contra sus piezas) o la oferta aceptada.
    private async Task<(OrderItem? Item, ApiError? Error)> ItemAsync(ConversationSummary conversation, int quantity, CancellationToken cancellationToken)
    {
        if (conversation.ContextType == ConversationContextTypes.Offer)
        {
            var offer = await offers.FindAsync(conversation.ContextId, cancellationToken).ConfigureAwait(false);
            // La oferta tiene que ser la de esta conversacion: quien busca compra y quien ofrecio vende.
            if (offer is null
                || !string.Equals(offer.CommunityId, conversation.CommunityId, StringComparison.Ordinal)
                || !string.Equals(offer.RequesterId, conversation.BuyerId, StringComparison.Ordinal)
                || !string.Equals(offer.SellerId, conversation.SellerId, StringComparison.Ordinal))
            {
                return (null, OrdersErrors.ProductUnavailable);
            }

            if (!offer.Accepted)
            {
                return (null, OrdersErrors.OfferNotAccepted);
            }

            string? photo = offer.Photo;
            if (offer.ProductId is { } linkedId)
            {
                // Oferta vinculada: la publicacion tiene que seguir disponible y con piezas.
                var linked = await products.FindAsync(linkedId, cancellationToken).ConfigureAwait(false);
                if (linked is null || !linked.Available)
                {
                    return (null, OrdersErrors.ProductUnavailable);
                }

                if (quantity > linked.Stock)
                {
                    return (null, OrdersErrors.NotEnoughStock);
                }

                photo ??= linked.Photo;
            }
            else if (quantity > offer.RequestQuantity)
            {
                return (null, OrdersErrors.NotEnoughStock);
            }

            return (Item(OrderSources.Offer, offer.Id, offer.ProductId, offer.Title, photo, offer.PriceCents, quantity), null);
        }

        var product = await products.FindAsync(conversation.ContextId, cancellationToken).ConfigureAwait(false);
        if (product is null || !product.Available || !string.Equals(product.CommunityId, conversation.CommunityId, StringComparison.Ordinal))
        {
            return (null, OrdersErrors.ProductUnavailable);
        }

        if (string.Equals(product.SellerId, conversation.BuyerId, StringComparison.Ordinal))
        {
            return (null, OrdersErrors.OwnProduct);
        }

        // Quien vende en la conversacion es el dueno de la publicacion (el chat se abrio con el).
        if (!string.Equals(product.SellerId, conversation.SellerId, StringComparison.Ordinal))
        {
            return (null, OrdersErrors.ProductUnavailable);
        }

        return quantity > product.Stock
            ? (null, OrdersErrors.NotEnoughStock)
            : (Item(OrderSources.Product, product.Id, product.Id, product.Title, product.Photo, product.PriceCents, quantity), null);
    }

    private static OrderItem Item(string source, string sourceId, string? productId, string title, string? photo, long priceCents, int quantity) => new()
    {
        Source = source,
        SourceId = sourceId,
        ProductId = productId,
        Title = title.Length > 80 ? title[..80] : title,
        Photo = photo,
        PriceCents = priceCents,
        Quantity = quantity,
        TotalCents = priceCents * quantity,
    };
}
