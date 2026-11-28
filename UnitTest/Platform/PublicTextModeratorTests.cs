using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Platform.Moderation;

namespace UnitTest.Platform;

/// <summary>
/// SCRUM-127: el texto publico pasa por el lexico y despues por la IA en la nube; el privado (chat) nunca sale a la
/// IA; si la IA no responde, decide el lexico.
/// </summary>
public sealed class PublicTextModeratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PublicTextModerator Moderator(FakeClassifier classifier) =>
        new(new InMemoryContentModerator(), classifier, NullLogger<PublicTextModerator>.Instance);

    [Fact]
    public async Task PublicText_WhatTheLexiconAllows_GoesToTheCloud()
    {
        var classifier = new FakeClassifier(texts => [.. texts.Select(t => t.Contains("estafa", StringComparison.Ordinal))]);

        var verdicts = await Moderator(classifier).CheckPublicTextsAsync(["Calculadora Casio", "Es una estafa, deposita antes", ""], Ct);

        Assert.Equal([ModerationVerdict.Allowed, ModerationVerdict.Rejected, ModerationVerdict.Allowed], verdicts);
        Assert.Equal(["Calculadora Casio", "Es una estafa, deposita antes"], Assert.Single(classifier.Calls));
    }

    [Fact]
    public async Task WhatTheLexiconRejects_NeverReachesTheCloud()
    {
        var classifier = new FakeClassifier(texts => [.. texts.Select(_ => false)]);

        var verdicts = await Moderator(classifier).CheckPublicTextsAsync(["Eres un pendejo", "Libro de cálculo"], Ct);

        Assert.Equal([ModerationVerdict.Rejected, ModerationVerdict.Allowed], verdicts);
        Assert.Equal(["Libro de cálculo"], Assert.Single(classifier.Calls));
    }

    [Fact]
    public async Task PrivateText_ChatAndOrders_NeverGoesToTheCloud()
    {
        var classifier = new FakeClassifier(texts => [.. texts.Select(_ => true)]);

        var verdicts = await Moderator(classifier).CheckTextsAsync(["Nos vemos en la biblioteca a las 2"], Ct);

        Assert.Equal([ModerationVerdict.Allowed], verdicts);
        Assert.Empty(classifier.Calls);
    }

    [Fact]
    public async Task IfTheCloudDoesNotAnswer_TheLexiconDecides()
    {
        var classifier = new FakeClassifier(_ => null);

        var verdicts = await Moderator(classifier).CheckPublicTextsAsync(["Calculadora", "Eres un pendejo"], Ct);

        Assert.Equal([ModerationVerdict.Allowed, ModerationVerdict.Rejected], verdicts);
    }

    [Fact]
    public async Task AnAnswerOfTheWrongSize_IsIgnored()
    {
        var classifier = new FakeClassifier(_ => [true]);

        var verdicts = await Moderator(classifier).CheckPublicTextsAsync(["Calculadora", "Libro"], Ct);

        Assert.Equal([ModerationVerdict.Allowed, ModerationVerdict.Allowed], verdicts);
    }

    [Fact]
    public async Task NothingToCheck_NoCall()
    {
        var classifier = new FakeClassifier(_ => throw new InvalidOperationException("no deberia llamar"));

        Assert.Equal([ModerationVerdict.Allowed], await Moderator(classifier).CheckPublicTextsAsync(["  "], Ct));
    }

    [Theory]
    [InlineData("Lexicon", true)]
    [InlineData("Aws", true)]
    [InlineData("Gemini", true)]
    [InlineData("OpenAi", false)]
    public void TheTextProvider_IsValidated(string provider, bool valid)
    {
        var validator = new ModerationOptionsValidator(new HostingEnvironment { EnvironmentName = "Production" });

        var result = validator.Validate(null, new ModerationOptions { TextProvider = provider });

        Assert.Equal(valid, result.Succeeded);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(6, true)]
    [InlineData(31, false)]
    public void TheTextTimeout_IsValidated(int seconds, bool valid)
    {
        var validator = new ModerationOptionsValidator(new HostingEnvironment { EnvironmentName = "Production" });

        Assert.Equal(valid, validator.Validate(null, new ModerationOptions { TextTimeoutSeconds = seconds }).Succeeded);
    }

    private sealed class FakeClassifier(Func<IReadOnlyList<string>, IReadOnlyList<bool>?> answer) : ITextSafetyClassifier
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<IReadOnlyList<bool>?> FindInappropriateAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Calls.Add(texts);
            return Task.FromResult(answer(texts));
        }
    }
}
