using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Orders;

/// <summary>Registro DI del modulo M-08 (Pedidos, pagos y entregas).</summary>
internal static class OrdersModuleServiceCollectionExtensions
{
    /// <summary>Registra los repositorios (Mongo o en memoria, lazy), el servicio, RN-07 y el borrado de la cuenta.</summary>
    /// <param name="services">La coleccion de servicios.</param>
    public static void AddOrdersModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var orders = new InMemoryOrderRepository();
        services.AddSingleton<IOrderRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? orders
            : new MongoOrderRepository(provider.GetRequiredService<IMongoDatabase>()));
        var points = new InMemoryMeetingPointRepository();
        services.AddSingleton<IMeetingPointRepository>(provider => MongoServiceCollectionExtensions.UseInMemory(provider)
            ? points
            : new MongoMeetingPointRepository(provider.GetRequiredService<IMongoDatabase>()));
        services.AddSingleton<OrderService>();
        services.AddSingleton<OrderLifecycleService>();
        services.AddSingleton<IOpenOrders, OpenOrders>();
        services.AddSingleton<IOrderRatingGate, OrderRatingGate>();
        services.AddSingleton<IMeetingPointDirectory, MeetingPointDirectory>();
        services.AddSingleton<IAccountDataEraser, OrderAccountEraser>();
    }
}

/// <summary>
/// Contrato publico de M-08 para los demas modulos (Modules/README: un modulo no lee las colecciones de otro).
/// Lo usan las publicaciones (RN-07: no se elimina una con pedidos abiertos).
/// </summary>
internal interface IOpenOrders
{
    /// <summary>Si una publicacion tiene pedidos pendientes o confirmados.</summary>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si tiene.</returns>
    Task<bool> ForProductAsync(string productId, CancellationToken cancellationToken);

    /// <summary>Si una conversacion ya tiene un pedido pendiente o confirmado (HU-34: ya no se resume el acuerdo).</summary>
    /// <param name="conversationId">La conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si tiene.</returns>
    Task<bool> ForConversationAsync(string conversationId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de pedidos.</summary>
internal sealed class OpenOrders(IOrderRepository orders) : IOpenOrders
{
    /// <inheritdoc />
    public Task<bool> ForProductAsync(string productId, CancellationToken cancellationToken) =>
        orders.HasOpenForProductAsync(productId, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> ForConversationAsync(string conversationId, CancellationToken cancellationToken) =>
        (await orders.FindByConversationAsync(conversationId, OrderService.MaxPerConversation, cancellationToken).ConfigureAwait(false))
            .Any(o => OrderStatuses.Open.Contains(o.Status));
}

/// <summary>Un punto de entrega del campus para otro modulo.</summary>
/// <param name="Slug">Id del punto.</param>
/// <param name="Name">Nombre.</param>
internal sealed record MeetingPointSummary(string Slug, string Name);

/// <summary>Contrato publico de M-08: los puntos de entrega activos de un campus (HU-33: la IA elige uno de ellos).</summary>
internal interface IMeetingPointDirectory
{
    /// <summary>Los puntos activos, en su orden.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los puntos.</returns>
    Task<IReadOnlyList<MeetingPointSummary>> ListAsync(string communityId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de puntos.</summary>
internal sealed class MeetingPointDirectory(IMeetingPointRepository points) : IMeetingPointDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MeetingPointSummary>> ListAsync(string communityId, CancellationToken cancellationToken) =>
        (await points.ListActiveAsync(communityId, cancellationToken).ConfigureAwait(false))
            .Select(p => new MeetingPointSummary(p.Slug, p.Name))
            .ToList();
}

/// <summary>
/// Borrado de una cuenta (HU-46, R11): sus pedidos abiertos se cancelan y su nombre se reemplaza en los que conserva
/// la otra parte (su historial de compras o ventas no desaparece). Las piezas de sus pedidos confirmados regresan a
/// la publicacion (HU-24).
/// </summary>
internal sealed class OrderAccountEraser(IOrderRepository orders, IProductStock stock, TimeProvider time) : IAccountDataEraser
{
    /// <inheritdoc />
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        var confirmed = await orders.FindConfirmedByUserAsync(userId, cancellationToken).ConfigureAwait(false);
        await orders.ReleaseAccountAsync(userId, OrderService.DeletedName, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        foreach (var item in confirmed.Select(o => o.Item).Where(i => i.ProductId is not null))
        {
            await stock.ReleaseAsync(item.ProductId!, item.Quantity, cancellationToken).ConfigureAwait(false);
        }
    }
}
