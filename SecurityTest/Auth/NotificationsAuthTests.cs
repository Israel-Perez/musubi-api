using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Notifications.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-31 / RNF-03 / OWASP API1 y API4: los avisos exigen sesion; nadie lee ni marca los de otra persona ni le quita su
/// dispositivo; sin la llave de Firebase no sale nada y los cuerpos tienen su tope.
/// </summary>
public sealed class NotificationsAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string AnyId = "6716a0000000000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Routes() => new()
    {
        { "GET", "/api/v1/notifications" },
        { "POST", "/api/v1/notifications/read" },
        { "POST", "/api/v1/notifications/" + AnyId + "/read" },
        { "PUT", "/api/v1/notifications/devices" },
        { "POST", "/api/v1/notifications/devices/remove" },
    };

    private static HttpRequestMessage Request(string method, string path, object? body = null) =>
        new(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = method is "PUT" || path?.EndsWith("remove", StringComparison.Ordinal) == true ? JsonContent.Create(body ?? new { token = "fcm-x" }) : null,
        };

    private async Task<string> MemberAsync()
    {
        var id = AuthIds.New();
        Assert.True(await production.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"ana.{id}@uttt.edu.mx",
                EmailNormalized = $"ana.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Ana Sofía Ramírez",
                Status = UserStatuses.Active,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private HttpClient ClientFor(string userId)
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, InMemoryCommunityRepository.UtttId, ["student"]);
        var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = Request(method, path);

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SomeoneElse_CannotRead_Mark_OrRemoveTheDeviceOfAnother()
    {
        var ana = await MemberAsync();
        var luis = await MemberAsync();
        await production.Services.GetRequiredService<INotificationCenter>().NotifyAsync(
            new Notice(ana, NotificationTypes.OrderCreated, "Tienes un pedido nuevo", "Pedido.", NotificationTargets.Order, AnyId, NotificationCategories.Orders),
            Ct);
        var tokens = production.Services.GetRequiredService<IDeviceTokenRepository>();
        var token = $"fcm-ana-{Guid.NewGuid():N}";
        await tokens.UpsertAsync(ana, token, DateTime.UtcNow, TimeSpan.FromDays(60), Ct);
        var notifications = production.Services.GetRequiredService<INotificationRepository>();
        var anaNotice = (await notifications.FindPageAsync(ana, null, null, 1, Ct)).Single();
        using var outsider = ClientFor(luis);
        using var remove = Request("POST", "/api/v1/notifications/devices/remove", new { token });

        var list = await outsider.GetFromJsonAsync<NotificationPage>(new Uri("/api/v1/notifications", UriKind.Relative), Ct);
        await outsider.PostAsync(new Uri($"/api/v1/notifications/{anaNotice.Id}/read", UriKind.Relative), null, Ct);
        await outsider.PostAsync(new Uri("/api/v1/notifications/read", UriKind.Relative), null, Ct);
        await outsider.SendAsync(remove, Ct);

        Assert.Empty(list!.Items);
        Assert.Equal(1, await notifications.CountUnreadAsync(ana, Ct));
        Assert.Equal([token], await tokens.FindByUserAsync(ana, DateTime.UtcNow, Ct));
    }

    [Fact]
    public void InProduction_WithoutTheServiceAccount_PushIsOff()
    {
        // RNF-15: Production usa FCM; sin la llave (Parameter Store) no sale nada y el resto sigue.
        using var scope = production.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();

        Assert.IsType<FcmPushSender>(sender);
        Assert.False(sender.Enabled);
    }

    [Theory]
    [InlineData(nameof(NotificationsController.RegisterDevice))]
    [InlineData(nameof(NotificationsController.RemoveDevice))]
    public void TheBodies_HaveTheirOwnSizeLimit(string action)
    {
        var method = typeof(NotificationsController).GetMethod(action)!;

        Assert.Single(method.GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: false));
        Assert.Equal(4 * 1024, NotificationsController.MaxBodyBytes);
    }
}
