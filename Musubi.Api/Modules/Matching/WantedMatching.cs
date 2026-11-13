using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Reputation;

namespace Musubi.Api.Modules.Matching;

/// <summary>Una solicitud recien publicada, con lo necesario para buscar coincidencias (HU-19).</summary>
/// <param name="RequestId">Solicitud.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="RequesterId">Quien busca.</param>
/// <param name="RequesterName">Su nombre corto ("Ana R.").</param>
/// <param name="Title">Lo que busca.</param>
/// <param name="CategorySlug">Categoria, o <c>null</c> si es "otro" (cualquiera).</param>
/// <param name="BudgetCents">Presupuesto.</param>
internal sealed record WantedPublished(
    string RequestId,
    string CommunityId,
    string RequesterId,
    string RequesterName,
    string Title,
    string? CategorySlug,
    long BudgetCents);

/// <summary>
/// Contrato publico de M-06 para M-05 (HU-19): al publicar una solicitud, la busqueda de publicaciones compatibles
/// corre en segundo plano (RNF-11). Nunca falla hacia quien publica.
/// </summary>
internal interface IWantedMatching
{
    /// <summary>Encola la busqueda sin esperar.</summary>
    /// <param name="request">La solicitud.</param>
    /// <returns><c>true</c> si entro a la cola.</returns>
    bool Enqueue(WantedPublished request);
}

/// <summary>
/// Reglas de compatibilidad (decisiones de la usuaria, 13-Nov-2026): misma categoria (o cualquiera si la solicitud
/// es "otro"), la mitad o mas de las palabras del nombre buscado en el nombre o la descripcion de la publicacion
/// (RN-28) y el precio no mas de 20 % arriba del presupuesto. Un aviso por solicitud y persona (RN-29) y hasta 3 al
/// dia por persona (RN-30). Nunca sobre lo propio ni entre personas bloqueadas (RN-31, HU-57).
/// </summary>
internal static class MatchRules
{
    /// <summary>Parte de las palabras buscadas que debe tener la publicacion.</summary>
    public const double MinShare = 0.5;

    /// <summary>Lo que el precio puede pasar del presupuesto, en por ciento.</summary>
    public const int BudgetMarginPercent = 20;

    /// <summary>Avisos de solicitud compatible por persona y dia del campus.</summary>
    public const int DailyLimit = 3;

    /// <summary>Palabras del nombre que se comparan como maximo.</summary>
    public const int MaxTerms = 8;

    /// <summary>Publicaciones candidatas que se leen como maximo.</summary>
    public const int MaxCandidates = 200;

    /// <summary>Personas avisadas por solicitud como maximo.</summary>
    public const int MaxSellers = 50;

    /// <summary>Clave para no repetir el aviso de una solicitud a la misma persona (RN-29).</summary>
    /// <param name="requestId">La solicitud.</param>
    /// <returns>La clave.</returns>
    public static string DedupeKey(string requestId) => $"wanted_match:{requestId}";

    // Palabras que no dicen que se busca (ya normalizadas: "las" queda "la", "unos" queda "uno").
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "al", "con", "de", "del", "el", "en", "la", "lo", "mi", "para", "por", "que", "sin", "su", "tu", "un", "una", "uno",
    };

    /// <summary>Las palabras del nombre buscado que cuentan.</summary>
    /// <param name="title">Lo que se busca.</param>
    /// <returns>Palabras normalizadas, sin repetir.</returns>
    public static IReadOnlyList<string> Terms(string title) =>
        [.. SearchWord.Words(title).Where(word => word.Length > 1 && !Ignored.Contains(word)).Take(MaxTerms)];

    /// <summary>Que parte de los terminos tiene la publicacion (una palabra que empieza igual cuenta, como al buscar).</summary>
    /// <param name="terms">Los terminos.</param>
    /// <param name="words">Las palabras de la publicacion.</param>
    /// <returns>De 0 a 1.</returns>
    public static double Share(IReadOnlyList<string> terms, IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(words);
        return terms.Count == 0
            ? 0
            : terms.Count(term => words.Any(word => word.StartsWith(term, StringComparison.Ordinal))) / (double)terms.Count;
    }

    /// <summary>Precio maximo para un presupuesto.</summary>
    /// <param name="budgetCents">Presupuesto.</param>
    /// <returns>El presupuesto mas el margen.</returns>
    public static long MaxPrice(long budgetCents) => budgetCents + (budgetCents * BudgetMarginPercent / 100);

    /// <summary>Inicio del dia del campus (UTC-6) que contiene a <paramref name="now"/>.</summary>
    /// <param name="now">Ahora (UTC).</param>
    /// <returns>Las 00:00 del campus, en UTC.</returns>
    public static DateTime CampusDayStart(DateTime now)
    {
        var local = now + PurchaseRequests.WantedService.CampusOffset;
        return DateTime.SpecifyKind(local.Date - PurchaseRequests.WantedService.CampusOffset, DateTimeKind.Utc);
    }
}

