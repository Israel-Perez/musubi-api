using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Admin;

/// <summary>Los periodos de los indicadores.</summary>
internal static class IndicatorPeriods
{
    /// <summary>Los ultimos 7 dias.</summary>
    public const string Week = "week";

    /// <summary>Los ultimos 30 dias.</summary>
    public const string Month = "month";

    /// <summary>Los dias de un periodo, o <c>null</c> si no existe.</summary>
    /// <param name="period">El periodo.</param>
    /// <returns>Los dias.</returns>
    public static int? Days(string? period) => (period ?? Week) switch
    {
        Week => 7,
        Month => 30,
        _ => null,
    };
}

/// <summary>Las unidades de un indicador (la app decide como mostrarlo).</summary>
internal static class IndicatorUnits
{
    /// <summary>Un conteo.</summary>
    public const string Count = "count";

    /// <summary>De 0 a 100.</summary>
    public const string Percent = "percent";

    /// <summary>Un promedio con decimales.</summary>
    public const string Average = "average";

    /// <summary>Horas.</summary>
    public const string Hours = "hours";

    /// <summary>De 1 a 5 estrellas.</summary>
    public const string Stars = "stars";
}

/// <summary>
/// HU-42 (RF-57, CU-27): los indicadores del apartado 19 de la propuesta para el administrador de un campus. Cada modulo
/// calcula lo suyo detras de su contrato (Modules/README: un modulo no lee las colecciones de otro); aqui solo se juntan
/// y se cruzan. Son conteos agregados: nunca salen nombres ni ids de personas (RNF-06). Los tratos que se cierran fuera
/// de la app no se cuentan: las ventas son un minimo.
/// </summary>
internal sealed class UsageIndicatorsService(
    AdminGate gate,
    IUserIndicators users,
    IListingIndicators listings,
    IWantedIndicators wanted,
    IMatchIndicators matches,
    IOrderIndicators orders,
    IChatIndicators chat,
    IAiIndicators ai,
    INotificationIndicators notices,
    ITrustIndicators trust,
    TimeProvider time)
{
    /// <summary>Los indicadores del campus del administrador en el periodo.</summary>
    /// <param name="adminId">Usuario (del token).</param>
    /// <param name="period"><c>week</c> (por defecto) o <c>month</c>.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los indicadores, o el error.</returns>
    public async Task<Result<UsageIndicatorsResponse>> IndicatorsAsync(string adminId, string? period, CancellationToken cancellationToken)
    {
        if (await gate.ActiveAdminAsync(adminId, cancellationToken).ConfigureAwait(false) is not { } admin)
        {
            return ApiErrors.Forbidden;
        }

        if (IndicatorPeriods.Days(period) is not { } days)
        {
            return AdminErrors.InvalidPeriod;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var from = now.AddDays(-days);
        // Los modulos cuentan [from, to): un tick despues de ahora incluye lo de este mismo instante.
        var to = now.AddTicks(1);
        var campus = admin.CommunityId;

        var adoption = await users.AdoptionAsync(campus, from, to, cancellationToken).ConfigureAwait(false);
        var supply = await listings.SupplyAsync(campus, from, to, cancellationToken).ConfigureAwait(false);
        var demand = await wanted.DemandAsync(campus, from, to, cancellationToken).ConfigureAwait(false);
        var connection = await matches.ConnectionAsync(campus, from, to, cancellationToken).ConfigureAwait(false);
        var operations = await orders.OperationsAsync(campus, from, to, cancellationToken).ConfigureAwait(false);
        var conversations = await chat.StartedAsync(campus, from, to, cancellationToken).ConfigureAwait(false);
        var withOrder = await orders.ConversationsWithOrdersAsync(conversations, cancellationToken).ConfigureAwait(false);
        var aiUsage = await ai.UsageAsync(campus, from, to, cancellationToken).ConfigureAwait(false);
        var savedSearches = await AttendedSearchesAsync(campus, from, to, cancellationToken).ConfigureAwait(false);
        var (slow, soldAfter) = await SlowListingsAsync(campus, from, to, cancellationToken).ConfigureAwait(false);
        var confidence = await trust.TrustAsync(campus, from, to, cancellationToken).ConfigureAwait(false);

        string Name(string slug) => slug == WantedService.OtherCategory ? "Otro" : supply.CategoryNames.GetValueOrDefault(slug, slug);
        IReadOnlyList<IndicatorBreakdown> Breakdown(IReadOnlyDictionary<string, long> bySlug) =>
            [.. bySlug.OrderByDescending(p => p.Value).ThenBy(p => Name(p.Key), StringComparer.Ordinal).Select(p => new IndicatorBreakdown(Name(p.Key), p.Value))];

        IReadOnlyList<IndicatorArea> areas =
        [
            new("adoption", "Adopción",
            [
                Count("users_registered", "Usuarios registrados", adoption.Registered, $"{adoption.Validated} validados · {adoption.Joined} nuevos en el periodo"),
                Count("users_active", "Usuarios activos", adoption.Active, "Usaron la app en el periodo"),
            ]),
            new("supply", "Oferta",
            [
                Count("products_created", "Publicaciones creadas", supply.Created, "En el periodo", Breakdown(supply.CreatedByCategory)),
                Count("products_active", "Publicaciones activas", supply.Active, "A la venta ahora"),
            ]),
            new("demand", "Demanda",
            [
                Count("requests_created", "Solicitudes de productos", demand.Requests, "Publicadas en el periodo", Breakdown(demand.ByCategory)),
                new("offers_per_request", "Ofertas por solicitud", Ratio(demand.OffersOnThem, demand.Requests), IndicatorUnits.Average, $"{demand.OffersOnThem} ofertas en {demand.Requests} solicitudes", null),
                new("direct_offers", "Ofertas directas", Percent(demand.DirectOffers, demand.OffersMade), IndicatorUnits.Percent, $"{demand.DirectOffers} de {demand.OffersMade} ofertas, con productos que no estaban publicados", null),
            ]),
            new("connection", "Conexión",
            [
                Count("matches", "Coincidencias", connection.Found, $"{connection.Ordered} terminaron en pedido"),
                new("request_conversion", "Conversión de solicitudes", Percent(operations.FromOffers, demand.Requests), IndicatorUnits.Percent, $"{operations.FromOffers} compras completadas desde una oferta ÷ {demand.Requests} solicitudes", null),
                Count("saved_searches_served", "Búsquedas guardadas atendidas", savedSearches, "Recibieron el aviso de que apareció"),
            ]),
            new("operations", "Operaciones",
            [
                Count("orders_completed", "Compras completadas", operations.Completed, $"{operations.Units} productos vendidos"),
                new("time_to_offer", "Tiempo para encontrar un producto", Round(demand.HoursToFirstOffer), IndicatorUnits.Hours, demand.HoursToAccepted is { } accepted
                    ? $"Hasta la primera oferta; hasta aceptar una, {Hours(accepted)}"
                    : "Hasta la primera oferta", null),
            ]),
            new("chat", "Chat",
            [
                Count("conversations", "Conversaciones iniciadas", conversations.Count, "En el periodo"),
                new("chat_conversion", "Conversaciones con pedido", Percent(withOrder, conversations.Count), IndicatorUnits.Percent, $"{withOrder} de {conversations.Count}", null),
            ]),
            new("ai", "IA",
            [
                Count("ai_summaries", "Resúmenes con IA", aiUsage.Summaries, $"{aiUsage.Confirmed} acuerdos confirmados"),
                new("ai_precision", "Confirmados sin correcciones", Percent(aiUsage.Uncorrected, aiUsage.Confirmed), IndicatorUnits.Percent, $"{aiUsage.Uncorrected} de {aiUsage.Confirmed} acuerdos", null),
            ]),
            new("sellers", "Vendedores",
            [
                Count("slow_listings", "Productos sin vender", slow, "Recibieron el aviso de 3 semanas sin venderse"),
                Count("sold_after_notice", "Vendidos después del aviso", soldAfter, $"De {slow} avisados"),
            ]),
            new("trust", "Confianza",
            [
                new("rating_average", "Calificación promedio", Round(confidence.Ratings.Average), IndicatorUnits.Stars, $"{confidence.Ratings.Count} calificaciones", null),
                Count("reports_upheld", "Reportes procedentes", confidence.Reports.Upheld, $"{confidence.Reports.Created} reportes en el periodo"),
            ]),
        ];

        return Result<UsageIndicatorsResponse>.Ok(new UsageIndicatorsResponse(period ?? IndicatorPeriods.Week, from, now, areas));
    }

    /// <summary>Un porcentaje de 0 a 100 con un decimal, o <c>null</c> si no hay base.</summary>
    /// <param name="part">La parte.</param>
    /// <param name="total">El total.</param>
    /// <returns>El porcentaje.</returns>
    public static double? Percent(long part, long total) => total == 0 ? null : Math.Round(100d * part / total, 1, MidpointRounding.AwayFromZero);

    /// <summary>Un cociente con un decimal, o <c>null</c> si no hay base.</summary>
    /// <param name="part">La parte.</param>
    /// <param name="total">El total.</param>
    /// <returns>El cociente.</returns>
    public static double? Ratio(long part, long total) => total == 0 ? null : Math.Round((double)part / total, 1, MidpointRounding.AwayFromZero);

    private static double? Round(double? value) => value is { } v ? Math.Round(v, 1, MidpointRounding.AwayFromZero) : null;

    private static string Hours(double hours) => $"{Math.Round(hours, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture)} h";

    private static Indicator Count(string key, string label, long value, string detail, IReadOnlyList<IndicatorBreakdown>? breakdown = null) =>
        new(key, label, value, IndicatorUnits.Count, detail, breakdown);

    // HU-12: las busquedas guardadas (de personas del campus) que recibieron "¡Ya apareció lo que buscabas!".
    private async Task<long> AttendedSearchesAsync(string campus, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var sent = await notices.SentAsync(NotificationTypes.SavedSearchMatch, from, to, cancellationToken).ConfigureAwait(false);
        var ours = await users.OfCommunityAsync(campus, [.. sent.Select(n => n.UserId).Distinct(StringComparer.Ordinal)], cancellationToken).ConfigureAwait(false);
        return sent
            .Where(n => ours.Contains(n.UserId) && n.DedupeKey is not null)
            .Select(n => n.DedupeKey!.Split(':') is [_, var search, ..] ? search : n.DedupeKey)
            .Distinct(StringComparer.Ordinal)
            .LongCount();
    }

    // HU-36: los avisos de "Llevan tiempo sin venderse" del periodo y cuantas de esas publicaciones se vendieron despues.
    private async Task<(long Notified, long SoldAfter)> SlowListingsAsync(string campus, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var sent = await notices.SentAsync(NotificationTypes.SlowListing, from, to, cancellationToken).ConfigureAwait(false);
        var ours = await users.OfCommunityAsync(campus, [.. sent.Select(n => n.UserId).Distinct(StringComparer.Ordinal)], cancellationToken).ConfigureAwait(false);
        var since = sent
            .Where(n => ours.Contains(n.UserId) && n.DedupeKey is not null && n.DedupeKey.StartsWith(NotificationTypes.SlowListing + ":", StringComparison.Ordinal))
            .GroupBy(n => n.DedupeKey![(NotificationTypes.SlowListing.Length + 1)..], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Min(n => n.CreatedAt), StringComparer.Ordinal);
        return (since.Count, await orders.SoldAfterAsync(since, cancellationToken).ConfigureAwait(false));
    }
}
