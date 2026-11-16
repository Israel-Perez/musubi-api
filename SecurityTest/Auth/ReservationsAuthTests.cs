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
/// HU-52 (RNF-03, OWASP API1): los apartados exigen sesion; uno ajeno responde como si no existiera, su codigo no
/// sale a nadie mas que a quien aparto y solo quien vende lo verifica (5 intentos contra la fuerza bruta).
/// </summary>
public sealed class ReservationsAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
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
                Email = $"luis.{id}@uttt.edu.mx",
                EmailNormalized = $"luis.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Luis Martínez",
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

    private async Task<ReservationDocument> ReservationAsync(string buyer, string seller)
    {
        var now = DateTime.UtcNow;
        var reservation = new ReservationDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            Number = 1001,
            BuyerId = buyer,
            SellerId = seller,
            BuyerName = "Luis Martínez",
            SellerName = "Valeria Cruz",
            ConversationId = AuthIds.New(),
            ProductId = AuthIds.New(),
            Title = "Juego de escuadras",
            PriceCents = 18_000,
            PointSlug = "biblioteca",
            PointName = "Biblioteca",
            PickupAt = now.AddHours(3),
            Code = "482173",
            CreatedAt = now,
            ExpiresAt = now.AddHours(24),
            UpdatedAt = now,
        };
        Assert.True(await production.Services.GetRequiredService<IReservationRepository>().TryInsertAsync(reservation, Ct).ConfigureAwait(false));
        return reservation;
    }

    [Theory]
    [InlineData("POST", "/api/v1/reservations")]
    [InlineData("GET", "/api/v1/reservations/mine")]
    [InlineData("GET", "/api/v1/reservations/6716a0000000000000000001")]
    [InlineData("POST", "/api/v1/reservations/6716a0000000000000000001/cancel")]
    [InlineData("POST", "/api/v1/reservations/6716a0000000000000000001/verify")]
    [InlineData("POST", "/api/v1/reservations/6716a0000000000000000001/complete")]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(new { }) };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request, Ct)).StatusCode);
    }

    [Fact]
    public async Task SomeoneElse_CannotSeeVerifyOrCancelIt()
    {
        var reservation = await ReservationAsync(await MemberAsync(), await MemberAsync());
        using var stranger = ClientFor(await MemberAsync());
        var id = reservation.Id;

        var seen = await stranger.GetAsync(new Uri($"/api/v1/reservations/{id}", UriKind.Relative), Ct);
        var verified = await stranger.PostAsJsonAsync(new Uri($"/api/v1/reservations/{id}/verify", UriKind.Relative), new { code = "482173" }, Ct);
        var cancelled = await stranger.PostAsync(new Uri($"/api/v1/reservations/{id}/cancel", UriKind.Relative), null, Ct);
        var completed = await stranger.PostAsync(new Uri($"/api/v1/reservations/{id}/complete", UriKind.Relative), null, Ct);

        Assert.All([seen, verified, cancelled, completed], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        Assert.Equal(ReservationStatuses.Active, (await production.Services.GetRequiredService<IReservationRepository>().FindByIdAsync(id, Ct))!.Status);
    }

    [Fact]
    public async Task OnlyTheBuyerGetsTheCode_AndOnlyTheSellerVerifies()
    {
        var buyer = await MemberAsync();
        var seller = await MemberAsync();
        var reservation = await ReservationAsync(buyer, seller);
        using var forBuyer = ClientFor(buyer);
        using var forSeller = ClientFor(seller);

        var asBuyer = await JsonAsync(await forBuyer.GetAsync(new Uri($"/api/v1/reservations/{reservation.Id}", UriKind.Relative), Ct));
        var asSeller = await JsonAsync(await forSeller.GetAsync(new Uri($"/api/v1/reservations/{reservation.Id}", UriKind.Relative), Ct));
        var byBuyer = await forBuyer.PostAsJsonAsync(new Uri($"/api/v1/reservations/{reservation.Id}/verify", UriKind.Relative), new { code = "482173" }, Ct);

        Assert.Equal("482173", asBuyer.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, asSeller.GetProperty("code").ValueKind);
        Assert.Equal(JsonValueKind.Null, asSeller.GetProperty("qrPayload").ValueKind);
        Assert.Equal("reservations.only_seller", (await JsonAsync(byBuyer)).GetProperty("code").GetString());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false)).RootElement;
}
