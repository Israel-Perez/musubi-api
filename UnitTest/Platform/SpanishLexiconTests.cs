using Amazon.Comprehend;
using Amazon.Rekognition;
using Amazon.Runtime;
using Amazon.Translate;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Musubi.Api.Platform.Moderation;

namespace UnitTest.Platform;

/// <summary>
/// 02-07: el lexico local rechaza groserias, odio, sexual, amenazas y drogas aunque se disfracen, sin rechazar
/// palabras inocentes que las contienen; y el moderador de produccion no llama a Translate ni Comprehend con
/// <c>TextProvider=Lexicon</c> (el plan gratuito de AWS no los incluye).
/// </summary>
public sealed class SpanishLexiconTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("eres un pendejo")]
    [InlineData("PENDEJOOOO")]
    [InlineData("p e n d e j o")]
    [InlineData("hijo de p.u.t.a")]
    [InlineData("p u t a o no")]
    [InlineData("pend3j0")]
    [InlineData("put@ madre")]
    [InlineData("¡Cabrón!")]
    [InlineData("vete a la verga")]
    [InlineData("chinguen a su madre")]
    [InlineData("qué coño quieres")]
    [InlineData("Si no me pagas te voy a matar")]
    [InlineData("te parto la madre, ya verás")]
    [InlineData("vendo marihuana barata")]
    [InlineData("fuck you")]
    public void Rejects_OffensiveText_EvenDisguised(string text)
    {
        Assert.True(SpanishLexicon.Default.IsOffensive(text));
    }

    [Theory]
    [InlineData("Calculadora Casio fx-991ES Plus")]
    [InlineData("¿Sigue disponible? Te lo compro en 500")]
    [InlineData("Computadora sin disputa, buena reputación")]
    [InlineData("Me da vergüenza preguntar, ¿lo dejas en $300?")]
    [InlineData("Cono de tránsito naranja para el laboratorio")]
    [InlineData("Mañana te lo llevo al edificio 4, a las 3 p. m.")]
    [InlineData("Libro de Química 3a edición, como nuevo")]
    [InlineData("Para matar el tiempo en el camión")]
    [InlineData("Guía para el examen de Niger y Nigeria")]
    [InlineData("")]
    [InlineData(null)]
    public void Allows_InnocentText_ThatContainsAForbiddenWord(string? text)
    {
        Assert.False(SpanishLexicon.Default.IsOffensive(text));
    }

    [Theory]
    [InlineData("  Árbol  ÑANDÚ  ", "arbol ñandu")]
    [InlineData("p.u.t.a no", "puta no")]
    [InlineData("hoooola 500 pesos", "hola 50 pesos")]
    public void Normalize_LowercasesStripsAccentsKeepsTheEnieAndJoinsLetters(string text, string expected)
    {
        Assert.Equal(expected, SpanishLexicon.Normalize(text));
    }

    [Fact]
    public void ExtraTerms_FromConfiguration_AddWordsAndPhrases()
    {
        var lexicon = new SpanishLexicon(["Fraudulento", "deposita primero", " "]);

        Assert.True(lexicon.IsOffensive("Es FRAUDULENTO"));
        Assert.True(lexicon.IsOffensive("Deposita primero y luego te lo mando"));
        Assert.False(lexicon.IsOffensive("Primero deposita"));
    }

    [Fact]
    public async Task AwsModerator_WithTheLexiconTextProvider_NeverCallsTranslateOrComprehend()
    {
        // Clientes que fallarian si se usaran: un Allowed prueba que no se llamaron.
        using var clients = new UnreachableClients();
        var moderator = clients.Moderator(ModerationOptions.LexiconTextProvider);

        var verdicts = await moderator.CheckTextsAsync(["Calculadora Casio", "eres un pendejo", ""], Ct);

        Assert.Equal([ModerationVerdict.Allowed, ModerationVerdict.Rejected, ModerationVerdict.Allowed], verdicts);
    }

    [Fact]
    public async Task AwsModerator_WithAwsText_KeepsTheLexiconRejection_WhenAwsIsDown()
    {
        using var clients = new UnreachableClients();
        var moderator = clients.Moderator(ModerationOptions.AwsProvider);

        var verdicts = await moderator.CheckTextsAsync(["Calculadora Casio", "eres un pendejo"], Ct);

        Assert.Equal([ModerationVerdict.Unavailable, ModerationVerdict.Rejected], verdicts);
    }

    /// <summary>Clientes de AWS apuntando a un puerto cerrado, sin reintentos.</summary>
    private sealed class UnreachableClients : IDisposable
    {
        private const string Nowhere = "http://127.0.0.1:9";
        private static readonly BasicAWSCredentials Credentials = new("unit", "test");

        private readonly AmazonRekognitionClient rekognition = new(Credentials, new AmazonRekognitionConfig { ServiceURL = Nowhere, MaxErrorRetry = 0 });
        private readonly AmazonTranslateClient translate = new(Credentials, new AmazonTranslateConfig { ServiceURL = Nowhere, MaxErrorRetry = 0 });
        private readonly AmazonComprehendClient comprehend = new(Credentials, new AmazonComprehendConfig { ServiceURL = Nowhere, MaxErrorRetry = 0 });

        public AwsContentModerator Moderator(string textProvider) => new(
            rekognition,
            translate,
            comprehend,
            SpanishLexicon.Default,
            Options.Create(new ModerationOptions { TextProvider = textProvider }),
            NullLogger<AwsContentModerator>.Instance);

        public void Dispose()
        {
            rekognition.Dispose();
            translate.Dispose();
            comprehend.Dispose();
        }
    }
}
