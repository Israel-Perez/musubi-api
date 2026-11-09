using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;

namespace IntegrationTest.PurchaseRequests;

/// <summary>
/// HU-13: "Publicar solicitud" (maqueta <c>Necesidad-1…3</c>) la deja activa, aparece en "Mis solicitudes" y la
/// ve otra persona del campus; lo que falta o no cumple responde con su codigo. HU-15: el tablero "Se busca" con
/// sesion y como visitante, con sus filtros.
/// </summary>
public sealed class WantedFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
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

    private static object Request(string title = "Calculadora Casio fx-991ES Plus", string category = "calculadoras", long? budgetCents = 60_000, int? quantity = 1) => new
    {
        title,
        category,
        description = "La necesito para Cálculo Integral y Física.",
        features = new[] { "Modelo fx-991ES Plus", "Con tapa" },
        budgetCents,
        quantity,
        neededBy = (string?)null,
    };

    [Fact]
    public async Task Publish_IsActive_InMyRequests_AndVisibleToTheCampus()
    {
        using var ana = await MemberAsync();
        using var diego = await MemberAsync();

        var published = await ana.PostAsJsonAsync(new Uri("/api/v1/wanted", UriKind.Relative), Request(), Ct);
        var body = await AuthFlow.JsonAsync(published);
        var id = body.GetProperty("id").GetString();
        var mine = await AuthFlow.JsonAsync(await ana.GetAsync(new Uri("/api/v1/wanted/mine", UriKind.Relative), Ct));
        var seenByDiego = await diego.GetAsync(new Uri($"/api/v1/wanted/{id}", UriKind.Relative), Ct);
        var diegoMine = await AuthFlow.JsonAsync(await diego.GetAsync(new Uri("/api/v1/wanted/mine", UriKind.Relative), Ct));

        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.Equal("Calculadoras", body.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal(60_000, body.GetProperty("budgetCents").GetInt64());
        Assert.Equal(id, Assert.Single(mine.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, seenByDiego.StatusCode);
        Assert.Empty(diegoMine.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("", "calculadoras", 60_000L, 1, "wanted.invalid_title")]
    [InlineData("Calculadora", "", 60_000L, 1, "wanted.invalid_category")]
    [InlineData("Calculadora", "calculadoras", null, 1, "wanted.invalid_budget")]
    [InlineData("Calculadora", "calculadoras", 60_000L, null, "wanted.invalid_quantity")]
    public async Task MissingRequiredFields_Are400_WithTheirCode(string title, string category, long? budget, int? quantity, string code)
    {
        using var ana = await MemberAsync();

        var response = await ana.PostAsJsonAsync(new Uri("/api/v1/wanted", UriKind.Relative), Request(title, category, budget, quantity), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await AuthFlow.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task APastDate_Is400()
    {
        using var ana = await MemberAsync();

        var response = await ana.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Calculadora", category = "calculadoras", budgetCents = 60_000, quantity = 1, neededBy = "2020-01-01" },
            Ct);

        Assert.Equal("wanted.invalid_date", (await AuthFlow.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnUnknownRequest_Is404()
    {
        using var ana = await MemberAsync();

        var response = await ana.GetAsync(new Uri("/api/v1/wanted/6716a0000000000000000099", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("wanted.not_found", (await AuthFlow.JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Board_ShowsItToTheCampus_WithTheFullName_ButNotToItsOwner()
    {
        using var ana = await MemberAsync();
        using var diego = await MemberAsync();
        var title = "Bata " + Guid.NewGuid().ToString("N")[..8];
        var id = (await AuthFlow.JsonAsync(await ana.PostAsJsonAsync(new Uri("/api/v1/wanted", UriKind.Relative), Request(title, "laboratorio"), Ct)))
            .GetProperty("id").GetString();

        var seen = await diego.GetAsync(new Uri("/api/v1/wanted?category=laboratorio&maxBudgetCents=60000&q=bata", UriKind.Relative), Ct);
        var page = await AuthFlow.JsonAsync(seen);
        var own = await AuthFlow.JsonAsync(await ana.GetAsync(new Uri("/api/v1/wanted?q=bata", UriKind.Relative), Ct));

        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
        var item = page.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id);
        Assert.Equal("Ana Sofía Prueba", item.GetProperty("requester").GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(item.GetProperty("requester").GetProperty("id").GetString()));
        // RN-06: la propia no sale en el tablero de quien la publico.
        Assert.DoesNotContain(own.GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task Board_AVisitorSeesIt_WithTheShortNameOnly()
    {
        using var ana = await MemberAsync();
        var title = "Escuadras " + Guid.NewGuid().ToString("N")[..8];
        var id = (await AuthFlow.JsonAsync(await ana.PostAsJsonAsync(new Uri("/api/v1/wanted", UriKind.Relative), Request(title, "dibujo"), Ct)))
            .GetProperty("id").GetString();
        using var visitor = factory.CreateClient();

        var page = await AuthFlow.JsonAsync(await visitor.GetAsync(new Uri("/api/v1/wanted?campus=uttt&category=dibujo&limit=50", UriKind.Relative), Ct));

        var requester = page.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("id").GetString() == id).GetProperty("requester");
        Assert.Equal(JsonValueKind.Null, requester.GetProperty("id").ValueKind);
        Assert.Equal("Ana P.", requester.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, requester.GetProperty("career").ValueKind);
    }

    [Theory]
    [InlineData("minBudgetCents=50000&maxBudgetCents=100", "wanted.invalid_budget_range")]
    [InlineData("neededWithinDays=0", "wanted.invalid_deadline")]
    [InlineData("q=%3F%3F", "wanted.invalid_query")]
    [InlineData("cursor=abc", "wanted.invalid_cursor")]
    [InlineData("campus=unam", "listings.unknown_campus")]
    public async Task Board_InvalidFilters_AreAnswered_WithTheirCode(string query, string code)
    {
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync(new Uri("/api/v1/wanted?" + query, UriKind.Relative), Ct);

        Assert.Equal(code == "listings.unknown_campus" ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await AuthFlow.JsonAsync(response)).GetProperty("code").GetString());
    }
}
