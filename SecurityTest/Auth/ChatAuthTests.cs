using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-20 / OWASP API1 y API2: el chat exige sesion, solo los participantes leen o escriben (RN-22) y el
/// token en la query solo vale para el hub (un WebSocket no lleva encabezados), nunca para la API REST.
/// </summary>
public sealed class ChatAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string AnyId = "6716a0000000000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> MemberOnly() => new()
    {
        { "GET", "/api/v1/conversations" },
        { "POST", "/api/v1/conversations" },
        { "POST", $"/api/v1/conversations/{AnyId}/read" },
        { "GET", $"/api/v1/conversations/{AnyId}" },
        { "GET", $"/api/v1/conversations/{AnyId}/messages" },
        { "POST", $"/api/v1/conversations/{AnyId}/messages" },
        { "POST", $"/api/v1/conversations/{AnyId}/photos" },
        { "GET", $"/api/v1/conversations/{AnyId}/photos/0123456789abcdef0123456789abcdef.webp" },
        { "GET", "/hubs/chat" },
    };

    [Theory]
    [MemberData(nameof(MemberOnly))]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(new { productId = AnyId, text = "Hola" }),
        };

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenInTheQuery_OpensTheHub_ButNotTheRestApi()
    {
        using var client = production.CreateClient();
        var token = Uri.EscapeDataString(TokenFor(AuthIds.New()));

        var rest = await client.GetAsync(new Uri($"/api/v1/conversations/{AnyId}?access_token={token}", UriKind.Relative), Ct);
        var hub = await client.GetAsync(new Uri($"/hubs/chat?access_token={token}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, rest.StatusCode);
        // Pasa la autenticacion; el hub solo acepta WebSockets y un GET normal no lo es.
        Assert.NotEqual(HttpStatusCode.Unauthorized, hub.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, hub.StatusCode);
    }

    [Fact]
    public async Task AForgedTokenInTheQuery_DoesNotOpenTheHub()
    {
        using var client = production.CreateClient();
        var forged = TokenFor(AuthIds.New())[..^4] + "AAAA";

        var hub = await client.GetAsync(new Uri($"/hubs/chat?access_token={Uri.EscapeDataString(forged)}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, hub.StatusCode);
    }

    [Fact]
    public async Task AConversationOfOthers_IsNotFound_ForReadingOrWriting()
    {
        var buyer = AuthIds.New();
        var seller = AuthIds.New();
        var conversation = AuthIds.New();
        await production.Services.GetRequiredService<IConversationRepository>().InsertOrGetAsync(
            new ConversationDocument
            {
                Id = conversation,
                CommunityId = InMemoryCommunityRepository.UtttId,
                BuyerId = buyer,
                SellerId = seller,
                Participants = [buyer, seller],
                Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = AuthIds.New(), Title = "Calculadora" },
                LastMessageAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct);
        using var outsider = production.CreateClient();
        outsider.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(AuthIds.New()));

        var get = await outsider.GetAsync(new Uri($"/api/v1/conversations/{conversation}", UriKind.Relative), Ct);
        var read = await outsider.GetAsync(new Uri($"/api/v1/conversations/{conversation}/messages", UriKind.Relative), Ct);
        var write = await outsider.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversation}/messages", UriKind.Relative), new { text = "Hola" }, Ct);
        var markRead = await outsider.PostAsync(new Uri($"/api/v1/conversations/{conversation}/read", UriKind.Relative), null, Ct);
        var inbox = await outsider.GetAsync(new Uri("/api/v1/conversations", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, markRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
        Assert.DoesNotContain(conversation, await inbox.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
    }

    [Fact]
    public async Task APhotoOfOthers_IsNotFound_ForSendingOrDownloading_AndANameIsNeverAPath()
    {
        // HU-65 (OWASP API1): las fotos del chat solo son de sus participantes; el nombre no puede salir de chat/.
        // Production guarda en S3: aqui, en memoria.
        var media = new InMemoryMediaStorage();
        using var app = production.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IMediaStorage>(media)));
        string Token(string userId) =>
            app.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, InMemoryCommunityRepository.UtttId, ["student"]).Token;
        var buyer = AuthIds.New();
        var seller = AuthIds.New();
        var conversation = AuthIds.New();
        await app.Services.GetRequiredService<IConversationRepository>().InsertOrGetAsync(
            new ConversationDocument
            {
                Id = conversation,
                CommunityId = InMemoryCommunityRepository.UtttId,
                BuyerId = buyer,
                SellerId = seller,
                Participants = [buyer, seller],
                Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = AuthIds.New(), Title = "Calculadora" },
                LastMessageAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct);
        var name = new string('a', 32);
        await media.PutAsync(ChatService.PhotoKey(conversation, name), [1, 2, 3], "image/webp", Ct);
        using var outsider = app.CreateClient();
        outsider.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(AuthIds.New()));
        using var member = app.CreateClient();
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(buyer));
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent([1, 2, 3]);
        form.Add(file, "photo", "foto.png");

        var download = await outsider.GetAsync(new Uri($"/api/v1/conversations/{conversation}/photos/{name}.webp", UriKind.Relative), Ct);
        var send = await outsider.PostAsync(new Uri($"/api/v1/conversations/{conversation}/photos", UriKind.Relative), form, Ct);
        var traversal = await member.GetAsync(new Uri($"/api/v1/conversations/{conversation}/photos/..%2F..%2Fmedia%2Fx.webp", UriKind.Relative), Ct);
        var mine = await member.GetAsync(new Uri($"/api/v1/conversations/{conversation}/photos/{name}.webp", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Contains("chat.photo_not_found", await download.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, send.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, traversal.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        // Privada: ningun intermediario (CloudFront ni un proxy) la guarda.
        Assert.True(mine.Headers.CacheControl!.Private);
        Assert.False(mine.Headers.CacheControl.Public);
    }

    [Fact]
    public void APhoto_HasItsOwnSizeLimit_AndTheUploadRateLimit()
    {
        // Kestrel aplica el tope en la nube; TestServer no, asi que se verifican los atributos (OWASP API4).
        var method = typeof(ConversationsController).GetMethod(nameof(ConversationsController.SendPhoto))!;

        Assert.Single(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute), inherit: false));
        Assert.Single(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute), inherit: false));
    }

    [Fact]
    public void EveryHub_RequiresASession()
    {
        var hubs = typeof(Program).Assembly.GetTypes().Where(t => typeof(Hub).IsAssignableFrom(t) && !t.IsAbstract).ToList();

        Assert.NotEmpty(hubs);
        Assert.All(hubs, hub => Assert.NotNull(hub.GetCustomAttribute<AuthorizeAttribute>()));
        Assert.All(hubs, hub => Assert.Null(hub.GetCustomAttribute<AllowAnonymousAttribute>()));
    }

    private string TokenFor(string userId) =>
        production.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, InMemoryCommunityRepository.UtttId, ["student"]).Token;
}
