using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.Matching.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared;
using UnitTest.Common;

namespace UnitTest.Matching;

/// <summary>
/// HU-60: las coincidencias tambien por significado. La solicitud "Calculadora científica" encuentra la "Casio fx-991"
/// aunque no compartan palabras; se mantienen la categoria, el presupuesto, lo propio y los bloqueos (HU-19); los
/// vectores se guardan y se recalculan solo si cambia el texto; sin proveedor o si falla, deciden las palabras (RNF-15).
/// </summary>
public sealed class SemanticMatchingTests
{
    private const string Campus = "6716a0000000000000000c01";

    private static readonly DateTimeOffset Now = new(2026, 11, 25, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly RecordingNotificationCenter center = new();
    private readonly InMemoryEmbeddingRepository embeddings = new();
    private readonly CountingEmbedder embedder = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly string ana = AuthIds.New();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACalculadoraCientifica_FindsTheCasio_WithoutSharedWords_OnlyBySense()
    {
        var diego = AuthIds.New();
        await ProductAsync(diego, "Casio fx-991ES Plus", "417 funciones, la usé en Física.");
        await ProductAsync(AuthIds.New(), "Regla T de 60 cm");
        var request = Request("Calculadora científica");

        var bySense = await Notifier(Semantic.On(embeddings, embedder)).NotifyAsync(request, Ct);
        var byWords = await Notifier(Semantic.Off()).NotifyAsync(Request("Calculadora científica"), Ct);

        Assert.Equal(1, bySense);
        Assert.Equal(diego, Assert.Single(center.Sent).UserId);
        Assert.Equal(0, byWords);
    }

    [Fact]
    public async Task TheRulesOfHu19_Stay_CategoryBudgetOwnAndBlocks()
    {
        var blocked = AuthIds.New();
        await ProductAsync(ana, "Casio fx-991ES Plus");
        await ProductAsync(blocked, "Casio fx-82MS");
        await ProductAsync(AuthIds.New(), "Casio fx-570", price: 80_000);
        await ProductAsync(AuthIds.New(), "Casio fx-350", category: "libros");
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), CommunityId = Campus, BlockerId = ana, BlockedId = blocked, BlockedName = "Luis" }, Ct);

        var notified = await Notifier(Semantic.On(embeddings, embedder)).NotifyAsync(Request("Calculadora científica", budget: 50_000), Ct);

        Assert.Equal(0, notified);
        Assert.Empty(center.Sent);
    }

    [Fact]
    public async Task Vectors_AreSaved_AndRecomputedOnlyWhenTheTextChanges()
    {
        var semantic = Semantic.On(embeddings, embedder, time);
        var product = MatchText.Product(AuthIds.New(), AuthIds.New(), Campus, "Casio fx-991ES Plus", "Con tapa.");
        var request = MatchText.Wanted(AuthIds.New(), ana, Campus, "Calculadora científica");

        var first = await semantic.SimilaritiesAsync(request, [product], computeMissing: true, Ct);
        var again = await semantic.SimilaritiesAsync(request, [product], computeMissing: true, Ct);
        var edited = product with { Text = Embeddings.Text("Casio fx-991ES Plus", "Con tapa y manual.") };
        await semantic.SimilaritiesAsync(request, [edited], computeMissing: true, Ct);

        Assert.Equal(first[product.Id], again[product.Id], 6);
        // La primera vez, uno por papel; la segunda, nada; al editar, solo la publicacion.
        Assert.Equal(3, embedder.Calls);
        var saved = await embeddings.FindAsync([product.Key, request.Key], Ct);
        Assert.Equal(2, saved.Count);
        Assert.Equal(SemanticMatcher.Hash(edited, embedder.Model), saved.Single(e => e.Kind == EmbeddingKinds.Product).TextHash);
        Assert.All(saved, e => Assert.Equal(Now.UtcDateTime, e.UpdatedAt));
    }

    [Fact]
    public async Task FromTheApp_OnlyTheSavedVectorsAreUsed_AndTheAiIsNotCalled()
    {
        var semantic = Semantic.On(embeddings, embedder);
        var product = MatchText.Product(AuthIds.New(), AuthIds.New(), Campus, "Casio fx-991ES Plus", null);
        var request = MatchText.Wanted(AuthIds.New(), ana, Campus, "Calculadora científica");

        var before = await semantic.SimilaritiesAsync(request, [product], computeMissing: false, Ct);
        await semantic.RememberAsync(request, Ct);
        await semantic.RememberAsync(product, Ct);
        var calls = embedder.Calls;
        var after = await semantic.SimilaritiesAsync(request, [product], computeMissing: false, Ct);

        Assert.Empty(before);
        Assert.True(after[product.Id] >= MatchRules.SemanticThreshold);
        Assert.Equal(calls, embedder.Calls);
    }

    [Fact]
    public async Task WithoutAProvider_OrIfItFails_TheWordsDecide()
    {
        var product = MatchText.Product(AuthIds.New(), AuthIds.New(), Campus, "Casio fx-991ES Plus", null);
        var request = MatchText.Wanted(AuthIds.New(), ana, Campus, "Calculadora científica");
        embedder.Fail = true;

        Assert.Empty(await Semantic.Off().SimilaritiesAsync(request, [product], computeMissing: true, Ct));
        Assert.Empty(await Semantic.On(embeddings, embedder).SimilaritiesAsync(request, [product], computeMissing: true, Ct));
        Assert.False(await Semantic.On(embeddings, embedder).RememberAsync(product, Ct));
        Assert.Empty(await Semantic.On(new BrokenRepository(), embedder).SimilaritiesAsync(request, [product], computeMissing: true, Ct));
    }

    [Fact]
    public async Task DeletingTheAccount_ErasesItsVectors()
    {
        var semantic = Semantic.On(embeddings, embedder);
        var mine = MatchText.Wanted(AuthIds.New(), ana, Campus, "Calculadora científica");
        var theirs = MatchText.Wanted(AuthIds.New(), AuthIds.New(), Campus, "Bata");
        await semantic.RememberAsync(mine, Ct);
        await semantic.RememberAsync(theirs, Ct);

        await semantic.EraseAsync(ana, Ct);

        Assert.Equal(theirs.Key, Assert.Single(await embeddings.FindAsync([mine.Key, theirs.Key], Ct)).Id);
    }

    [Fact]
    public void Rules_ByWordsOrBySense_AndTheSimilarOnes()
    {
        Assert.True(MatchRules.Compatible(0.5, null));
        Assert.False(MatchRules.Compatible(0.4, null));
        Assert.True(MatchRules.Compatible(0, MatchRules.SemanticThreshold));
        Assert.False(MatchRules.Compatible(0, MatchRules.SemanticThreshold - 0.01));
        Assert.Equal(0.8, MatchRules.Score(0.5, 0.8), 6);
        Assert.Equal(1, MatchRules.Score(1, 0.8), 6);

        // HU-18: por significado tambien hay exactas y parecidas.
        var exact = new ProductCandidate(AuthIds.New(), AuthIds.New(), "Casio fx-991ES", 45_000, ["casio", "fx", "991e"]);
        var near = new ProductCandidate(AuthIds.New(), AuthIds.New(), "TI-84 Plus", 70_000, ["ti", "84", "plu"]);
        var far = new ProductCandidate(AuthIds.New(), AuthIds.New(), "Regla T", 10_000, ["regla", "t"]);
        var similarity = new Dictionary<string, double>(StringComparer.Ordinal) { [exact.Id] = 0.78, [near.Id] = 0.71, [far.Id] = 0.5 };

        var ranked = WantedMatchesService.Rank([far, near, exact], MatchRules.Terms("Calculadora científica"), 50_000, similarity).ToList();

        Assert.Equal([(exact.Id, WantedMatchKinds.Exact), (near.Id, WantedMatchKinds.Similar)], ranked);
    }

    [Fact]
    public void AListing_ReachesTheRequestsThatMeanTheSame_InItsBudget()
    {
        // HU-29 y HU-38: de la publicacion a las solicitudes, con la misma regla.
        var product = new ProductPublished(AuthIds.New(), Campus, AuthIds.New(), "Casio fx-991ES Plus", "calculadoras", 45_000, ["casio", "fx", "991e", "plu"]);
        var hers = new WantedForMatch(AuthIds.New(), Campus, ana, "Calculadora científica", "calculadoras", 45_000, Active: true);
        var cheap = new WantedForMatch(AuthIds.New(), Campus, AuthIds.New(), "Calculadora para cálculo", "calculadoras", 20_000, Active: true);
        var other = new WantedForMatch(AuthIds.New(), Campus, AuthIds.New(), "Regla T", "calculadoras", 45_000, Active: true);
        var similarity = new Dictionary<string, double>(StringComparer.Ordinal) { [hers.Id] = 0.77, [cheap.Id] = 0.8, [other.Id] = 0.4 };

        Assert.Equal([hers], DemandRules.BestRequests([other, cheap, hers], product, similarity));
        Assert.Empty(DemandRules.BestRequests([other, cheap, hers], product));
    }

    private WantedMatchNotifier Notifier(SemanticMatcher semantic) =>
        new(new ProductMatches(products), new BlockDirectory(blocks), center, new MatchLedger(new InMemoryMatchRepository(), time, NullLogger<MatchLedger>.Instance), semantic, time);

    private WantedPublished Request(string title, long budget = 50_000) => new(AuthIds.New(), Campus, ana, "Ana R.", title, "calculadoras", budget);

    private async Task ProductAsync(string seller, string title, string description = "", long price = 45_000, string category = "calculadoras")
    {
        await products.InsertAsync(
            new ProductDocument
            {
                Id = AuthIds.New(),
                CommunityId = Campus,
                SellerId = seller,
                Seller = new ProductSeller { Name = "Diego H." },
                Title = title,
                Description = description,
                CategorySlug = category,
                PriceCents = price,
                Stock = 1,
                Status = ProductStatuses.Active,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false);
    }

    /// <summary>La IA en memoria, contando las llamadas; con <see cref="Fail"/>, no responde.</summary>
    private sealed class CountingEmbedder : ITextEmbedder
    {
        private readonly InMemoryTextEmbedder inner = new();

        public int Calls { get; private set; }

        public bool Fail { get; set; }

        public bool Enabled => true;

        public string Model => inner.Model;

        public Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken cancellationToken)
        {
            Calls++;
            return Fail ? Task.FromResult<IReadOnlyList<float[]>?>(null) : inner.EmbedAsync(texts, role, cancellationToken);
        }
    }

    /// <summary>La base no responde.</summary>
    private sealed class BrokenRepository : IEmbeddingRepository
    {
        public Task<IReadOnlyList<EmbeddingDocument>> FindAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
            throw new TimeoutException("sin base");

        public Task UpsertAsync(EmbeddingDocument embedding, CancellationToken cancellationToken) => throw new TimeoutException("sin base");

        public Task<long> DeleteByOwnerAsync(string ownerId, CancellationToken cancellationToken) => throw new TimeoutException("sin base");
    }
}
