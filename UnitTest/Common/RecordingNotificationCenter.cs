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
}
