using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Caching;

/// <summary>
/// 02-02: GET condicional (ETag y 304) en los catalogos y la vitrina, y la vitrina del visitante (cache de
/// salida) muestra lo recien publicado en la siguiente consulta.
/// </summary>
public sealed class CachingFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
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

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? etag = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        return await client.SendAsync(request, Ct).ConfigureAwait(false);
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Purple);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Theory]
    [InlineData("/api/v1/categories")]
    [InlineData("/api/v1/products")]
    public async Task PublicCatalogs_AnswerWithAnETag_And304WhenUnchanged(string path)
    {
        using var client = factory.CreateClient();

        var first = await GetAsync(client, path);
        var etag = first.Headers.ETag?.ToString();
        var again = await GetAsync(client, path, etag);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotNull(etag);
        Assert.Contains("no-cache", first.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
        Assert.Empty(await again.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task Careers_WithSession_AlsoAnswer304()
    {
        using var client = await SignedInClientAsync();

        var first = await GetAsync(client, "/api/v1/careers");
        var again = await GetAsync(client, "/api/v1/careers", first.Headers.ETag?.ToString());
        var stale = await GetAsync(client, "/api/v1/careers", "\"otra-version\"");

        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
    }

    [Fact]
    public async Task Showcase_OfTheVisitor_ShowsWhatWasJustPublished()
    {
        using var visitor = factory.CreateClient();
        var before = await AuthFlow.JsonAsync(await GetAsync(visitor, "/api/v1/products"));
        using var seller = await SignedInClientAsync();
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var key = (await AuthFlow.JsonAsync(await seller.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct)))
            .GetProperty("key").GetString();

        var published = await seller.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new
            {
                title = "Bata de laboratorio talla M",
                category = "laboratorio",
                priceCents = 25_000,
                condition = "new",
                stock = 1,
                photos = new[] { key },
            },
            Ct);
        var id = (await AuthFlow.JsonAsync(published)).GetProperty("id").GetString();
        var after = await AuthFlow.JsonAsync(await GetAsync(visitor, "/api/v1/products"));

        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        Assert.DoesNotContain(before.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetString() == id);
        Assert.Contains(after.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetString() == id);
    }
}