/// <summary>
/// "Hay una solicitud compatible con tu producto" (HU-19, RF-39, maqueta <c>Notificaciones</c> → "Coincidencias"):
/// busca las publicaciones compatibles y avisa a cada persona una vez, con su publicacion mas parecida.
/// </summary>
internal sealed class WantedMatchNotifier(IProductMatches products, IBlockDirectory blocks, INotificationCenter center, TimeProvider time)
{
    /// <summary>Busca y avisa.</summary>
    /// <param name="request">La solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>A cuantas personas se aviso.</returns>
    public async Task<int> NotifyAsync(WantedPublished request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var terms = MatchRules.Terms(request.Title);
        if (terms.Count == 0)
        {
            return 0;
        }

        // RN-31 y HU-57: ni lo propio ni lo de quien bloqueo o fue bloqueado.
        List<string> excluded = [request.RequesterId, .. await blocks.HiddenForAsync(request.RequesterId, cancellationToken).ConfigureAwait(false)];
        var candidates = await products.FindCandidatesAsync(
            new ProductMatchQuery(request.CommunityId, request.CategorySlug, terms, MatchRules.MaxPrice(request.BudgetCents), excluded, MatchRules.MaxCandidates),
            cancellationToken).ConfigureAwait(false);

        // RN-28 y RN-29: de cada persona, su publicacion mas parecida (y la de precio mas cercano al presupuesto).
        var best = candidates
            .Select(product => (Product: product, Share: MatchRules.Share(terms, product.Words)))
            .Where(match => match.Share >= MatchRules.MinShare)
            .GroupBy(match => match.Product.SellerId, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(match => match.Share)
                .ThenBy(match => Math.Abs(match.Product.PriceCents - request.BudgetCents))
                .First())
            .OrderByDescending(match => match.Share)
            .ThenBy(match => Math.Abs(match.Product.PriceCents - request.BudgetCents))
            .Take(MatchRules.MaxSellers)
            .ToList();

        var since = MatchRules.CampusDayStart(time.GetUtcNow().UtcDateTime);
        var first = ChatService.FirstName(request.RequesterName);
        var notified = 0;
        foreach (var (product, _) in best)
        {
            // RN-30: hasta 3 al dia; las demas siguen en el tablero.
            if (await center.CountSinceAsync(product.SellerId, NotificationTypes.WantedMatch, since, cancellationToken).ConfigureAwait(false) >= MatchRules.DailyLimit)
            {
                continue;
            }

            await center.NotifyAsync(
                new Notice(
                    product.SellerId,
                    NotificationTypes.WantedMatch,
                    "Hay una solicitud compatible con tu producto",
                    $"{first} busca {request.Title}. Tu publicación «{product.Title}» coincide.",
                    NotificationTargets.WantedBoard,
                    request.RequestId,
                    NotificationCategories.Matches,
                    MatchRules.DedupeKey(request.RequestId)),
                cancellationToken).ConfigureAwait(false);
            notified++;
        }

        return notified;
    }
}

/// <summary>Cola en memoria de las solicitudes por revisar (como la de push).</summary>
internal sealed class WantedMatchQueue : IWantedMatching
{
    private readonly Channel<WantedPublished> channel = Channel.CreateBounded<WantedPublished>(new BoundedChannelOptions(500)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    /// <summary>Lector de la cola.</summary>
    public ChannelReader<WantedPublished> Reader => channel.Reader;

    /// <inheritdoc />
    public bool Enqueue(WantedPublished request) => channel.Writer.TryWrite(request);
}

/// <summary>Revisa cada solicitud publicada en segundo plano (RNF-11). Una que falla no detiene la cola (RNF-15).</summary>
internal sealed partial class WantedMatchWorker(WantedMatchQueue queue, WantedMatchNotifier notifier, ILogger<WantedMatchWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await notifier.NotifyAsync(request, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se revisaron las coincidencias de una solicitud ({Error})")]
    private static partial void LogFailed(ILogger logger, string error);
}
