using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using IntegrationTest.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Musubi.Api.Modules.GenAi;
using SkiaSharp;

namespace IntegrationTest.Listings;

/// <summary>
/// HU-59 de punta a punta (maqueta <c>Vender-1…4</c>): Diego sube la foto de su calculadora, toca "Llenar con IA" y
/// la propuesta queda en el borrador; la revisa, pone el precio y publica. La foto de Diego no le sirve a nadie mas, y
/// sin IA se publica igual, a mano (RNF-15).
/// </summary>
public sealed class PhotoDraftFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static readonly Uri DraftUri = new("/api/v1/products/photo-draft", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThePhoto_FillsTheDraft_AndItPublishesAfterReviewingIt()
    {
        var diego = await MemberAsync();
        var key = await UploadAsync(diego);

        var drafted = await diego.PostAsJsonAsync(DraftUri, new { photo = key }, Ct);

        Assert.Equal(HttpStatusCode.OK, drafted.StatusCode);
        var draft = await AuthFlow.JsonAsync(drafted);
        Assert.Equal(InMemoryPhotoDraftWriter.Title, draft.GetProperty("title").GetString());
        Assert.Equal(("used", "good"), (draft.GetProperty("condition").GetString(), draft.GetProperty("conditionDetail").GetString()));
        var categories = await AuthFlow.JsonAsync(await diego.GetAsync(new Uri("/api/v1/categories", UriKind.Relative), Ct));
        Assert.Contains(draft.GetProperty("category").GetString(), categories.EnumerateArray().Select(c => c.GetProperty("slug").GetString()));

        // RN-38: nada se publico solo; quien vende lo revisa (aqui cambia el nombre) y pone el precio.
        var mine = await AuthFlow.JsonAsync(await diego.GetAsync(new Uri("/api/v1/products/mine", UriKind.Relative), Ct));
        Assert.Empty(mine.GetProperty("items").EnumerateArray());
        var published = await diego.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new
            {
                title = "Calculadora Casio fx-82 con tapa",
                description = draft.GetProperty("description").GetString(),
                category = draft.GetProperty("category").GetString(),
                priceCents = 30_000,
                condition = draft.GetProperty("condition").GetString(),
                conditionDetail = draft.GetProperty("conditionDetail").GetString(),
                stock = 1,
                photos = new[] { key },
            },
            Ct);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
    }

    [Fact]
    public async Task SomeoneElsesPhoto_IsNotFound()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var hers = await UploadAsync(ana);

        var other = await diego.PostAsJsonAsync(DraftUri, new { photo = hers }, Ct);
        var invented = await diego.PostAsJsonAsync(DraftUri, new { photo = "media/products/000000000000000000000000/00000000000000000000000000000000.webp" }, Ct);

        await AuthFlow.AssertCodeAsync(other, HttpStatusCode.NotFound, "listings.photo_not_found");
        await AuthFlow.AssertCodeAsync(invented, HttpStatusCode.NotFound, "listings.photo_not_found");
    }

    [Fact]
    public async Task WithoutAi_ItSaysSo_AndPublishingByHandKeepsWorking()
    {
        using var off = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["GenAi:Provider"] = "Off" })));
        var diego = await MemberAsync(off);
        var key = await UploadAsync(diego);

        var drafted = await diego.PostAsJsonAsync(DraftUri, new { photo = key }, Ct);
        var published = await diego.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title = "Calculadora Casio", category = "calculadoras", priceCents = 30_000, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { key } },
            Ct);

        await AuthFlow.AssertCodeAsync(drafted, HttpStatusCode.ServiceUnavailable, "listings.draft_unavailable");
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
    }

    private static async Task<string> UploadAsync(HttpClient client)
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Teal);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(data.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "calculadora.png");
        var response = await client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await AuthFlow.JsonAsync(response).ConfigureAwait(false)).GetProperty("key").GetString()!;
    }

    private async Task<HttpClient> MemberAsync(WebApplicationFactory<Program>? host = null)
    {
        // El JWT se emite con el reloj de la fabrica, pero JwtBearer lo valida con la hora real.
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var app = host ?? factory;
        var email = AuthFlow.NewEmail();
        using var anonymous = app.CreateClient();
        var registered = await anonymous.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Diego Prueba", password = AuthFlow.Password, acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var verified = await anonymous.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = factory.Emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await AuthFlow.JsonAsync(verified).ConfigureAwait(false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }
}
