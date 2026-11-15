using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Notifications.Data;

namespace Musubi.TestShared.Notifications;

/// <summary>
/// HU-31: comportamiento esperado de los repositorios de avisos y de tokens. Corre contra los fakes en memoria
/// (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class NotificationRepositoryContract
{
    // Relativa a hoy: la coleccion tiene TTL y MongoDB borra lo que ya vencio en la fecha real (una fecha fija hace que
    // la prueba falle sola con el tiempo).
    private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(18);

    internal abstract INotificationRepository Notifications { get; }

    internal abstract IDeviceTokenRepository Tokens { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static NotificationDocument Notice(
        string userId,
        string category = NotificationCategories.Orders,
        string? dedupe = null,
        int minutes = 0,
        string? type = null,
        string target = NotificationTargets.Order) => new()
    {
        Id = AuthIds.New(),
        UserId = userId,
        Type = type ?? (category == NotificationCategories.Orders ? NotificationTypes.OrderCreated : NotificationTypes.OfferReceived),
        Category = category,
        Title = "Tienes un pedido nuevo",
        Body = "Ana pidió 1 × Calculadora.",
        Target = new NotificationTarget { Type = target, Id = AuthIds.New() },
        DedupeKey = dedupe,
        CreatedAt = Now.AddMinutes(minutes),
        ExpiresAt = Now.AddDays(60),
    };

    [Fact]
    public async Task APage_IsNewestFirst_ByCategory_WithCursor_AndOnlyMine()
    {
        var ana = AuthIds.New();
        var first = Notice(ana);
        var second = Notice(ana, NotificationCategories.Requests, minutes: 1);
        var third = Notice(ana, minutes: 2);
        foreach (var n in new[] { first, second, third, Notice(AuthIds.New()) })
        {
            Assert.True(await Notifications.TryInsertAsync(n, Ct));
        }

        var all = await Notifications.FindPageAsync(ana, null, null, 10, Ct);
        var orders = await Notifications.FindPageAsync(ana, NotificationCategories.Orders, null, 10, Ct);
        var older = await Notifications.FindPageAsync(ana, null, third.Id, 10, Ct);

        Assert.Equal([third.Id, second.Id, first.Id], all.Select(n => n.Id));
        Assert.Equal([third.Id, first.Id], orders.Select(n => n.Id));
        Assert.Equal([second.Id, first.Id], older.Select(n => n.Id));
        Assert.Equal(NotificationTargets.Order, all[0].Target.Type);
    }

    // Los tipos que quedan en el historial (los de "message" solo van por push, sin categoria).
    private static IEnumerable<string> StoredTypes() =>
        typeof(NotificationTypes).GetFields()
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(type => type != NotificationTypes.Message);

    private static IEnumerable<string> Targets() =>
        typeof(NotificationTargets).GetFields().Select(field => (string)field.GetRawConstantValue()!);

    [Fact]
    public async Task EveryTypeAndTargetOfTheCode_CanBeStored()
    {
        // db/019 no conocia wanted_match, offer_closed ni sus destinos y MongoDB los rechazaba (db/024 lo corrige).
        var ana = AuthIds.New();

        var types = new List<(string Type, bool Saved)>();
        foreach (var type in StoredTypes())
        {
            types.Add((type, await Notifications.TryInsertAsync(Notice(ana, NotificationCategories.Matches, type: type), Ct)));
        }

        var targets = new List<(string Target, bool Saved)>();
        foreach (var target in Targets())
        {
            targets.Add((target, await Notifications.TryInsertAsync(Notice(ana, target: target), Ct)));
        }

        Assert.All(types, t => Assert.True(t.Saved, t.Type));
        Assert.All(targets, t => Assert.True(t.Saved, t.Target));
    }

    [Fact]
    public async Task TheSameDedupeKey_IsSavedOnlyOnce_PerPerson()
    {
        var ana = AuthIds.New();
        var luis = AuthIds.New();

        var once = await Notifications.TryInsertAsync(Notice(ana, dedupe: "request_expiring:w1"), Ct);
        var twice = await Notifications.TryInsertAsync(Notice(ana, dedupe: "request_expiring:w1"), Ct);
        var other = await Notifications.TryInsertAsync(Notice(luis, dedupe: "request_expiring:w1"), Ct);
        var free1 = await Notifications.TryInsertAsync(Notice(ana), Ct);
        var free2 = await Notifications.TryInsertAsync(Notice(ana), Ct);

        Assert.Equal((true, false, true, true, true), (once, twice, other, free1, free2));
    }

    [Fact]
    public async Task MarkRead_OneOrAll_OnlyMine_AndUnreadCounts()
    {
        var ana = AuthIds.New();
        var luis = AuthIds.New();
        var mine = Notice(ana);
        var theirs = Notice(luis);
        await Notifications.TryInsertAsync(mine, Ct);
        await Notifications.TryInsertAsync(Notice(ana, minutes: 1), Ct);
        await Notifications.TryInsertAsync(theirs, Ct);

        var stolen = await Notifications.MarkReadAsync(ana, theirs.Id, Now, Ct);
        var one = await Notifications.MarkReadAsync(ana, mine.Id, Now, Ct);
        var unreadAfterOne = await Notifications.CountUnreadAsync(ana, Ct);
        var rest = await Notifications.MarkReadAsync(ana, null, Now, Ct);

        Assert.Equal((0L, 1L, 1L, 1L), (stolen, one, unreadAfterOne, rest));
        Assert.Equal(0, await Notifications.CountUnreadAsync(ana, Ct));
        Assert.Equal(1, await Notifications.CountUnreadAsync(luis, Ct));
        Assert.NotNull((await Notifications.FindPageAsync(ana, null, null, 10, Ct))[0].ReadAt);
    }

    [Fact]
    public async Task Tokens_MoveToTheLastAccount_Expire_AndAreRemoved()
    {
        var ana = AuthIds.New();
        var luis = AuthIds.New();
        var token = $"fcm-{Guid.NewGuid():N}";
        var old = $"fcm-{Guid.NewGuid():N}";

        await Tokens.UpsertAsync(ana, token, Now, TimeSpan.FromDays(60), Ct);
        await Tokens.UpsertAsync(ana, old, Now.AddDays(-59), TimeSpan.FromDays(60), Ct);
        var anas = await Tokens.FindByUserAsync(ana, Now, Ct);
        var later = await Tokens.FindByUserAsync(ana, Now.AddDays(2), Ct);
        await Tokens.UpsertAsync(luis, token, Now, TimeSpan.FromDays(60), Ct);
        var moved = (await Tokens.FindByUserAsync(ana, Now, Ct), await Tokens.FindByUserAsync(luis, Now, Ct));
        var notMine = await Tokens.RemoveAsync(token, ana, Ct);
        var removed = await Tokens.RemoveAsync(token, luis, Ct);

        Assert.Equal(new[] { old, token }.Order(StringComparer.Ordinal), anas.Order(StringComparer.Ordinal));
        Assert.Equal([token], later);
        Assert.Equal([old], moved.Item1);
        Assert.Equal([token], moved.Item2);
        Assert.False(notMine);
        Assert.True(removed);
        Assert.Empty(await Tokens.FindByUserAsync(luis, Now, Ct));
    }

    [Fact]
    public async Task DeleteByUser_RemovesOnlyThatPersonsNoticesAndTokens()
    {
        var ana = AuthIds.New();
        var luis = AuthIds.New();
        await Notifications.TryInsertAsync(Notice(ana), Ct);
        await Notifications.TryInsertAsync(Notice(luis), Ct);
        await Tokens.UpsertAsync(ana, $"fcm-{Guid.NewGuid():N}", Now, TimeSpan.FromDays(60), Ct);
        await Tokens.UpsertAsync(luis, $"fcm-{Guid.NewGuid():N}", Now, TimeSpan.FromDays(60), Ct);

        Assert.Equal(1, await Notifications.DeleteByUserAsync(ana, Ct));
        Assert.Equal(1, await Tokens.DeleteByUserAsync(ana, Ct));
        Assert.Empty(await Notifications.FindPageAsync(ana, null, null, 10, Ct));
        Assert.Single(await Notifications.FindPageAsync(luis, null, null, 10, Ct));
        Assert.Single(await Tokens.FindByUserAsync(luis, Now, Ct));
    }

    [Fact]
    public async Task CountSince_CountsOnlyThatTypeForThatPerson_FromThatMoment()
    {
        // HU-19: el limite diario de "solicitud compatible" (RN-30).
        var ana = AuthIds.New();
        foreach (var n in new[] { Notice(ana, minutes: -1), Notice(ana), Notice(ana, minutes: 5), Notice(ana, NotificationCategories.Requests, minutes: 5), Notice(AuthIds.New(), minutes: 5) })
        {
            Assert.True(await Notifications.TryInsertAsync(n, Ct));
        }

        Assert.Equal(2, await Notifications.CountSinceAsync(ana, NotificationTypes.OrderCreated, Now, Ct));
        Assert.Equal(1, await Notifications.CountSinceAsync(ana, NotificationTypes.OfferReceived, Now, Ct));
        Assert.Equal(0, await Notifications.CountSinceAsync("no-es-un-id", NotificationTypes.OrderCreated, Now, Ct));
    }
}
