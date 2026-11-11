using Musubi.Api.Modules.Notifications.Data;
using Musubi.TestShared.Notifications;

namespace UnitTest.Data;

/// <summary>HU-31: los fakes en memoria cumplen el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryNotificationRepositoryTests : NotificationRepositoryContract
{
    internal override INotificationRepository Notifications { get; } = new InMemoryNotificationRepository();

    internal override IDeviceTokenRepository Tokens { get; } = new InMemoryDeviceTokenRepository();
}
