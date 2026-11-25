using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.GenAi;

namespace UnitTest.GenAi;

/// <summary>
/// HU-60: los vectores de las coincidencias. Gemini recibe solo textos publicos sin datos de contacto, con la llave en
/// el header, el tipo de tarea de cada papel y 256 dimensiones; cualquier falla deja las palabras (RNF-15).
/// </summary>
public sealed class TextEmbeddingsTests : IDisposable
{
    private readonly List<IDisposable> owned = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void TheText_HasTheTitleAndDescription_WithoutContactData()
    {
        var text = Embeddings.Text("Casio fx-991ES", "Escríbeme a diego@uttt.edu.mx o al 7731234567. Ver https://wa.me/123");

        Assert.StartsWith("Casio fx-991ES. Escríbeme a ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("diego@uttt.edu.mx", text, StringComparison.Ordinal);
        Assert.DoesNotContain("7731234567", text, StringComparison.Ordinal);
        Assert.DoesNotContain("wa.me", text, StringComparison.Ordinal);
        Assert.Equal("Calculadora científica", Embeddings.Text(" Calculadora científica ", null));
        Assert.Equal(Embeddings.MaxText, Embeddings.Text(new string('a', 80), new string('b', 900)).Length);
    }

    [Fact]
    public void Cosine_IsOneForTheSame_ZeroForOrthogonal_AndSafeForEmpty()
    {
        Assert.Equal(1, Embeddings.Cosine([1f, 2f, 3f], [2f, 4f, 6f]), 6);
        Assert.Equal(0, Embeddings.Cosine([1f, 0f], [0f, 1f]), 6);
        Assert.Equal(0, Embeddings.Cosine([], []));
        Assert.Equal(0, Embeddings.Cosine([1f, 2f], [1f]));
        Assert.Equal(0, Embeddings.Cosine([0f, 0f], [1f, 1f]));
    }

    [Fact]
    public async Task TheInMemoryAi_BringsTogetherWhatMeansTheSame()
    {
        var ai = new InMemoryTextEmbedder();
        var vectors = (await ai.EmbedAsync(["Calculadora científica", "Casio fx-991ES Plus. 417 funciones.", "Regla T de 60 cm de madera."], EmbeddingRole.Document, Ct))!;

        Assert.True(Embeddings.Cosine(vectors[0], vectors[1]) >= 0.74);
        Assert.True(Embeddings.Cosine(vectors[0], vectors[2]) < 0.3);
        Assert.Equal(InMemoryTextEmbedder.Dimensions, vectors[0].Length);
    }

    [Fact]
    public async Task Gemini_GetsTheKeyInAHeader_TheRoleAndTheDimensions_InBatchesOf100()
    {
        var (embedder, handler) = Gemini(request => Vectors(request, 256));
        var texts = Enumerable.Range(0, 150).Select(i => $"Calculadora {i}").ToList();

        var vectors = await embedder.EmbedAsync(texts, EmbeddingRole.Query, Ct);

        Assert.Equal(150, vectors!.Count);
        Assert.Equal(2, handler.Requests.Count);
        var first = handler.Requests[0];
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:batchEmbedContents", first.Uri);
        Assert.Equal("llave-de-prueba", first.Key);
        Assert.DoesNotContain("llave-de-prueba", first.Body, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(first.Body);
        var requests = body.RootElement.GetProperty("requests");
        Assert.Equal(100, requests.GetArrayLength());
        Assert.Equal("models/gemini-embedding-001", requests[0].GetProperty("model").GetString());
        Assert.Equal("RETRIEVAL_QUERY", requests[0].GetProperty("taskType").GetString());
        Assert.Equal(256, requests[0].GetProperty("outputDimensionality").GetInt32());
        Assert.Equal("Calculadora 0", requests[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("gemini-embedding-001:256", embedder.Model);
    }

    [Fact]
    public async Task Gemini_ForAListing_UsesTheDocumentRole()
    {
        var (embedder, handler) = Gemini(request => Vectors(request, 256));

        await embedder.EmbedAsync(["Casio fx-991ES"], EmbeddingRole.Document, Ct);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        Assert.Equal("RETRIEVAL_DOCUMENT", body.RootElement.GetProperty("requests")[0].GetProperty("taskType").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Gemini_WhenItRefuses_IsNull(HttpStatusCode status)
    {
        var (embedder, _) = Gemini(_ => new HttpResponseMessage(status));

        Assert.Null(await embedder.EmbedAsync(["Calculadora"], EmbeddingRole.Query, Ct));
    }

    [Fact]
    public async Task Gemini_WithAnotherShape_OtherDimensions_OrWithoutNetwork_IsNull()
    {
        var (fewer, _) = Gemini(_ => Json("""{"embeddings":[]}"""));
        var (smaller, _) = Gemini(request => Vectors(request, 8));
        var (broken, _) = Gemini(_ => Json("no es json"));
        var (offline, _) = Gemini(_ => throw new HttpRequestException("sin red"));
        var (slow, _) = Gemini(_ => throw new TaskCanceledException("tiempo agotado"));

        foreach (var embedder in new[] { fewer, smaller, broken, offline, slow })
        {
            Assert.Null(await embedder.EmbedAsync(["Calculadora"], EmbeddingRole.Query, Ct));
        }
    }

    [Fact]
    public async Task Gemini_WithoutAKey_IsOff_AndCallsNobody()
    {
        var (embedder, handler) = Gemini(request => Vectors(request, 256), key: " ");

        Assert.False(embedder.Enabled);
        Assert.Null(await embedder.EmbedAsync(["Calculadora"], EmbeddingRole.Query, Ct));
        Assert.Empty(handler.Requests);
        Assert.False(new OffTextEmbedder().Enabled);
        Assert.Null(await new OffTextEmbedder().EmbedAsync(["Calculadora"], EmbeddingRole.Query, Ct));
    }

    public void Dispose()
    {
        foreach (var d in owned)
        {
            d.Dispose();
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Vectors(HttpRequestMessage request, int dimensions)
    {
        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult());
        var count = body.RootElement.GetProperty("requests").GetArrayLength();
        var embeddings = Enumerable.Range(0, count).Select(i => new { values = Enumerable.Repeat(0.1f + i, dimensions).ToArray() }).ToArray();
        return Json(JsonSerializer.Serialize(new { embeddings }));
    }

    private (GeminiTextEmbedder Embedder, FakeHandler Handler) Gemini(Func<HttpRequestMessage, HttpResponseMessage> respond, string key = "llave-de-prueba")
    {
        var handler = new FakeHandler(respond);
        owned.Add(handler);
        return (new GeminiTextEmbedder(
            new Clients(handler),
            Options.Create(new GenAiOptions()),
            Options.Create(new GeminiOptions { ApiKey = key }),
            NullLogger<GeminiTextEmbedder>.Instance), handler);
    }

    private sealed class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = GeminiApi.BaseAddress };
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Uri, string? Key, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Requests.Add((request.RequestUri!.ToString(), request.Headers.TryGetValues("x-goog-api-key", out var keys) ? keys.Single() : null, body));
            return respond(request);
        }
    }
}
