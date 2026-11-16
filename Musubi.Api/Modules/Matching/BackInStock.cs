using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Reputation;

namespace Musubi.Api.Modules.Matching;

/// <summary>
/// "¡Volvió lo que esperabas!" (HU-51, maqueta <c>Producto</c> → "Avísame"): cuando una publicacion agotada vuelve a
/// tener piezas, a cada persona que pidio el aviso le llega uno que abre la publicacion, y su aviso se borra (decision de
/// la usuaria del 16-Nov-2026: se usa una vez). Nunca a quien vende ni entre personas bloqueadas (HU-57).
/// </summary>
internal sealed class BackInStockNotifier(
    IProductDirectory products,
    IStockAlertDirectory alerts,
    IBlockDirectory blocks,
    INotificationCenter center)
{
    /// <summary>Avisos que se toman en cada revision (los demas esperan a la siguiente reposicion).</summary>
    public const int MaxAlerts = 200;

    /// <summary>Clave para no repetir el aviso.</summary>
    /// <param name="alertId">El aviso.</param>
    /// <returns>La clave.</returns>
    public static string DedupeKey(string alertId) => $"back_in_stock:{alertId}";

    /// <summary>Revisa una publicacion y avisa si de verdad volvio a tener piezas.</summary>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>A cuantas personas se aviso.</returns>
    public async Task<int> NotifyAsync(string productId, CancellationToken cancellationToken)
    {
        // Se vuelve a leer: entre la reposicion y esta revision pudo agotarse otra vez o pausarse.
        if (await products.FindAsync(productId, cancellationToken).ConfigureAwait(false) is not { Available: true, Stock: > 0 } product)
        {
            return 0;
        }

        List<string> excluded = [product.SellerId, .. await blocks.HiddenForAsync(product.SellerId, cancellationToken).ConfigureAwait(false)];
        var taken = await alerts.TakeAsync(product.Id, excluded, MaxAlerts, cancellationToken).ConfigureAwait(false);
        var seller = ChatService.FirstName(product.SellerName);
        var left = product.Stock == 1 ? "Queda 1." : $"Quedan {product.Stock}.";
        foreach (var alert in taken)
        {
            await center.NotifyAsync(
                new Notice(
                    alert.UserId,
                    NotificationTypes.BackInStock,
                    "¡Volvió lo que esperabas!",
                    $"{(seller.Length == 0 ? "Quien vende" : seller)} repuso «{product.Title}». {left}",
                    NotificationTargets.Product,
                    product.Id,
                    NotificationCategories.Matches,
                    DedupeKey(alert.Id)),
                cancellationToken).ConfigureAwait(false);
        }

        return taken.Count;
    }
}

/// <summary>Cola en memoria de las publicaciones que pudieron volver (como la de las publicaciones nuevas, HU-29).</summary>
internal sealed class BackInStockQueue : IProductRestocks
{
    private readonly Channel<string> channel = Channel.CreateBounded<string>(new BoundedChannelOptions(500)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    /// <summary>Lector de la cola.</summary>
    public ChannelReader<string> Reader => channel.Reader;

    /// <inheritdoc />
    public bool Restocked(string productId) => channel.Writer.TryWrite(productId);
}

/// <summary>Revisa cada reposicion en segundo plano (RNF-11). Una que falla no detiene la cola (RNF-15).</summary>
internal sealed partial class BackInStockWorker(BackInStockQueue queue, BackInStockNotifier notifier, ILogger<BackInStockWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var productId in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await notifier.NotifyAsync(productId, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se revisaron los avisos de disponibilidad de una publicacion ({Error})")]
    private static partial void LogFailed(ILogger logger, string error);
}
