using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.GenAi;

namespace UnitTest.GenAi;

/// <summary>
/// HU-59: "Llenar con IA". La IA mira la portada y propone el nombre, la categoria del campus, el estado y la descripcion.
/// Solo sale la foto y los nombres de las categorias (RNF-06), la respuesta pasa las reglas de la publicacion antes de
/// usarse (RN-03, RN-04) y cualquier falla de Gemini deja la publicacion a mano (RNF-15).
/// </summary>
public sealed class PhotoDraftTests : IDisposable
{
    private const string FullAnswer =
        """{"title":"Calculadora científica Casio fx-991ES Plus","category":"calculadoras","condition":"used","conditionDetail":"likeNew","description":"Calculadora científica Casio con su tapa. Se ve como nueva."}""";

    private static readonly DraftCategory[] Categories =
    [
        new("calculadoras", "Calculadoras"),
        new("laboratorio", "Laboratorio"),
        new("ropa", "Ropa y batas"),
    ];

    private static readonly PhotoDraftPrompt Prompt = new(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x01, 0x02 }, Categories);

    private readonly List<IDisposable> owned = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ThePromptText_HasOnlyTheCampusCategories()
    {
        Assert.Equal(
            """
            Categorías del campus (slug: nombre):
            - calculadoras: Calculadoras
            - laboratorio: Laboratorio
            - ropa: Ropa y batas

            """.ReplaceLineEndings(Environment.NewLine),
            PhotoDraftText.User(Prompt));
    }

    [Fact]
    public void AFullAnswer_IsTheDraft()
    {
        Assert.Equal(
            new PhotoDraft("Calculadora científica Casio fx-991ES Plus", "calculadoras", "used", "likeNew", "Calculadora científica Casio con su tapa. Se ve como nueva."),
            PhotoDraftJson.Parse(FullAnswer, Categories));
    }

    [Fact]
    public void ACategoryThatIsNotOfTheCampus_IsLeftEmpty()
    {
        var other = PhotoDraftJson.Parse(Answer(category: "electronica"), Categories);
        var none = PhotoDraftJson.Parse(Answer(category: null), Categories);

        Assert.Null(other!.Category);
        Assert.Null(none!.Category);
        Assert.Equal("calculadoras", PhotoDraftJson.Parse(Answer(category: " calculadoras "), Categories)!.Category);
    }

    [Fact]
    public void TheCondition_FollowsTheRules()
    {
        // Nuevo no lleva detalle; un usado sin detalle valido queda en "Buen estado" (RN-04).
        Assert.Equal(("new", (string?)null), Condition(Answer(condition: "new", detail: "likeNew")));
        Assert.Equal(("used", "good"), Condition(Answer(condition: "used", detail: null)));
        Assert.Equal(("used", "good"), Condition(Answer(condition: "used", detail: "roto")));
        Assert.Equal(("used", "fair"), Condition(Answer(condition: "used", detail: "fair")));
        Assert.Null(PhotoDraftJson.Parse(Answer(condition: "seminuevo"), Categories));
        Assert.Null(PhotoDraftJson.Parse(Answer(condition: null), Categories));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("[]")]
    [InlineData("""{"title":"Calculadora","condition":"used"}""")]
    [InlineData("""{"title":3,"category":null,"condition":"used","conditionDetail":"good","description":"Bien."}""")]
    public void AnAnswerWithAnotherShape_IsNotUsed(string json)
    {
        Assert.Null(PhotoDraftJson.Parse(json, Categories));
    }

    [Fact]
    public void AnAnswerOutsideTheRules_IsNotUsed()
    {
        Assert.Null(PhotoDraftJson.Parse(Answer(title: "ab"), Categories));
        Assert.Null(PhotoDraftJson.Parse(Answer(title: new string('a', 81)), Categories));
        Assert.Null(PhotoDraftJson.Parse(Answer(description: "  "), Categories));
        Assert.Null(PhotoDraftJson.Parse(Answer(description: new string('a', 501)), Categories));
        Assert.NotNull(PhotoDraftJson.Parse(Answer(title: new string('a', 80), description: new string('a', 500)), Categories));
    }

    [Fact]
    public async Task Gemini_GetsTheKeyInAHeader_ThePhoto_TheSchema_AndNothingElse()
    {
        var (writer, handler) = Gemini(_ => Reply(FullAnswer));

        var draft = await writer.SuggestAsync(Prompt, Ct);

        Assert.Equal("calculadoras", draft?.Category);
        var request = handler.Requests.Single();
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent", request.Uri);
        Assert.Equal("llave-de-prueba", request.Key);
        Assert.DoesNotContain("llave-de-prueba", request.Body, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(request.Body);
        var parts = body.RootElement.GetProperty("contents")[0].GetProperty("parts");
        Assert.Equal(2, parts.GetArrayLength());
        var photo = parts[0].GetProperty("inlineData");
        Assert.Equal("image/webp", photo.GetProperty("mimeType").GetString());
        Assert.Equal(Prompt.Photo.ToArray(), Convert.FromBase64String(photo.GetProperty("data").GetString()!));
        Assert.Equal(PhotoDraftText.User(Prompt), parts[1].GetProperty("text").GetString());
        Assert.Equal(PhotoDraftText.System, body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        var schema = body.RootElement.GetProperty("generationConfig").GetProperty("responseSchema");
        Assert.Equal(PhotoDraftJson.Fields, schema.GetProperty("required").EnumerateArray().Select(f => f.GetString()));
        var properties = schema.GetProperty("properties");
        Assert.Equal(["new", "used"], properties.GetProperty("condition").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.True(properties.GetProperty("category").GetProperty("nullable").GetBoolean());
        Assert.False(properties.GetProperty("title").TryGetProperty("nullable", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Gemini_WhenItRefuses_LeavesItByHand(HttpStatusCode status)
    {
        var (writer, _) = Gemini(_ => new HttpResponseMessage(status));

        Assert.Null(await writer.SuggestAsync(Prompt, Ct));
    }

    [Theory]
    [InlineData("""{"candidates":[{"content":{"parts":[{"text":"una calculadora"}]},"finishReason":"STOP"}]}""")]
    [InlineData("""{"candidates":[{"finishReason":"SAFETY"}]}""")]
    [InlineData("""{"promptFeedback":{"blockReason":"SAFETY"}}""")]
    [InlineData("no es json")]
    public async Task Gemini_WithABlockedOrStrangeAnswer_LeavesItByHand(string body)
    {
        var (writer, _) = Gemini(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        Assert.Null(await writer.SuggestAsync(Prompt, Ct));
    }

    [Fact]
    public async Task Gemini_WithoutNetwork_OrTooSlow_LeavesItByHand()
    {
        var (offline, _) = Gemini(_ => throw new HttpRequestException("sin red"));
        var (slow, _) = Gemini(_ => throw new TaskCanceledException("tiempo agotado"));

        Assert.Null(await offline.SuggestAsync(Prompt, Ct));
        Assert.Null(await slow.SuggestAsync(Prompt, Ct));
    }

    [Fact]
    public async Task Gemini_WithoutAKey_IsOff_AndCallsNobody()
    {
        var (writer, handler) = Gemini(_ => Reply(FullAnswer), key: "");

        Assert.False(writer.Enabled);
        Assert.Null(await writer.SuggestAsync(Prompt, Ct));
        Assert.Empty(handler.Requests);
        Assert.False(new OffPhotoDraftWriter().Enabled);
        Assert.Null(await new OffPhotoDraftWriter().SuggestAsync(Prompt, Ct));
    }

    [Fact]
    public async Task TheInMemoryAi_IsDeterministic_AndWithinTheRules()
    {
        var draft = await new InMemoryPhotoDraftWriter().SuggestAsync(Prompt, Ct);
        var noCategories = await new InMemoryPhotoDraftWriter().SuggestAsync(Prompt with { Categories = [] }, Ct);

        Assert.Equal(InMemoryPhotoDraftWriter.Title, draft!.Title);
        Assert.Equal(("calculadoras", "used", "good"), (draft.Category, draft.Condition, draft.ConditionDetail));
        Assert.InRange(draft.Description.Length, 1, 500);
        Assert.Null(noCategories!.Category);
    }

    public void Dispose()
    {
        foreach (var d in owned)
        {
            d.Dispose();
        }
    }

    private static (string?, string?) Condition(string json)
    {
        var draft = PhotoDraftJson.Parse(json, Categories);
        return (draft?.Condition, draft?.ConditionDetail);
    }

    private static string Answer(
        string title = "Calculadora científica",
        string? category = "calculadoras",
        string? condition = "used",
        string? detail = "good",
        string description = "Calculadora con su tapa.") =>
        JsonSerializer.Serialize(new { title, category, condition, conditionDetail = detail, description });

    private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = json } } }, finishReason = "STOP" } } }),
            Encoding.UTF8,
            "application/json"),
    };

    private (GeminiPhotoDraftWriter Writer, FakeHandler Handler) Gemini(Func<HttpRequestMessage, HttpResponseMessage> respond, string key = "llave-de-prueba")
    {
        var handler = new FakeHandler(respond);
        owned.Add(handler);
        var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = GeminiApi.BaseAddress };
        owned.Add(client);
        return (new GeminiPhotoDraftWriter(
            client,
            Options.Create(new GenAiOptions()),
            Options.Create(new GeminiOptions { ApiKey = key }),
            NullLogger<GeminiPhotoDraftWriter>.Instance), handler);
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
