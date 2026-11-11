using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-23 / RNF-03 / OWASP API1, API2 y API4: los pedidos exigen sesion; solo sus partes los ven, solo quien compra
/// los crea y paga, a cualquier otra persona se le responde 404 sin cambios, una cuenta suspendida no actua y los
/// cuerpos tienen su tope.
/// </summary>
public sealed class OrdersAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string AnyId = "6716a0000000000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Routes() => new()
    {
        { "GET", "/api/v1/orders/meeting-points" },
        { "POST", "/api/v1/orders" },
        { "GET", "/api/v1/orders?conversationId=" + AnyId },
        { "GET", "/api/v1/orders/" + AnyId },
        { "PUT", "/api/v1/orders/" + AnyId + "/payment" },
        { "POST", "/api/v1/orders/" + AnyId + "/accept" },
        { "POST", "/api/v1/orders/" + AnyId + "/cancel" },
        { "POST", "/api/v1/orders/" + AnyId + "/handover" },
    };

    private IOrderRepository Orders => production.Services.GetRequiredService<IOrderRepository>();

    private static HttpRequestMessage Request(string method, string path, object? body = null) =>
        new(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = method is "POST" or "PUT" ? JsonContent.Create(body ?? new { method = "cash" }) : null,
        };

    private async Task<string> MemberAsync(string status = UserStatuses.Active)
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
                Status = status,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private async Task<string> ConversationAsync(string buyer, string seller)
    {
        var (conversation, _) = await production.Services.GetRequiredService<IConversationRepository>().InsertOrGetAsync(
            new ConversationDocument
            {
                Id = AuthIds.New(),
                CommunityId = InMemoryCommunityRepository.UtttId,
                BuyerId = buyer,
                SellerId = seller,
                Participants = [buyer, seller],
                Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = AuthIds.New(), Title = "Calculadora" },
                LastMessageAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false);
        return conversation.Id;
    }

    private async Task<OrderDocument> OrderAsync(string buyer, string seller)
    {
        var order = new OrderDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            Number = 1001,
            BuyerId = buyer,
            SellerId = seller,
            BuyerName = "Ana",
            SellerName = "Diego",
            ConversationId = await ConversationAsync(buyer, seller).ConfigureAwait(false),
            Item = new OrderItem { SourceId = AuthIds.New(), Title = "Calculadora", PriceCents = 50_000, Quantity = 1, TotalCents = 50_000 },
            Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", At = DateTime.UtcNow.AddDays(1) },
            Status = OrderStatuses.Pending,
            History = [new OrderTransition { Status = OrderStatuses.Pending, At = DateTime.UtcNow }],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        Assert.True(await Orders.TryInsertAsync(order, Ct).ConfigureAwait(false));
        return order;
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
    public async Task SomeoneElse_Gets404_OnTheOrderAndItsPayment_AndNothingChanges()
    {
        var order = await OrderAsync(await MemberAsync(), await MemberAsync());
        using var outsider = ClientFor(await MemberAsync());
        using var pay = Request("PUT", $"/api/v1/orders/{order.Id}/payment");

        var read = await outsider.GetAsync(new Uri($"/api/v1/orders/{order.Id}", UriKind.Relative), Ct);
        var paid = await outsider.SendAsync(pay, Ct);
        var inChat = await outsider.GetAsync(new Uri($"/api/v1/orders?conversationId={order.ConversationId}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, paid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inChat.StatusCode);
        Assert.Contains("orders.not_found", await paid.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Null((await Orders.FindByIdAsync(order.Id, Ct))!.Payment);
    }

    [Fact]
    public async Task SomeoneElse_CannotCreateAnOrderOnAConversationThatIsNotTheirs()
    {
        var conversation = await ConversationAsync(await MemberAsync(), await MemberAsync());
        using var outsider = ClientFor(await MemberAsync());
        using var create = Request(
            "POST",
            "/api/v1/orders",
            new { conversationId = conversation, quantity = 1, meetingPoint = "biblioteca", deliveryAt = DateTime.UtcNow.AddDays(1) });

        var response = await outsider.SendAsync(create, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("orders.conversation_not_found", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Empty(await Orders.FindByConversationAsync(conversation, 10, Ct));
    }

    [Fact]
    public async Task TheSeller_SeesTheOrder_ButCannotChooseThePayment()
    {
        var seller = await MemberAsync();
        var order = await OrderAsync(await MemberAsync(), seller);
        using var client = ClientFor(seller);
        using var pay = Request("PUT", $"/api/v1/orders/{order.Id}/payment");

        var read = await client.GetAsync(new Uri($"/api/v1/orders/{order.Id}", UriKind.Relative), Ct);
        var paid = await client.SendAsync(pay, Ct);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, paid.StatusCode);
        Assert.Contains("orders.only_buyer", await paid.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Null((await Orders.FindByIdAsync(order.Id, Ct))!.Payment);
    }

    [Theory]
    [InlineData("GET", "/api/v1/orders/no-es-un-id", HttpStatusCode.NotFound)]
    [InlineData("PUT", "/api/v1/orders/no-es-un-id/payment", HttpStatusCode.NotFound)]
    [InlineData("POST", "/api/v1/orders/no-es-un-id/accept", HttpStatusCode.NotFound)]
    [InlineData("POST", "/api/v1/orders/no-es-un-id/handover", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/v1/orders?conversationId=no-es-un-id", HttpStatusCode.BadRequest)]
    [InlineData("GET", "/api/v1/orders", HttpStatusCode.BadRequest)]
    public async Task AnIdThatIsNotAnObjectId_IsRejected(string method, string path, HttpStatusCode status)
    {
        using var member = ClientFor(await MemberAsync());
        using var request = Request(method, path);

        var response = await member.SendAsync(request, Ct);

        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task ASuspendedAccount_IsForbidden_EvenOnItsOwnOrder()
    {
        var buyer = await MemberAsync(UserStatuses.Suspended);
        var order = await OrderAsync(buyer, await MemberAsync());
        using var client = ClientFor(buyer);
        using var pay = Request("PUT", $"/api/v1/orders/{order.Id}/payment");
        using var create = Request(
            "POST",
            "/api/v1/orders",
            new { conversationId = order.ConversationId, quantity = 1, meetingPoint = "biblioteca", deliveryAt = DateTime.UtcNow.AddDays(1) });

        var paid = await client.SendAsync(pay, Ct);
        var created = await client.SendAsync(create, Ct);
        var points = await client.GetAsync(new Uri("/api/v1/orders/meeting-points", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, paid.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, points.StatusCode);
        Assert.Null((await Orders.FindByIdAsync(order.Id, Ct))!.Payment);
    }

    [Theory]
    [InlineData(nameof(OrdersController.Create))]
    [InlineData(nameof(OrdersController.SetPayment))]
    [InlineData(nameof(OrdersController.Accept))]
    [InlineData(nameof(OrdersController.Cancel))]
    [InlineData(nameof(OrdersController.Handover))]
    public void TheBodies_HaveTheirOwnSizeLimit(string action)
    {
        // Kestrel aplica el tope en la nube; TestServer no lo aplica, asi que se verifica el atributo: sin el, se
        // aceptaria hasta el tope global (OWASP API4).
        var method = typeof(OrdersController).GetMethod(action)!;
        var limit = method.GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: false);

        Assert.Single(limit);
        Assert.Equal(16 * 1024, OrdersController.MaxBodyBytes);
    }

    [Fact]
    public async Task SomeoneElse_CannotAcceptCancelOrConfirmAnOrder_AndNothingChanges()
    {
        var order = await OrderAsync(await MemberAsync(), await MemberAsync());
        using var outsider = ClientFor(await MemberAsync());
        using var accept = Request("POST", $"/api/v1/orders/{order.Id}/accept");
        using var cancel = Request("POST", $"/api/v1/orders/{order.Id}/cancel", new { reason = "other" });
        using var handover = Request("POST", $"/api/v1/orders/{order.Id}/handover");

        var accepted = await outsider.SendAsync(accept, Ct);
        var cancelled = await outsider.SendAsync(cancel, Ct);
        var confirmed = await outsider.SendAsync(handover, Ct);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (accepted.StatusCode, cancelled.StatusCode, confirmed.StatusCode));
        Assert.Contains("orders.not_found", await cancelled.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var read = (await Orders.FindByIdAsync(order.Id, Ct))!;
        Assert.Equal((OrderStatuses.Pending, (OrderCancellation?)null), (read.Status, read.Cancellation));
    }

    [Fact]
    public async Task TheBuyer_CannotAcceptItsOwnOrder()
    {
        var buyer = await MemberAsync();
        var order = await OrderAsync(buyer, await MemberAsync());
        using var client = ClientFor(buyer);
        using var accept = Request("POST", $"/api/v1/orders/{order.Id}/accept");

        var response = await client.SendAsync(accept, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("orders.only_seller", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(OrderStatuses.Pending, (await Orders.FindByIdAsync(order.Id, Ct))!.Status);
    }
}
