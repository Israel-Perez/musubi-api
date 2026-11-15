using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-55 (RNF-03, RNF-05, OWASP API1/API3): la cuenta para recibir pagos y los datos de una transferencia exigen sesion
/// y solo los ven las partes del pedido; en Production sin <c>Payments:EncryptionKey</c> no se guarda ninguna CLABE
/// (RNF-15: se paga en efectivo).
/// </summary>
public sealed class PaymentsAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync()
    {
        var id = AuthIds.New();
        Assert.True(await production.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"diego.{id}@uttt.edu.mx",
                EmailNormalized = $"diego.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Diego Hernández",
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

    private async Task<string> TransferOrderAsync(string buyer, string seller)
    {
        var now = DateTime.UtcNow;
        var order = new OrderDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            Number = 1001,
            BuyerId = buyer,
            SellerId = seller,
            BuyerName = "Ana Sofía Ramírez",
            SellerName = "Diego Hernández",
            ConversationId = AuthIds.New(),
            Item = new OrderItem { Source = OrderSources.Product, SourceId = AuthIds.New(), Title = "Calculadora", PriceCents = 50_000, Quantity = 1, TotalCents = 50_000 },
            Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", At = now.AddDays(1) },
            Payment = new OrderPayment { Method = PaymentMethods.Transfer, ChosenAt = now },
            Status = OrderStatuses.Pending,
            History = [new OrderTransition { Status = OrderStatuses.Pending, At = now }],
            CreatedAt = now,
            UpdatedAt = now,
        };
        Assert.True(await production.Services.GetRequiredService<IOrderRepository>().TryInsertAsync(order, Ct).ConfigureAwait(false));
        return order.Id;
    }

    [Theory]
    [InlineData("GET", "/api/v1/payouts/banks")]
    [InlineData("GET", "/api/v1/payouts/account")]
    [InlineData("PUT", "/api/v1/payouts/account")]
    [InlineData("DELETE", "/api/v1/payouts/account")]
    [InlineData("GET", "/api/v1/orders/6716a0000000000000000001/transfer")]
    [InlineData("POST", "/api/v1/orders/6716a0000000000000000001/transfer")]
    [InlineData("POST", "/api/v1/orders/6716a0000000000000000001/transfer/not-received")]
    [InlineData("POST", "/api/v1/orders/6716a0000000000000000001/delivery-code")]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(new { }) };

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithoutTheEncryptionKey_NoClabeIsStored_AndTransferIsOff()
    {
        var diego = await MemberAsync();
        using var client = ClientFor(diego);

        var saved = await client.PutAsJsonAsync(new Uri("/api/v1/payouts/account", UriKind.Relative), new { holderName = "Diego Hernández", clabe = "012180015488214829" }, Ct);
        var status = await JsonAsync(await client.GetAsync(new Uri("/api/v1/payouts/account", UriKind.Relative), Ct));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, saved.StatusCode);
        Assert.Equal("payouts.unavailable", (await JsonAsync(saved)).GetProperty("code").GetString());
        Assert.False(status.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("account").ValueKind);
    }

    [Fact]
    public async Task SomeoneElse_CannotSeeOrTouchTheTransfer()
    {
        var ana = await MemberAsync();
        var diego = await MemberAsync();
        var order = await TransferOrderAsync(ana, diego);
        using var luis = ClientFor(await MemberAsync());
        using var seller = ClientFor(diego);

        var seen = await luis.GetAsync(new Uri($"/api/v1/orders/{order}/transfer", UriKind.Relative), Ct);
        var reported = await luis.PostAsJsonAsync(new Uri($"/api/v1/orders/{order}/transfer", UriKind.Relative), new { reference = "4829173" }, Ct);
        var notReceived = await luis.PostAsync(new Uri($"/api/v1/orders/{order}/transfer/not-received", UriKind.Relative), null, Ct);
        var code = await luis.PostAsJsonAsync(new Uri($"/api/v1/orders/{order}/delivery-code", UriKind.Relative), new { code = "1234" }, Ct);
        // Quien vende no puede avisar "ya transferi" por quien compra.
        var bySeller = await seller.PostAsJsonAsync(new Uri($"/api/v1/orders/{order}/transfer", UriKind.Relative), new { reference = "4829173" }, Ct);

        Assert.All([seen, reported, notReceived, code], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal("orders.only_buyer", (await JsonAsync(bySeller)).GetProperty("code").GetString());
        Assert.Null((await production.Services.GetRequiredService<IOrderRepository>().FindByIdAsync(order, Ct))!.Payment!.ReportedAt);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false)).RootElement;
}
