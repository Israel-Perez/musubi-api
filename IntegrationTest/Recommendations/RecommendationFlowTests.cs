using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Recommendations;
using SkiaSharp;

namespace IntegrationTest.Recommendations;

/// <summary>
/// HU-35 de punta a punta: Diego vende dos calculadoras y una bata. Ana abre una calculadora y la guarda en favoritos, y
/// "Recomendado para ti" le muestra la otra calculadora antes que la bata. Alguien sin historial recibe lo general, nadie
/// recibe lo propio, y "Ver parecidos" funciona con y sin sesion.
/// </summary>
public sealed class RecommendationFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Member(HttpClient Client, string Id);

    private async Task<Member> MemberAsync()
    {
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var email = await flow.RegisterActiveAsync().ConfigureAwait(false);
        var login = await AuthFlow.JsonAsync(await flow.LoginAsync(email).ConfigureAwait(false)).ConfigureAwait(false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        var me = await AuthFlow.JsonAsync(await client.GetAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return new Member(client, me.GetProperty("id").GetString()!);
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Teal);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<string> PublishAsync(Member seller, string title, string category, string description)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString();
        var published = await seller.Client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title, description, category, priceCents = 40_000, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { key } },
            Ct).ConfigureAwait(false);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    private static List<string> Ids(JsonElement page) => [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!)];

    // El modelo se vuelve a entrenar cada 30 minutos; la prueba no espera.
    private void Retrain() => factory.Services.GetRequiredService<RecommendationModelStore>().Invalidate(InMemoryCommunityRepository.UtttId);

    [Fact]
    public async Task WhatAnaLooksAt_ShapesHerRecommendations_AndSimilarWorksForEveryone()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var luis = await MemberAsync();
        var casio = await PublishAsync(diego, "Calculadora científica Casio fx-991ES Plus", "calculadoras", "Con tapa y pilas.");
        var fx82 = await PublishAsync(diego, "Calculadora científica Casio fx-82MS", "calculadoras", "Para Cálculo.");
        var bata = await PublishAsync(diego, "Bata de laboratorio blanca talla M", "laboratorio", "Manga larga.");

        await GetAsync(ana.Client, $"/api/v1/products/{casio}");
        var saved = await ana.Client.PutAsync(new Uri($"/api/v1/favorites/{casio}", UriKind.Relative), null, Ct);
        Retrain();
        var forAna = await GetAsync(ana.Client, "/api/v1/recommendations?limit=20");
        var forLuis = await GetAsync(luis.Client, "/api/v1/recommendations?limit=20");
        var forDiego = await GetAsync(diego.Client, "/api/v1/recommendations?limit=20");

        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal("personal", forAna.GetProperty("basis").GetString());
        var anaIds = Ids(forAna);
        // Lo que ya vio no se repite, y la otra calculadora va antes que la bata.
        Assert.DoesNotContain(casio, anaIds);
        Assert.Contains(fx82, anaIds);
        Assert.True(!anaIds.Contains(bata) || anaIds.IndexOf(fx82) < anaIds.IndexOf(bata));
        Assert.Equal("general", forLuis.GetProperty("basis").GetString());
        // RN-45: nunca lo propio.
        Assert.DoesNotContain(casio, Ids(forDiego));
        Assert.DoesNotContain(fx82, Ids(forDiego));

        using var visitor = factory.CreateClient();
        var similar = await GetAsync(ana.Client, $"/api/v1/products/{casio}/similar");
        var similarForVisitor = await GetAsync(visitor, $"/api/v1/products/{casio}/similar");

        Assert.Equal("similar", similar.GetProperty("basis").GetString());
        Assert.Equal(fx82, Ids(similar)[0]);
        Assert.NotEqual(JsonValueKind.Null, similar.GetProperty("items")[0].GetProperty("seller").ValueKind);
        // Sin sesion, sin vendedor (RNF-05).
        Assert.Equal(JsonValueKind.Null, similarForVisitor.GetProperty("items")[0].GetProperty("seller").ValueKind);
    }

    [Fact]
    public async Task Similar_OfAProductThatDoesNotExist_Is404()
    {
        var ana = await MemberAsync();

        var response = await ana.Client.GetAsync(new Uri("/api/v1/products/6716a0000000000000000fff/similar", UriKind.Relative), Ct);

        await AuthFlow.AssertCodeAsync(response, HttpStatusCode.NotFound, "listings.product_not_found");
    }
}
