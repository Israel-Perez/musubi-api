using Musubi.Api.Modules.Notifications;

namespace UnitTest.Common;

/// <summary>Centro de avisos falso: guarda cada aviso que dan los servicios (HU-31).</summary>
internal sealed class RecordingNotificationCenter : INotificationCenter
{
    public List<Notice> Sent { get; } = [];

    public Task NotifyAsync(Notice notice, CancellationToken cancellationToken)
    {
        Sent.Add(notice);
        return Task.CompletedTask;
    }

    /// <summary>Los avisos de ese tipo para esa persona (sin mirar la hora: cada prueba es de un solo dia).</summary>
    public Task<long> CountSinceAsync(string userId, string type, DateTime since, CancellationToken cancellationToken) =>
        Task.FromResult((long)Sent.Count(n => n.UserId == userId && n.Type == type));
}
