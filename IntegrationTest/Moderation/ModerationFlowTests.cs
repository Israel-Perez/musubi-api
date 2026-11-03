using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using IntegrationTest.Auth;
using Musubi.Api.Platform.Moderation;
using SkiaSharp;

namespace IntegrationTest.Moderation;

/// <summary>
/// 02-07: lo inapropiado se rechaza antes de guardarse. Moderador en memoria: el marcador en un texto y las
/// fotos magenta.
/// </summary>
public sealed class ModerationFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static readonly SKColor Magenta = new(255, 0, 255);

    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> SignedInClientAsync()
    {
        // El JWT se emite con el reloj de la fabrica y JwtBearer lo valida con la hora real.
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

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string path, SKColor color, bool put = false)
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(data.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var uri = new Uri(path, UriKind.Relative);
        return put
            ? await client.PutAsync(uri, form, Ct).ConfigureAwait(false)
            : await client.PostAsync(uri, form, Ct).ConfigureAwait(false);
    }

    private static Task<HttpResponseMessage> PublishAsync(HttpClient client, string key, string title, string description) =>
        client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title, description, category = "libros", priceCents = 20_000, condition = "new", stock = 1, photos = new[] { key } },
            Ct);

    [Fact]
    public async Task InappropriatePhoto_Is422_AndNeverStored()
    {
        using var client = await SignedInClientAsync();

        var product = await UploadAsync(client, "/api/v1/products/photos", Magenta);
        var avatar = await UploadAsync(client, "/api/v1/profiles/me/photo", Magenta, put: true);
        var me = await AuthFlow.JsonAsync(await client.GetAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), Ct));

        await AuthFlow.AssertCodeAsync(product, (HttpStatusCode)422, "moderation.image_rejected");
        await AuthFlow.AssertCodeAsync(avatar, (HttpStatusCode)422, "moderation.image_rejected");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, me.GetProperty("photo").ValueKind);
    }

    [Theory]
    [InlineData(true, "el nombre del producto")]
    [InlineData(false, "la descripción")]
    public async Task InappropriateText_Is422_NamesTheField_AndIsNotPublished(bool inTitle, string field)
    {
        using var client = await SignedInClientAsync();
        var key = (await AuthFlow.JsonAsync(await UploadAsync(client, "/api/v1/products/photos", SKColors.Teal))).GetProperty("key").GetString()!;
        var marker = InMemoryContentModerator.RejectMarker;

        var response = await PublishAsync(
            client,
            key,
            inTitle ? $"Libro {marker}" : "Libro de Cálculo de Stewart",
            inTitle ? "Como nuevo." : $"Como nuevo {marker}");
        var mine = await AuthFlow.JsonAsync(await client.GetAsync(new Uri("/api/v1/products/mine", UriKind.Relative), Ct));

        await AuthFlow.AssertCodeAsync(response, (HttpStatusCode)422, "moderation.text_rejected");
        Assert.Contains(field, (await AuthFlow.JsonAsync(response)).GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Empty(mine.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task AppropriateContent_IsPublished()
    {
        using var client = await SignedInClientAsync();
        var key = (await AuthFlow.JsonAsync(await UploadAsync(client, "/api/v1/products/photos", SKColors.Teal))).GetProperty("key").GetString()!;

        var response = await PublishAsync(client, key, "Libro de Cálculo de Stewart", "Como nuevo, sin rayones.");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
