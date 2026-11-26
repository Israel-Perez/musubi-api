using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Notifications.Data;

namespace Musubi.Api.Modules.Notifications;

/// <summary>Contrato publico de M-09 para M-14 (HU-42): que avisos se enviaron, sin su texto.</summary>
internal interface INotificationIndicators
{
    /// <summary>Los avisos de un tipo del periodo.</summary>
    /// <param name="type">El tipo (<see cref="NotificationTypes"/>).</param>
    /// <param name="from">Inicio (UTC, inclusivo).</param>
    /// <param name="to">Fin (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>A quien, con que clave y cuando.</returns>
    Task<IReadOnlyList<NoticeSent>> SentAsync(string type, DateTime from, DateTime to, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre los avisos (se guardan 60 dias).</summary>
internal sealed class NotificationIndicators(INotificationRepository notifications) : INotificationIndicators
{
    /// <summary>Avisos que se leen como maximo.</summary>
    public const int MaxNotices = 10_000;

    /// <inheritdoc />
    public Task<IReadOnlyList<NoticeSent>> SentAsync(string type, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        notifications.FindSentAsync(type, from, to, MaxNotices, cancellationToken);
}
