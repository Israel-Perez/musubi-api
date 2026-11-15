using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Orders;

/// <summary>
/// Apartar un producto 24 h (HU-52; decisiones de la usuaria del 15-Nov-2026: uno activo a la vez, QR y camara, y al
/// entregar nace un pedido completado). Apartar guarda una pieza (como aceptar un pedido) y abre la conversacion de los
/// dos; quien vende verifica el QR o el codigo de 6 digitos (5 intentos) y "Confirmar entrega" crea el pedido
/// completado con los efectos de HU-24/26/35. Si nadie lo recoge, a las 24 h vence y la pieza regresa.
/// </summary>
internal sealed class ReservationService(
    IReservationRepository reservations,
    IOrderRepository orders,
    IMeetingPointRepository points,
    IProductDirectory products,
    IProductStock stock,
    IBlockDirectory blocks,
    IMemberDirectory members,
    IConversationOpener conversations,
    INotificationCenter center,
    OrderLifecycleService lifecycle,
    TimeProvider time)
{
    /// <summary>Lo que dura un apartado ("Te lo guarda 24 h").</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromHours(24);

    /// <summary>Digitos del codigo ("482 173").</summary>
    public const int CodeLength = 6;

    /// <summary>Intentos de quien vende (fuerza bruta: 5 de un millon).</summary>
    public const int MaxAttempts = 5;

    /// <summary>Apartados por pestana.</summary>
    public const int MaxList = 50;

    /// <summary>"Apartar": guarda una pieza 24 h y avisa a quien vende.</summary>
    /// <param name="userId">Quien aparta (del token).</param>
    /// <param name="body">Publicacion, punto y hora.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado (con su QR y su codigo) o el error.</returns>
    public async Task<Result<ReservationResponse>> CreateAsync(string userId, CreateReservationRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false) is not { Active: true } member)
        {
            return ApiErrors.Forbidden;
        }

        var product = string.IsNullOrEmpty(body.ProductId) ? null : await products.FindAsync(body.ProductId, cancellationToken).ConfigureAwait(false);
        // Otro campus se trata como no disponible (RNF-21): no se revela.
        if (product is null || !product.Visible || !string.Equals(product.CommunityId, member.CommunityId, StringComparison.Ordinal))
        {
            return ReservationsErrors.ProductUnavailable;
        }

        if (string.Equals(product.SellerId, userId, StringComparison.Ordinal))
        {
            return ReservationsErrors.OwnProduct;
        }

        if (!product.Available || product.Stock < 1)
        {
            return ReservationsErrors.ProductUnavailable;
        }

        if (await blocks.BetweenAsync(userId, product.SellerId, cancellationToken).ConfigureAwait(false))
        {
            return ReservationsErrors.Blocked;
        }

        var point = (await points.ListActiveAsync(member.CommunityId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.Slug, body.MeetingPoint, StringComparison.Ordinal));
        if (point is null)
        {
            return ReservationsErrors.InvalidMeetingPoint;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (body.PickupAt is not { } pickupAt || !IsValidPickup(pickupAt.ToUniversalTime(), now))
        {
            return ReservationsErrors.InvalidPickupTime;
        }

        if (!await stock.TryReserveAsync(product.Id, 1, cancellationToken).ConfigureAwait(false))
        {
            return ReservationsErrors.ProductUnavailable;
        }

        var seller = await members.FindMemberAsync(product.SellerId, cancellationToken).ConfigureAwait(false);
        var conversationId = await conversations.OpenForProductAsync(
            new ProductConversation(member.CommunityId, userId, product.SellerId, product.Id, product.Title, product.Photo),
            cancellationToken).ConfigureAwait(false);
        var reservation = new ReservationDocument
        {
            Id = MongoConventions.NewId(),
            CommunityId = member.CommunityId,
            Number = await reservations.NextNumberAsync(member.CommunityId, cancellationToken).ConfigureAwait(false),
            BuyerId = userId,
            SellerId = product.SellerId,
            BuyerName = member.FullName,
            SellerName = seller?.FullName ?? ChatService.UnknownMember,
            ConversationId = conversationId,
            ProductId = product.Id,
            Title = product.Title,
            Photo = product.Photo,
            PriceCents = product.PriceCents,
            PointSlug = point.Slug,
            PointName = point.Name,
            PickupAt = pickupAt.ToUniversalTime(),
            Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture),
            CreatedAt = now,
            ExpiresAt = now + Duration,
            UpdatedAt = now,
        };
        if (!await reservations.TryInsertAsync(reservation, cancellationToken).ConfigureAwait(false))
        {
            // Ya tenia uno activo (dos toques a la vez o desde otro celular): la pieza regresa.
            await stock.ReleaseAsync(product.Id, 1, cancellationToken).ConfigureAwait(false);
            return ReservationsErrors.LimitReached;
        }

        await NotifyAsync(
            reservation.SellerId,
            NotificationTypes.ReservationCreated,
            $"{ChatService.FirstName(reservation.BuyerName)} apartó tu {Short(reservation.Title)}",
            $"Pasa por él a {reservation.PointName}. Te mostrará un QR o un código; tienes la pieza guardada 24 h.",
            reservation,
            cancellationToken).ConfigureAwait(false);
        return Result<ReservationResponse>.Ok(ToResponse(reservation, userId));
    }

    /// <summary>"Mis apartados": "Yo aparté" o "Me apartaron", con los numeros de las dos.</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="tab"><c>buyer</c> (por defecto) o <c>seller</c>.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina.</returns>
    public async Task<ReservationPage> MineAsync(string userId, string? tab, CancellationToken cancellationToken)
    {
        var asSeller = string.Equals(tab, OrderRoles.Seller, StringComparison.Ordinal);
        var found = await reservations.FindByUserAsync(userId, asSeller, MaxList, cancellationToken).ConfigureAwait(false);
        var (bought, sold) = await reservations.CountByUserAsync(userId, cancellationToken).ConfigureAwait(false);
        return new ReservationPage([.. found.Select(r => ToResponse(r, userId))], new ReservationCounts(bought, sold));
    }

    /// <summary>Un apartado de quien consulta.</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="id">El apartado.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado o <c>reservations.not_found</c> (OWASP API1).</returns>
    public async Task<Result<ReservationResponse>> GetAsync(string userId, string id, CancellationToken cancellationToken)
    {
        var reservation = await reservations.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return reservation is not null && RoleOf(reservation, userId) is not null
            ? Result<ReservationResponse>.Ok(ToResponse(reservation, userId))
            : ReservationsErrors.NotFound;
    }

    /// <summary>"Cancelar apartado" (cualquiera de las partes): la pieza regresa y se avisa a la otra.</summary>
    /// <param name="userId">Quien cancela (del token).</param>
    /// <param name="id">El apartado.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado cancelado o el error.</returns>
    public async Task<Result<ReservationResponse>> CancelAsync(string userId, string id, CancellationToken cancellationToken)
    {
        var reservation = await reservations.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (reservation is null || RoleOf(reservation, userId) is not { } role)
        {
            return ReservationsErrors.NotFound;
        }

        var cancelled = await reservations.CancelAsync(id, userId, role, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (cancelled is null)
        {
            return ReservationsErrors.NotActive;
        }

        await stock.ReleaseAsync(cancelled.ProductId, 1, cancellationToken).ConfigureAwait(false);
        var who = ChatService.FirstName(role == OrderRoles.Seller ? cancelled.SellerName : cancelled.BuyerName);
        await NotifyAsync(
            role == OrderRoles.Seller ? cancelled.BuyerId : cancelled.SellerId,
            NotificationTypes.ReservationCancelled,
            "Apartado cancelado",
            $"{who} canceló el apartado #{ReservationNumbers.Display(cancelled.Number)} de {Short(cancelled.Title)}.",
            cancelled,
            cancellationToken).ConfigureAwait(false);
        return Result<ReservationResponse>.Ok(ToResponse(cancelled, userId));
    }

    /// <summary>
    /// "Verificar comprador": quien vende escanea el QR o escribe el codigo. Cada intento cuenta (hasta 5) antes de
    /// comparar, en tiempo constante.
    /// </summary>
    /// <param name="userId">Quien vende (del token).</param>
    /// <param name="id">El apartado.</param>
    /// <param name="body">El codigo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado verificado o el error.</returns>
    public async Task<Result<ReservationResponse>> VerifyAsync(string userId, string id, VerifyReservationRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var reservation = await reservations.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (SellerCheck(reservation, userId) is { } error)
        {
            return error;
        }

        var code = (body.Code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (code.Length != CodeLength || !code.All(char.IsAsciiDigit))
        {
            return ReservationsErrors.InvalidCodeFormat;
        }

        var attempt = await reservations.UseAttemptAsync(id, userId, MaxAttempts, cancellationToken).ConfigureAwait(false);
        if (attempt is null)
        {
            return reservation!.CodeAttempts >= MaxAttempts ? ReservationsErrors.CodeLocked : ReservationsErrors.NotActive;
        }

        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(code), Encoding.ASCII.GetBytes(attempt.Code)))
        {
            return ReservationsErrors.InvalidCode;
        }

        var verified = await reservations.MarkVerifiedAsync(id, userId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        return verified is null ? ReservationsErrors.NotActive : Result<ReservationResponse>.Ok(ToResponse(verified, userId));
    }

    /// <summary>
    /// "Confirmar entrega" (<c>Apartado-verificado</c>): el apartado pasa a recogido y nace un pedido completado en
    /// efectivo (decision de la usuaria del 15-Nov-2026): cuenta en ventas y compras, marca la publicacion vendida si
    /// se acabaron las piezas y los dos pueden calificarse (HU-27).
    /// </summary>
    /// <param name="userId">Quien vende (del token).</param>
    /// <param name="id">El apartado.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado recogido (con su <c>orderId</c>) o el error.</returns>
    public async Task<Result<ReservationResponse>> CompleteAsync(string userId, string id, CancellationToken cancellationToken)
    {
        var reservation = await reservations.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (SellerCheck(reservation, userId) is { } error)
        {
            return error;
        }

        if (reservation!.VerifiedAt is null)
        {
            return ReservationsErrors.NotVerified;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var orderId = MongoConventions.NewId();
        // Primero el apartado (atomico): dos toques a la vez no crean dos pedidos.
        var collected = await reservations.MarkCollectedAsync(id, userId, orderId, now, cancellationToken).ConfigureAwait(false);
        if (collected is null)
        {
            return ReservationsErrors.NotActive;
        }

        var order = new OrderDocument
        {
            Id = orderId,
            CommunityId = collected.CommunityId,
            Number = await orders.NextNumberAsync(collected.CommunityId, cancellationToken).ConfigureAwait(false),
            BuyerId = collected.BuyerId,
            SellerId = collected.SellerId,
            BuyerName = collected.BuyerName,
            SellerName = collected.SellerName,
            ConversationId = collected.ConversationId,
            Item = new OrderItem
            {
                Source = OrderSources.Product,
                SourceId = collected.ProductId,
                ProductId = collected.ProductId,
                Title = collected.Title,
                Photo = collected.Photo,
                PriceCents = collected.PriceCents,
                Quantity = 1,
                TotalCents = collected.PriceCents,
            },
            Delivery = new OrderDelivery { PointSlug = collected.PointSlug, PointName = collected.PointName, At = now },
            Payment = new OrderPayment { Method = PaymentMethods.Cash, ChosenAt = collected.CreatedAt },
            Handover = new OrderHandover { BuyerAt = now, SellerAt = now },
            Status = OrderStatuses.Completed,
            History =
            [
                new OrderTransition { Status = OrderStatuses.Pending, At = collected.CreatedAt },
                new OrderTransition { Status = OrderStatuses.Completed, At = now },
            ],
            CreatedAt = now,
            UpdatedAt = now,
        };
        await orders.TryInsertAsync(order, cancellationToken).ConfigureAwait(false);
        await lifecycle.AfterCompletedAsync(
            order,
            now,
            order.BuyerId,
            "¡Listo! Recogiste tu apartado",
            $"{ChatService.FirstName(order.SellerName)} confirmó la entrega de {Short(order.Item.Title)}. Toca para calificar.",
            cancellationToken).ConfigureAwait(false);
        return Result<ReservationResponse>.Ok(ToResponse(collected, userId));
    }

    /// <summary>Si una hora de recoger cae dentro de las 24 h, de 7:00 a 21:00 del campus.</summary>
    /// <param name="pickupAt">La hora (UTC).</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <returns><c>true</c> si es valida.</returns>
    public static bool IsValidPickup(DateTime pickupAt, DateTime now) =>
        pickupAt >= now - TimeSpan.FromMinutes(5) && pickupAt <= now + Duration && OrderService.IsCampusHour(pickupAt);

    /// <summary>La respuesta para una de las partes: el codigo y el QR solo a quien aparta, los intentos a quien vende.</summary>
    /// <param name="r">El apartado.</param>
    /// <param name="userId">Quien consulta.</param>
    /// <returns>La respuesta.</returns>
    internal static ReservationResponse ToResponse(ReservationDocument r, string userId)
    {
        var buyer = string.Equals(r.BuyerId, userId, StringComparison.Ordinal);
        var active = r.Status == ReservationStatuses.Active;
        return new ReservationResponse(
            r.Id,
            ReservationNumbers.Display(r.Number),
            r.Status,
            buyer ? OrderRoles.Buyer : OrderRoles.Seller,
            r.ProductId,
            r.Title,
            r.Photo,
            r.PriceCents,
            new OrderPartyResponse(r.BuyerId, r.BuyerName),
            new OrderPartyResponse(r.SellerId, r.SellerName),
            r.PointSlug,
            r.PointName,
            r.PickupAt,
            r.ExpiresAt,
            r.CreatedAt,
            buyer && active ? r.Code : null,
            buyer && active ? QrPayload(r) : null,
            !buyer && active ? Math.Max(0, MaxAttempts - r.CodeAttempts) : null,
            r.VerifiedAt,
            r.OrderId,
            r.ConversationId,
            r.CancelledBy);
    }

    /// <summary>Lo que lleva el QR: el apartado y su codigo (quien vende lo escanea desde ese apartado).</summary>
    /// <param name="r">El apartado.</param>
    /// <returns>El texto del QR.</returns>
    public static string QrPayload(ReservationDocument r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return $"musubi:apartado:{r.Id}:{r.Code}";
    }

    /// <summary>"la calculadora" en un aviso: el nombre hasta 40 caracteres.</summary>
    /// <param name="title">El nombre.</param>
    /// <returns>Corto.</returns>
    internal static string Short(string title) => title.Length <= 40 ? title : title[..39].TrimEnd() + "…";

    internal Task NotifyAsync(string userId, string type, string title, string body, ReservationDocument r, CancellationToken cancellationToken) =>
        center.NotifyAsync(
            new Notice(userId, type, title, body, NotificationTargets.Reservation, r.Id, NotificationCategories.Orders),
            cancellationToken);

    private static string? RoleOf(ReservationDocument r, string userId)
    {
        if (string.Equals(r.BuyerId, userId, StringComparison.Ordinal))
        {
            return OrderRoles.Buyer;
        }

        return string.Equals(r.SellerId, userId, StringComparison.Ordinal) ? OrderRoles.Seller : null;
    }

    private static ApiError? SellerCheck(ReservationDocument? reservation, string userId)
    {
        if (reservation is null || RoleOf(reservation, userId) is not { } role)
        {
            return ReservationsErrors.NotFound;
        }

        if (role != OrderRoles.Seller)
        {
            return ReservationsErrors.OnlySeller;
        }

        return reservation.Status == ReservationStatuses.Active ? null : ReservationsErrors.NotActive;
    }
}

/// <summary>A las 24 h sin recogerse, el apartado vence: la pieza regresa y se avisa a los dos.</summary>
internal sealed class ReservationExpiryJob(IReservationRepository reservations, IProductStock stock, ReservationService service)
{
    /// <summary>Apartados por vuelta.</summary>
    public const int BatchSize = 200;

    /// <summary>Vence los apartados que ya pasaron sus 24 h.</summary>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos vencio esta vuelta.</returns>
    public async Task<int> RunAsync(DateTime now, CancellationToken cancellationToken)
    {
        var expired = 0;
        foreach (var r in await reservations.FindExpiredAsync(now, BatchSize, cancellationToken).ConfigureAwait(false))
        {
            // Solo quien lo marca regresa la pieza (dos instancias no la regresan dos veces).
            if (!await reservations.MarkExpiredAsync(r.Id, now, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            expired++;
            await stock.ReleaseAsync(r.ProductId, 1, cancellationToken).ConfigureAwait(false);
            var title = ReservationService.Short(r.Title);
            await service.NotifyAsync(r.BuyerId, NotificationTypes.ReservationExpired, "Venció tu apartado", $"Pasaron 24 h sin recoger {title}. Ya está disponible otra vez.", r, cancellationToken).ConfigureAwait(false);
            await service.NotifyAsync(r.SellerId, NotificationTypes.ReservationExpired, $"Venció el apartado de {ChatService.FirstName(r.BuyerName)}", $"{title} vuelve a estar disponible.", r, cancellationToken).ConfigureAwait(false);
        }

        return expired;
    }
}

/// <summary>Corre <see cref="ReservationExpiryJob"/> cada <see cref="ReminderOptions.ReminderMinutes"/>.</summary>
internal sealed partial class ReservationExpiryWorker(
    ReservationExpiryJob job,
    IOptions<ReminderOptions> options,
    TimeProvider time,
    ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.ReminderMinutes <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.ReminderMinutes), time);
        do
        {
            try
            {
                await job.RunAsync(time.GetUtcNow().UtcDateTime, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException)
            {
                LogSkipped(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vencimiento de apartados omitido ({Error})")]
    private static partial void LogSkipped(ILogger logger, string error);
}

/// <summary>Borrado de una cuenta (HU-46): sus apartados activos se cancelan y las piezas regresan.</summary>
internal sealed class ReservationAccountEraser(IReservationRepository reservations, IProductStock stock, TimeProvider time) : IAccountDataEraser
{
    /// <inheritdoc />
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        foreach (var r in await reservations.FindActiveByUserAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            var role = string.Equals(r.BuyerId, userId, StringComparison.Ordinal) ? OrderRoles.Buyer : OrderRoles.Seller;
            if (await reservations.CancelAsync(r.Id, userId, role, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false) is not null)
            {
                await stock.ReleaseAsync(r.ProductId, 1, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
