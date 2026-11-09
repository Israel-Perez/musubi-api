using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using IntegrationTest.Auth;

namespace IntegrationTest.PurchaseRequests;

/// <summary>
/// HU-16: "Yo tengo este producto" con oferta directa (maqueta <c>Yo-tengo-oferta</c>): se crea (201), cuenta en la
/// solicitud, se edita (200) y quien busca no puede ofrecer en la suya.
/// </summary>
public sealed class OfferFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> MemberAsync()
    {
        // El JWT se emite con el reloj de la fabrica, pero JwtBearer lo valida con la hora real.
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var email = await flow.RegisterActiveAsync().ConfigureAwait(false);
        var login = await AuthFlow.JsonAsync(await flow.LoginAsync(email).ConfigureAwait(false)).ConfigureAwait(false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<string> PublishAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Bata de laboratorio talla M", category = "laboratorio", budgetCents = 30_000, quantity = 1 },
            Ct).ConfigureAwait(false);
        return (await AuthFlow.JsonAsync(response).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static Uri Mine(string requestId) => new($"/api/v1/wanted/{requestId}/offers/mine", UriKind.Relative);

    [Fact]
    public async Task ADirectOffer_IsCreated_Counted_AndEditable()
    {
        using var fernanda = await MemberAsync();
        using var valeria = await MemberAsync();
        var requestId = await PublishAsync(fernanda);

        var none = await valeria.GetAsync(Mine(requestId), Ct);
        var sent = await valeria.PutAsJsonAsync(
            Mine(requestId),
            new { description = "Bata blanca de algodón, talla M", priceCents = 32_000, condition = "used", message = "Te la llevo." },
            Ct);
        var created = await AuthFlow.JsonAsync(sent);
        var edited = await valeria.PutAsJsonAsync(
            Mine(requestId),
            new { description = "Bata blanca de algodón, talla M", priceCents = 28_000, condition = "used" },
            Ct);
        var mine = await AuthFlow.JsonAsync(await valeria.GetAsync(Mine(requestId), Ct));
        var request = await AuthFlow.JsonAsync(await fernanda.GetAsync(new Uri($"/api/v1/wanted/{requestId}", UriKind.Relative), Ct));

        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal("offers.not_found", (await AuthFlow.JsonAsync(none)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.True(created.GetProperty("overBudget").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(created.GetProperty("id").GetString(), mine.GetProperty("id").GetString());
        Assert.Equal(28_000, mine.GetProperty("priceCents").GetInt64());
        Assert.False(mine.GetProperty("overBudget").GetBoolean());
        Assert.Equal(1, request.GetProperty("offersCount").GetInt32());
    }

    [Fact]
    public async Task TheRequester_CannotOfferOnTheirOwn()
    {
        using var fernanda = await MemberAsync();
        var requestId = await PublishAsync(fernanda);

        var response = await fernanda.PutAsJsonAsync(
            Mine(requestId),
            new { description = "Bata blanca de algodón", priceCents = 28_000, condition = "used" },
            Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("offers.own_request", (await AuthFlow.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("", 28_000L, "used", "offers.invalid_description")]
    [InlineData("Bata blanca", null, "used", "offers.invalid_price")]
    [InlineData("Bata blanca", 28_000L, "", "offers.invalid_condition")]
    public async Task MissingFields_Are400_WithTheirCode(string description, long? price, string condition, string code)
    {
        using var fernanda = await MemberAsync();
        using var valeria = await MemberAsync();
        var requestId = await PublishAsync(fernanda);

        var response = await valeria.PutAsJsonAsync(Mine(requestId), new { description, priceCents = price, condition }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await AuthFlow.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnUnknownRequest_Is404()
    {
        using var valeria = await MemberAsync();

        var response = await valeria.PutAsJsonAsync(
            Mine("6716a00000000000000000ff"),
            new { description = "Bata blanca", priceCents = 28_000, condition = "used" },
            Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("wanted.not_found", (await AuthFlow.JsonAsync(response)).GetProperty("code").GetString());
    }

    private static Uri Offers(string requestId, string? action = null) =>
        new($"/api/v1/wanted/{requestId}/offers{action}", UriKind.Relative);

    [Fact]
    public async Task TheRequester_SeesTheOffers_AcceptsOne_AndBothCanChat()
    {
        using var fernanda = await MemberAsync();
        using var valeria = await MemberAsync();
        using var diego = await MemberAsync();
        var requestId = await PublishAsync(fernanda);
        var fromValeria = (await AuthFlow.JsonAsync(await valeria.PutAsJsonAsync(
            Mine(requestId), new { description = "Bata blanca de algodón", priceCents = 28_000, condition = "used", message = "Te la llevo." }, Ct)))
            .GetProperty("id").GetString();
        var fromDiego = (await AuthFlow.JsonAsync(await diego.PutAsJsonAsync(
            Mine(requestId), new { description = "Bata talla M", priceCents = 25_000, condition = "new" }, Ct)))
            .GetProperty("id").GetString();

        var list = await AuthFlow.JsonAsync(await fernanda.GetAsync(Offers(requestId), Ct));
        var rejected = await fernanda.PostAsync(Offers(requestId, $"/{fromDiego}/reject"), null, Ct);
        var accepted = await fernanda.PostAsync(Offers(requestId, $"/{fromValeria}/accept"), null, Ct);
        var conversationId = (await AuthFlow.JsonAsync(accepted)).GetProperty("conversationId").GetString();
        var asBuyer = await AuthFlow.JsonAsync(await fernanda.GetAsync(new Uri($"/api/v1/conversations/{conversationId}", UriKind.Relative), Ct));
        var asSeller = await AuthFlow.JsonAsync(await valeria.GetAsync(new Uri($"/api/v1/conversations/{conversationId}", UriKind.Relative), Ct));
        var sent = await fernanda.PostAsJsonAsync(
            new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative), new { text = "¡Hola, Valeria! Acepto tu oferta." }, Ct);
        var after = await AuthFlow.JsonAsync(await fernanda.GetAsync(Offers(requestId), Ct));
        var diegoMine = await AuthFlow.JsonAsync(await diego.GetAsync(Mine(requestId), Ct));

        Assert.Equal(2, list.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(("buyer", "offer"), (asBuyer.GetProperty("role").GetString(), asBuyer.GetProperty("context").GetProperty("type").GetString()));
        Assert.Equal("seller", asSeller.GetProperty("role").GetString());
        Assert.True(asBuyer.GetProperty("context").GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        var only = Assert.Single(after.GetProperty("items").EnumerateArray());
        Assert.Equal(("accepted", fromValeria), (only.GetProperty("status").GetString(), only.GetProperty("id").GetString()));
        Assert.Equal("rejected", diegoMine.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SomeoneElse_CannotSeeOrDecideTheOffers()
    {
        using var fernanda = await MemberAsync();
        using var valeria = await MemberAsync();
        var requestId = await PublishAsync(fernanda);
        var offerId = (await AuthFlow.JsonAsync(await valeria.PutAsJsonAsync(
            Mine(requestId), new { description = "Bata blanca", priceCents = 28_000, condition = "used" }, Ct)))
            .GetProperty("id").GetString();

        var list = await valeria.GetAsync(Offers(requestId), Ct);
        var accept = await valeria.PostAsync(Offers(requestId, $"/{offerId}/accept"), null, Ct);
        var unknown = await fernanda.PostAsync(Offers(requestId, "/6716a00000000000000000ff/chat"), null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        Assert.Equal("wanted.not_found", (await AuthFlow.JsonAsync(list)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        Assert.Equal("offers.offer_not_found", (await AuthFlow.JsonAsync(unknown)).GetProperty("code").GetString());
    }
}
