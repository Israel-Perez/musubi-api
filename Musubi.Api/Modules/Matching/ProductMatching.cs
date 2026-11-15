using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Matching.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.Reputation;

namespace Musubi.Api.Modules.Matching;

/// <summary>
/// "Alguien publicó lo que buscas" (HU-29, RF-38, maqueta <c>Notificaciones</c> → "Coincidencias"; decision de la
/// usuaria del 14-Nov-2026): al publicar, a quien tenga una solicitud activa compatible (las mismas reglas de HU-19) le
/// llega un aviso que abre la publicacion. Uno por solicitud y publicacion (RN-29), hasta 3 al dia por persona (RN-30)
/// y nunca entre personas bloqueadas ni a quien vende (RN-31, HU-57).
/// </summary>
internal sealed class ProductMatchNotifier(
    IWantedMatchDirectory wanted,
    IBlockDirectory blocks,
    INotificationCenter center,
    IMatchLedger ledger,
    TimeProvider time)
{
    /// <summary>Solicitudes candidatas que se leen como maximo.</summary>
    public const int MaxRequests = 200;

    /// <summary>Clave para no repetir el aviso de una publicacion a una solicitud (RN-29).</summary>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="productId">La publicacion.</param>
    /// <returns>La clave.</returns>
    public static string DedupeKey(string requestId, string productId) => $"product_match:{requestId}:{productId}";

    /// <summary>Presupuesto minimo para que un precio quepa con el margen de 20 % (RN-28).</summary>
    /// <param name="priceCents">Precio.</param>
    /// <returns>El presupuesto minimo.</returns>
    public static long MinBudget(long priceCents) =>
        (long)Math.Ceiling(priceCents * 100m / (100 + MatchRules.BudgetMarginPercent));

    /// <summary>Busca y avisa.</summary>
    /// <param name="product">La publicacion nueva.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>A cuantas personas se aviso.</returns>
    public async Task<int> NotifyAsync(ProductPublished product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        List<string> excluded = [product.SellerId, .. await blocks.HiddenForAsync(product.SellerId, cancellationToken).ConfigureAwait(false)];
        var requests = await wanted.ActiveCandidatesAsync(
            product.CommunityId,
            product.CategorySlug,
            MinBudget(product.PriceCents),
            excluded,
            MaxRequests,
            cancellationToken).ConfigureAwait(false);

        // RN-28: la mitad de las palabras buscadas; RN-29: de cada persona, solo su solicitud que mas se parece.
        var best = requests
            .Select(request => (Request: request, Share: MatchRules.Share(MatchRules.Terms(request.Title), product.Words)))
            .Where(match => match.Share >= MatchRules.MinShare && product.PriceCents <= MatchRules.MaxPrice(match.Request.BudgetCents))
            .GroupBy(match => match.Request.RequesterId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(match => match.Share).First().Request)
            .ToList();

        var since = MatchRules.CampusDayStart(time.GetUtcNow().UtcDateTime);
        var notified = 0;
        foreach (var request in best)
        {
            // RN-30: hasta 3 al dia; la coincidencia igual se registra (RF-25).
            var allowed = await center.CountSinceAsync(request.RequesterId, NotificationTypes.ProductMatch, since, cancellationToken).ConfigureAwait(false) < MatchRules.DailyLimit;
            if (allowed)
            {
                await center.NotifyAsync(
                    new Notice(
                        request.RequesterId,
                        NotificationTypes.ProductMatch,
                        "Alguien publicó lo que buscas",
                        $"«{product.Title}» · {OfferService.Pesos(product.PriceCents)} coincide con tu solicitud de {request.Title}.",
                        NotificationTargets.Product,
                        product.Id,
                        NotificationCategories.Matches,
                        DedupeKey(request.Id, product.Id)),
                    cancellationToken).ConfigureAwait(false);
                notified++;
            }

            await ledger.RecordAsync(
                new MatchRecord(product.CommunityId, request.Id, product.Id, request.RequesterId, product.SellerId, NotifiedRequester: allowed, NotifiedSeller: false),
                cancellationToken).ConfigureAwait(false);
        }

        return notified;
    }
}

/// <summary>Cola en memoria de las publicaciones por revisar (como la de las solicitudes, HU-19).</summary>
internal sealed class ProductMatchQueue : IProductPublications
{
    private readonly Channel<ProductPublished> channel = Channel.CreateBounded<ProductPublished>(new BoundedChannelOptions(500)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    /// <summary>Lector de la cola.</summary>
    public ChannelReader<ProductPublished> Reader => channel.Reader;

    /// <inheritdoc />
    public bool Published(ProductPublished product) => channel.Writer.TryWrite(product);
}

/// <summary>Revisa cada publicacion nueva en segundo plano (RNF-11). Una que falla no detiene la cola (RNF-15).</summary>
internal sealed partial class ProductMatchWorker(ProductMatchQueue queue, ProductMatchNotifier notifier, ILogger<ProductMatchWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var product in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await notifier.NotifyAsync(product, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se revisaron las solicitudes de una publicacion ({Error})")]
    private static partial void LogFailed(ILogger logger, string error);
}
