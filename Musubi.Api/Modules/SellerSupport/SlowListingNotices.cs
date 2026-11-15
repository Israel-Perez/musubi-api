using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Orders;

namespace Musubi.Api.Modules.SellerSupport;

/// <summary>
/// "Tu bata lleva 34 días sin venderse" (maqueta <c>Notificaciones</c>, HU-36, RN-35; decision de la usuaria del
/// 15-Nov-2026: una sola vez por publicacion, al cumplir 3 semanas). Revisa las que cruzaron el umbral en los ultimos
/// dos dias (si el servicio estuvo apagado una vuelta, no se pierden); la <c>dedupeKey</c> deja pasar un aviso por
/// publicacion aunque las dos instancias revisen.
/// </summary>
internal sealed class SlowListingJob(ISellerListings listings, IOpenOrders orders, IProductDemand demand, INotificationCenter center)
{
    /// <summary>Publicaciones que se revisan por vuelta.</summary>
    public const int BatchSize = 200;

    /// <summary>Lo que se mira hacia atras desde el umbral.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(2);

    /// <summary>Clave para no repetir el aviso de una publicacion.</summary>
    /// <param name="productId">La publicacion.</param>
    /// <returns>La clave.</returns>
    public static string DedupeKey(string productId) => $"{NotificationTypes.SlowListing}:{productId}";

    /// <summary>"Tu bata": la primera palabra del nombre, o el nombre entre comillas si no sirve.</summary>
    /// <param name="title">Nombre de la publicacion.</param>
    /// <param name="days">Dias sin venderse.</param>
    /// <returns>El titulo del aviso.</returns>
    public static string NoticeTitle(string title, int days)
    {
        ArgumentNullException.ThrowIfNull(title);
        var first = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()?.TrimEnd(',', '.', ':', ';') ?? string.Empty;
        string who;
        if (first.Length >= 3 && first.All(char.IsLetter))
        {
            // "Calculadora" queda "calculadora"; "iPad" o "USB" se quedan como estan.
            who = "Tu " + (char.IsUpper(first[0]) && char.IsLower(first[1]) ? char.ToLowerInvariant(first[0]) + first[1..] : first);
        }
        else
        {
            var name = title.Trim();
            who = $"«{(name.Length > 60 ? name[..59].TrimEnd() + "…" : name)}»";
        }

        return $"{who} lleva {days} días sin venderse";
    }

    /// <summary>"3 estudiantes buscan algo similar. Te damos ideas para venderla."</summary>
    /// <param name="people">Personas que la buscan.</param>
    /// <returns>El texto del aviso.</returns>
    public static string NoticeBody(int people) => people switch
    {
        0 => "Te damos ideas para vender más rápido: el precio, la descripción y las fotos.",
        1 => "1 estudiante busca algo similar. Te damos ideas para vender más rápido.",
        _ => $"{people} estudiantes buscan algo similar. Te damos ideas para vender más rápido.",
    };

    /// <summary>Revisa una vez.</summary>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas publicaciones se avisaron.</returns>
    public async Task<int> RunAsync(DateTime now, CancellationToken cancellationToken)
    {
        var threshold = now.AddDays(-SellerSupportService.ThresholdDays);
        var crossed = await listings.ListedBetweenAsync(threshold - Window, threshold, BatchSize, cancellationToken).ConfigureAwait(false);
        var slow = await SellerSupportService.WithoutOrdersAsync(crossed, orders, BatchSize, cancellationToken).ConfigureAwait(false);
        foreach (var listing in slow)
        {
            var found = await demand.ForAsync(listing.Match, cancellationToken).ConfigureAwait(false);
            await center.NotifyAsync(
                new Notice(
                    listing.Match.SellerId,
                    NotificationTypes.SlowListing,
                    NoticeTitle(listing.Product.Title, SellerSupportService.DaysListed(listing.Product.PublishedAt, now)),
                    NoticeBody(found.People),
                    NotificationTargets.MyProducts,
                    listing.Product.Id,
                    NotificationCategories.Matches,
                    DedupeKey(listing.Product.Id)),
                cancellationToken).ConfigureAwait(false);
        }

        return slow.Count;
    }
}

/// <summary>Corre <see cref="SlowListingJob"/> cada <see cref="ReminderOptions.ReminderMinutes"/> (como los recordatorios).</summary>
internal sealed partial class SlowListingWorker(
    SlowListingJob job,
    IOptions<ReminderOptions> options,
    TimeProvider time,
    ILogger<SlowListingWorker> logger) : BackgroundService
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
                // Sin base, se intenta en la siguiente vuelta.
                LogSkipped(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Revision de publicaciones sin venderse omitida ({Error})")]
    private static partial void LogSkipped(ILogger logger, string error);
}
