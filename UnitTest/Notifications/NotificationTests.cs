using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Notifications.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.TestShared;

namespace UnitTest.Notifications;

/// <summary>
/// HU-31: el centro guarda el aviso (si tiene pestana) y lo encola para push sin fallar hacia quien lo llama; cada
/// persona solo ve y marca los suyos; FCM se autentica con la cuenta de servicio y borra los tokens que ya no existen;
/// "vence manana" y "vencio" se avisan una sola vez.
/// </summary>
public sealed class NotificationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryNotificationRepository notifications = new();
    private readonly InMemoryDeviceTokenRepository tokens = new();
    private readonly InMemoryUserRepository users = new();
    private readonly PushQueue queue = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly List<IDisposable> owned = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var d in owned)
        {
            d.Dispose();
        }
    }

    private NotificationCenter Center() => new(notifications, queue, time, NullLogger<NotificationCenter>.Instance);

    private NotificationService Service() => new(notifications, tokens, new MemberDirectory(new InMemoryCommunityRepository(), users), time);

    private List<PushJob> Queued()
    {
        var jobs = new List<PushJob>();
        while (queue.Reader.TryRead(out var job))
        {
            jobs.Add(job);
        }

        return jobs;
    }

    private async Task<string> MemberAsync(string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"u.{id}@uttt.edu.mx",
                EmailNormalized = $"u.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Ana Sofía Ramírez",
                Status = status,
                EmailVerifiedAt = Now.UtcDateTime,
                CreatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false);
        return id;
    }

    [Fact]
    public async Task ANoticeWithACategory_IsSaved_AndQueuedForPush_WithWhereItLeads()
    {
        var ana = AuthIds.New();
        var order = AuthIds.New();

        await Center().NotifyAsync(
            new Notice(ana, NotificationTypes.OrderCreated, "Tienes un pedido nuevo", "Luis pidió 1 × Calculadora.", NotificationTargets.Order, order, NotificationCategories.Orders),
            Ct);

        var saved = (await notifications.FindPageAsync(ana, null, null, 10, Ct)).Single();
        var job = Queued().Single();
        Assert.Equal((NotificationCategories.Orders, order), (saved.Category, saved.Target.Id));
        Assert.Equal(ana, job.UserId);
        Assert.Equal("Tienes un pedido nuevo", job.Message.Title);
        Assert.Equal(NotificationTypes.OrderCreated, job.Message.Data["type"]);
        Assert.Equal((NotificationTargets.Order, order, saved.Id), (job.Message.Data["targetType"], job.Message.Data["targetId"], job.Message.Data["notificationId"]));
        Assert.Equal(Now.UtcDateTime.AddDays(60), saved.ExpiresAt);
    }

    [Fact]
    public async Task AMessage_IsOnlyPushed_AndALongTextIsCut()
    {
        var ana = AuthIds.New();

        await Center().NotifyAsync(new Notice(ana, NotificationTypes.Message, "Luis", new string('a', 300), NotificationTargets.Conversation, AuthIds.New()), Ct);

        Assert.Empty(await notifications.FindPageAsync(ana, null, null, 10, Ct));
        var job = Queued().Single();
        Assert.Equal(NotificationCenter.MaxBody, job.Message.Body.Length);
        Assert.EndsWith("…", job.Message.Body, StringComparison.Ordinal);
        Assert.False(job.Message.Data.ContainsKey("notificationId"));
    }

    [Fact]
    public async Task TheSameDedupeKey_IsNotifiedOnlyOnce()
    {
        var ana = AuthIds.New();
        var notice = new Notice(ana, NotificationTypes.RequestExpiring, "Tu solicitud vence mañana", "Bata.", NotificationTargets.Wanted, AuthIds.New(), NotificationCategories.Requests, "request_expiring:w1");

        await Center().NotifyAsync(notice, Ct);
        await Center().NotifyAsync(notice, Ct);

        Assert.Single(await notifications.FindPageAsync(ana, null, null, 10, Ct));
        Assert.Single(Queued());
    }

    [Fact]
    public async Task TheList_IsMine_ByCategory_WithUnread_AndMarkReadOnlyTouchesMine()
    {
        var ana = AuthIds.New();
        var luis = AuthIds.New();
        var center = Center();
        await center.NotifyAsync(new Notice(ana, NotificationTypes.OrderCreated, "Pedido", "1", NotificationTargets.Order, AuthIds.New(), NotificationCategories.Orders), Ct);
        await center.NotifyAsync(new Notice(ana, NotificationTypes.OfferReceived, "Oferta", "2", NotificationTargets.Wanted, AuthIds.New(), NotificationCategories.Requests), Ct);
        await center.NotifyAsync(new Notice(luis, NotificationTypes.OfferReceived, "Oferta", "3", NotificationTargets.Wanted, AuthIds.New(), NotificationCategories.Requests), Ct);
        var service = Service();
        var luisNotice = (await service.ListAsync(luis, null, null, null, Ct)).Value!.Items.Single();

        var all = (await service.ListAsync(ana, null, null, null, Ct)).Value!;
        var requests = (await service.ListAsync(ana, NotificationCategories.Requests, null, null, Ct)).Value!;
        var firstPage = (await service.ListAsync(ana, null, null, 1, Ct)).Value!;
        var badCategory = await service.ListAsync(ana, "spam", null, null, Ct);
        var badCursor = await service.ListAsync(ana, null, "no-es-un-id", null, Ct);
        await service.MarkReadAsync(ana, luisNotice.Id, Ct);
        await service.MarkReadAsync(ana, all.Items[0].Id, Ct);
        var afterOne = (await service.ListAsync(ana, null, null, null, Ct)).Value!;
        await service.MarkReadAsync(ana, null, Ct);

        Assert.Equal(["Oferta", "Pedido"], all.Items.Select(n => n.Title));
        Assert.Equal(2, all.Unread);
        Assert.Equal(["Oferta"], requests.Items.Select(n => n.Title));
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal("notifications.invalid_category", badCategory.Error?.Code);
        Assert.Equal("notifications.invalid_cursor", badCursor.Error?.Code);
        Assert.Equal(1, afterOne.Unread);
        Assert.True(afterOne.Items[0].Read);
        Assert.Equal(0, (await service.ListAsync(ana, null, null, null, Ct)).Value!.Unread);
        Assert.Equal(1, (await service.ListAsync(luis, null, null, null, Ct)).Value!.Unread);
    }

    [Theory]
    [InlineData("")]
    [InlineData("con espacios")]
    [InlineData(null)]
    public async Task ADeviceToken_MustBeValid(string? token)
    {
        var ana = await MemberAsync();

        var result = await Service().RegisterDeviceAsync(ana, new DeviceRequest(token), Ct);

        Assert.Equal("notifications.invalid_token", result.Error?.Code);
    }

    [Fact]
    public async Task Devices_AreRegisteredByActiveMembers_AndRemovedOnlyByTheirOwner()
    {
        var ana = await MemberAsync();
        var luis = await MemberAsync();
        var suspended = await MemberAsync(UserStatuses.Suspended);
        var service = Service();

        var registered = await service.RegisterDeviceAsync(ana, new DeviceRequest(" fcm-ana "), Ct);
        var blocked = await service.RegisterDeviceAsync(suspended, new DeviceRequest("fcm-x"), Ct);
        await service.RemoveDeviceAsync(luis, new DeviceRequest("fcm-ana"), Ct);
        var stillThere = await tokens.FindByUserAsync(ana, Now.UtcDateTime, Ct);
        await service.RemoveDeviceAsync(ana, new DeviceRequest("fcm-ana"), Ct);

        Assert.True(registered.Value);
        Assert.Equal("auth.forbidden", blocked.Error?.Code);
        Assert.Equal(["fcm-ana"], stillThere);
        Assert.Empty(await tokens.FindByUserAsync(ana, Now.UtcDateTime, Ct));
    }

    [Fact]
    public async Task Delivery_GoesToEveryDevice_AndDropsTheUninstalledOnes()
    {
        var ana = AuthIds.New();
        await tokens.UpsertAsync(ana, "fcm-phone", Now.UtcDateTime, TimeSpan.FromDays(60), Ct);
        await tokens.UpsertAsync(ana, "fcm-old", Now.UtcDateTime, TimeSpan.FromDays(60), Ct);
        var sender = new RecordingPushSender();
        sender.Invalid["fcm-old"] = true;
        var job = new PushJob(ana, new PushMessage("Luis", "Hola", new Dictionary<string, string>()));

        var sent = await PushWorker.DeliverAsync(sender, tokens, job, Now.UtcDateTime, Ct);

        Assert.Equal(1, sent);
        Assert.Equal("fcm-phone", sender.Sent.Single().Token);
        Assert.Equal(["fcm-phone"], await tokens.FindByUserAsync(ana, Now.UtcDateTime, Ct));
    }

    [Fact]
    public void TheServiceAccount_IsReadFromItsJson_AndNeverPrintsTheKey()
    {
        using var rsa = RSA.Create(2048);
        var json = JsonSerializer.Serialize(new { type = "service_account", project_id = "musubi-test", client_email = "push@musubi-test.iam.gserviceaccount.com", private_key = rsa.ExportPkcs8PrivateKeyPem() });

        var sa = ServiceAccount.Parse(json);

        Assert.Equal(("musubi-test", "push@musubi-test.iam.gserviceaccount.com"), (sa!.ProjectId, sa.ClientEmail));
        Assert.DoesNotContain("PRIVATE KEY", sa.ToString(), StringComparison.Ordinal);
        Assert.Null(ServiceAccount.Parse(""));
        Assert.Null(ServiceAccount.Parse("{\"project_id\":\"x\"}"));
        Assert.Null(ServiceAccount.Parse("no es json"));
    }

    [Fact]
    public void TheJwt_IsRs256_ForFcm_AndVerifiesWithThePublicKey()
    {
        using var rsa = RSA.Create(2048);
        var sa = new ServiceAccount("musubi-test", "push@musubi-test.iam.gserviceaccount.com", rsa.ExportPkcs8PrivateKeyPem());

        var jwt = FcmPushSender.SignedAssertion(sa, Now);

        var parts = jwt.Split('.');
        static byte[] Decode(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
        using var header = JsonDocument.Parse(Decode(parts[0]));
        using var claims = JsonDocument.Parse(Decode(parts[1]));
        Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal("push@musubi-test.iam.gserviceaccount.com", claims.RootElement.GetProperty("iss").GetString());
        Assert.Equal("https://www.googleapis.com/auth/firebase.messaging", claims.RootElement.GetProperty("scope").GetString());
        Assert.Equal("https://oauth2.googleapis.com/token", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(3600, claims.RootElement.GetProperty("exp").GetInt64() - claims.RootElement.GetProperty("iat").GetInt64());
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task Fcm_ExchangesTheJwtOnce_SendsToTheProject_AndReadsUnregisteredTokens()
    {
        using var rsa = RSA.Create(2048);
        var calls = new List<(string Uri, string? Auth, string Body)>();
        var sender = Fcm(rsa, request =>
        {
            var uri = request.RequestUri!.ToString();
            if (uri.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal))
            {
                return Json("{\"access_token\":\"acceso-de-prueba\",\"expires_in\":3599}");
            }

            return calls.Count > 3
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":{\"status\":\"NOT_FOUND\",\"details\":[{\"errorCode\":\"UNREGISTERED\"}]}}") }
                : Json("{\"name\":\"projects/musubi-test/messages/1\"}");
        }, calls);
        var message = new PushMessage("Luis", "Hola", new Dictionary<string, string> { ["type"] = "message" });

        var first = await sender.SendAsync("fcm-1", message, Ct);
        var second = await sender.SendAsync("fcm-2", message, Ct);
        var gone = await sender.SendAsync("fcm-3", message, Ct);

        Assert.Equal((PushResult.Sent, PushResult.Sent, PushResult.InvalidToken), (first, second, gone));
        Assert.Single(calls, c => c.Uri.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal));
        var send = calls.First(c => c.Uri.Contains("messages:send", StringComparison.Ordinal));
        Assert.Equal("https://fcm.googleapis.com/v1/projects/musubi-test/messages:send", send.Uri);
        Assert.Equal("Bearer acceso-de-prueba", send.Auth);
        using var body = JsonDocument.Parse(send.Body);
        var m = body.RootElement.GetProperty("message");
        Assert.Equal("fcm-1", m.GetProperty("fid").GetString());
        Assert.False(m.TryGetProperty("token", out _));
        Assert.Equal("Luis", m.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal("message", m.GetProperty("data").GetProperty("type").GetString());
        Assert.Equal("musubi", m.GetProperty("android").GetProperty("notification").GetProperty("channel_id").GetString());
    }

    [Fact]
    public async Task Fcm_WithoutAKey_IsOff_AndAFailureIsNotFatal()
    {
        var off = Fcm(null, _ => Json("{}"), []);
        using var rsa = RSA.Create(2048);
        var failing = Fcm(rsa, _ => throw new HttpRequestException("sin red"), []);
        var message = new PushMessage("Luis", "Hola", new Dictionary<string, string>());

        Assert.False(off.Enabled);
        Assert.Equal(PushResult.Failed, await off.SendAsync("fcm-1", message, Ct));
        Assert.Equal(PushResult.Failed, await failing.SendAsync("fcm-1", message, Ct));
    }

    [Fact]
    public async Task Reminders_ExpiringAndExpired_AreNotifiedOnce_AndNotForClosedOnes()
    {
        var requests = new InMemoryWantedRepository();
        var ana = AuthIds.New();
        async Task<string> Wanted(string title, TimeSpan expiresIn, string status = WantedStatuses.Active)
        {
            var id = AuthIds.New();
            await requests.InsertAsync(
                new WantedRequestDocument
                {
                    Id = id,
                    CommunityId = InMemoryCommunityRepository.UtttId,
                    RequesterId = ana,
                    RequesterName = "Ana S.",
                    Title = title,
                    CategorySlug = "libros",
                    CategoryName = "Libros",
                    BudgetCents = 30_000,
                    ExpiresAt = Now.UtcDateTime + expiresIn,
                    Status = status,
                    CreatedAt = Now.UtcDateTime.AddDays(-5),
                },
                Ct).ConfigureAwait(false);
            return id;
        }

        var soon = await Wanted("Física Universitaria", TimeSpan.FromHours(20));
        var gone = await Wanted("Bata de laboratorio", TimeSpan.FromHours(-3));
        await Wanted("Calculadora", TimeSpan.FromDays(5));
        await Wanted("Cerrada", TimeSpan.FromHours(5), WantedStatuses.Closed);
        var center = Center();
        var job = new WantedReminderJob(new WantedReminders(requests), center);

        await job.RunAsync(Now.UtcDateTime, Ct);
        await job.RunAsync(Now.UtcDateTime.AddHours(1), Ct);

        var saved = await notifications.FindPageAsync(ana, NotificationCategories.Requests, null, 10, Ct);
        Assert.Equal(2, saved.Count);
        Assert.Contains(saved, n => n.Type == NotificationTypes.RequestExpiring && n.Target.Id == soon && n.Title == "Tu solicitud vence mañana");
        Assert.Contains(saved, n => n.Type == NotificationTypes.RequestExpired && n.Target.Id == gone && n.Title == "Tu solicitud venció");
        Assert.Equal(2, Queued().Count);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private FcmPushSender Fcm(RSA? rsa, Func<HttpRequestMessage, HttpResponseMessage> respond, List<(string Uri, string? Auth, string Body)> calls)
    {
        var json = rsa is null
            ? string.Empty
            : JsonSerializer.Serialize(new { project_id = "musubi-test", client_email = "push@musubi-test.iam.gserviceaccount.com", private_key = rsa.ExportPkcs8PrivateKeyPem() });
        var handler = new FakeHandler(respond, calls);
        owned.Add(handler);
        var client = new HttpClient(handler, disposeHandler: false);
        owned.Add(client);
        var sender = new FcmPushSender(client, Options.Create(new FirebaseOptions { ServiceAccount = json }), time, NullLogger<FcmPushSender>.Instance);
        owned.Add(sender);
        return sender;
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, List<(string Uri, string? Auth, string Body)> calls) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            calls.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return respond(request);
        }
    }
}
