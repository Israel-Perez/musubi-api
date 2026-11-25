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
using Musubi.Api.Modules.Matching.Data;
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
/// (RN-28) o, con HU-60, que signifiquen lo mismo (<see cref="SemanticThreshold"/>), y el precio no mas de 20 % arriba
/// del presupuesto. Un aviso por solicitud y persona (RN-29) y hasta 3 al
/// dia por persona (RN-30). Nunca sobre lo propio ni entre personas bloqueadas (RN-31, HU-57).
/// </summary>
internal static class MatchRules
{
    /// <summary>Parte de las palabras buscadas que debe tener la publicacion.</summary>
    public const double MinShare = 0.5;

    /// <summary>Lo que el precio puede pasar del presupuesto, en por ciento.</summary>
    public const int BudgetMarginPercent = 20;

    /// <summary>
    /// HU-60: similitud de los vectores desde la que una publicacion le sirve a una solicitud aunque no compartan
    /// palabras. Medida el 25-Nov-2026 con 16 solicitudes y 24 publicaciones del campus y Gemini
    /// (<c>gemini-embedding-001</c>, 256 dimensiones, <c>RETRIEVAL_QUERY</c>/<c>RETRIEVAL_DOCUMENT</c>): dentro de la
    /// categoria, precision 0.75 y cobertura 0.86, contra 0.67 y 0.19 de las palabras
    /// (<c>SemanticEvaluationTests</c>).
    /// </summary>
    public const double SemanticThreshold = 0.74;

    /// <summary>HU-18: desde aqui una publicacion es "parecida" por significado (RF-24).</summary>
    public const double SimilarSemanticThreshold = 0.70;

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

    /// <summary>RN-28 con HU-60: la mitad de las palabras o el mismo significado.</summary>
    /// <param name="share">Parte de las palabras (<see cref="Share"/>).</param>
    /// <param name="similarity">Similitud de los vectores, o <c>null</c> sin ellos.</param>
    /// <returns>Si coincide el nombre.</returns>
    public static bool Compatible(double share, double? similarity) =>
        share >= MinShare || similarity >= SemanticThreshold;

    /// <summary>Para ordenar: la mas parecida, por palabras o por significado.</summary>
    /// <param name="share">Parte de las palabras.</param>
    /// <param name="similarity">Similitud, o <c>null</c>.</param>
    /// <returns>De 0 a 1.</returns>
    public static double Score(double share, double? similarity) => Math.Max(share, similarity ?? 0);

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
internal sealed class WantedMatchNotifier(
    IProductMatches products,
    IBlockDirectory blocks,
    INotificationCenter center,
    IMatchLedger ledger,
    SemanticMatcher semantic,
    TimeProvider time)
{
    /// <summary>Busca y avisa.</summary>
    /// <param name="request">La solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>A cuantas personas se aviso.</returns>
    public async Task<int> NotifyAsync(WantedPublished request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var terms = MatchRules.Terms(request.Title);
        // HU-60: con vectores tambien se buscan las que no comparten palabras; sin ellos, solo por palabras (RNF-15).
        var bySense = semantic.Enabled;
        if (terms.Count == 0 && !bySense)
        {
            return 0;
        }

        // RN-31 y HU-57: ni lo propio ni lo de quien bloqueo o fue bloqueado.
        List<string> excluded = [request.RequesterId, .. await blocks.HiddenForAsync(request.RequesterId, cancellationToken).ConfigureAwait(false)];
        var candidates = await products.FindCandidatesAsync(
            new ProductMatchQuery(request.CommunityId, request.CategorySlug, terms, MatchRules.MaxPrice(request.BudgetCents), excluded, MatchRules.MaxCandidates, bySense),
            cancellationToken).ConfigureAwait(false);
        // Segundo plano (RNF-11): aqui se calculan y guardan los vectores que falten, incluido el de la solicitud (lo
        // usa despues "Ya coinciden con lo que buscas").
        var text = MatchText.Wanted(request.RequestId, request.RequesterId, request.CommunityId, request.Title);
        var similarity = await semantic.SimilaritiesAsync(
            text,
            [.. candidates.Select(p => MatchText.Product(p.Id, p.SellerId, request.CommunityId, p.Title, p.Description))],
            computeMissing: true,
            cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            await semantic.RememberAsync(text, cancellationToken).ConfigureAwait(false);
        }

        // RN-28 y RN-29: de cada persona, su publicacion mas parecida (y la de precio mas cercano al presupuesto).
        var best = candidates
            .Select(product =>
            {
                var share = MatchRules.Share(terms, product.Words);
                double? sense = similarity.TryGetValue(product.Id, out var s) ? s : null;
                return (Product: product, Compatible: MatchRules.Compatible(share, sense), Score: MatchRules.Score(share, sense));
            })
            .Where(match => match.Compatible)
            .GroupBy(match => match.Product.SellerId, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(match => match.Score)
                .ThenBy(match => Math.Abs(match.Product.PriceCents - request.BudgetCents))
                .First())
            .OrderByDescending(match => match.Score)
            .ThenBy(match => Math.Abs(match.Product.PriceCents - request.BudgetCents))
            .Take(MatchRules.MaxSellers)
            .ToList();

        var since = MatchRules.CampusDayStart(time.GetUtcNow().UtcDateTime);
        var first = ChatService.FirstName(request.RequesterName);
        var notified = 0;
        foreach (var (product, _, _) in best)
        {
            // RN-30: hasta 3 al dia; las demas siguen en el tablero. La coincidencia igual se registra (RF-25).
            var allowed = await center.CountSinceAsync(product.SellerId, NotificationTypes.WantedMatch, since, cancellationToken).ConfigureAwait(false) < MatchRules.DailyLimit;
            await ledger.RecordAsync(
                new MatchRecord(request.CommunityId, request.RequestId, product.Id, request.RequesterId, product.SellerId, NotifiedRequester: false, NotifiedSeller: allowed),
                cancellationToken).ConfigureAwait(false);
            if (!allowed)
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
