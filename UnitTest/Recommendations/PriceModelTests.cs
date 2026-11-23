using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Recommendations;
using Musubi.TestShared;

namespace UnitTest.Recommendations;

/// <summary>
/// HU-58 (L-16i): la regresion de ML.NET estima un rango de precio con la categoria, el estado y las palabras del nombre;
/// medida fuera de linea, se equivoca menos que la mediana de la categoria (lo que se haria sin ML). Con pocos precios no
/// sugiere nada, lo vendido pesa mas que lo publicado y se reentrena en segundo plano.
/// </summary>
public sealed class PriceModelTests
{
    private const string Uttt = "6716a0000000000000000001";

    private static readonly DateTime Now = new(2026, 11, 23, 18, 0, 0, DateTimeKind.Utc);

    // Precio base por categoria (pesos), por palabra del nombre y por estado: asi se ponen los precios en un campus.
    private static readonly (string Category, double Base, (string Words, double Factor)[] Kinds)[] Campus =
    [
        ("calculadoras", 300, [("Calculadora básica Casio", 0.4), ("Calculadora científica Casio fx-991", 1.0), ("Calculadora graficadora Texas TI-84", 3.0)]),
        ("libros", 150, [("Libro de cálculo Stewart", 1.6), ("Novela de bolsillo", 0.5), ("Libro de física Serway", 1.4)]),
        ("electronica", 1500, [("Audífonos inalámbricos", 0.3), ("Tablet Samsung", 2.0), ("Laptop Lenovo", 4.0)]),
        ("ropa", 200, [("Bata de laboratorio", 1.0), ("Sudadera UTTT", 1.6), ("Gorra", 0.5)]),
    ];

    private static readonly (string Condition, double Factor)[] Conditions =
        [("new", 1.3), ("used:likeNew", 1.0), ("used:good", 0.85), ("used:fair", 0.65)];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Precios de un campus con +-12 % de ruido.</summary>
    private static List<PriceSample> CampusPrices(int perKind = 16)
    {
        var samples = new List<PriceSample>();
        var n = 0;
        foreach (var (category, @base, kinds) in Campus)
        {
            foreach (var (words, factor) in kinds)
            {
                for (var i = 0; i < perKind; i++)
                {
                    var (condition, conditionFactor) = Conditions[i % Conditions.Length];
                    // Ruido fijo de -12 % a +12 % (sin Random: siempre los mismos precios).
                    var noise = 1 + ((((n * 37) % 25) - 12) / 100d);
                    var pesos = Math.Round(@base * factor * conditionFactor * noise);
                    samples.Add(new PriceSample($"p{n++:D4}", words, category, condition, (long)pesos * 100, Sold: i % 3 == 0));
                }
            }
        }

        return samples;
    }

    [Fact]
    public void MeasuredOffline_TheModelBeatsTheCategoryMedian()
    {
        var model = PriceModel.Train(CampusPrices(), Now, evaluate: true);

        var e = Assert.IsType<PriceEvaluation>(model.Evaluation);
        TestContext.Current.TestOutputHelper?.WriteLine($"MAE del modelo ${e.ModelMae:0.0} contra ${e.BaselineMae:0.0} de la mediana ({e.Train} para entrenar, {e.Test} apartados)");
        Assert.True(e.Test >= PriceModel.MinPerCategory, $"apartados: {e.Test}");
        Assert.Equal(model.SampleCount, e.Train + e.Test);
        // La mediana de la categoria no ve las palabras ni el estado; el modelo si.
        Assert.True(e.ModelMae < e.BaselineMae * 0.6, $"MAE del modelo ${e.ModelMae:0} contra ${e.BaselineMae:0} de la mediana");
    }

    [Fact]
    public void TheRange_FollowsTheWordsAndTheCondition_AndContainsTheTypicalPrice()
    {
        var model = PriceModel.Train(CampusPrices(), Now, evaluate: true);

        var scientific = model.Estimate("Calculadora científica Casio fx-991", "calculadoras", "used:likeNew")!;
        var graphing = model.Estimate("Calculadora graficadora Texas TI-84", "calculadoras", "used:likeNew")!;
        var newOne = model.Estimate("Calculadora científica Casio fx-991", "calculadoras", "new")!;
        var worn = model.Estimate("Calculadora científica Casio fx-991", "calculadoras", "used:fair")!;

        Assert.InRange(30_000L, scientific.MinCents, scientific.MaxCents);
        Assert.True(graphing.MinCents > scientific.MaxCents, "la graficadora vale mas que la cientifica");
        Assert.True(newOne.MinCents + newOne.MaxCents > worn.MinCents + worn.MaxCents, "nueva vale mas que con detalles");
        Assert.True(scientific.MinCents < scientific.MaxCents);
        Assert.Equal(48, scientific.Samples);
    }

    [Fact]
    public void TheRange_IsInRoundPesos()
    {
        var model = PriceModel.Train(CampusPrices(), Now, evaluate: false);

        var cheap = model.Estimate("Gorra", "ropa", "used:good")!;
        var laptop = model.Estimate("Laptop Lenovo", "electronica", "used:good")!;

        Assert.All(new[] { cheap.MinCents, cheap.MaxCents }, c => Assert.Equal(0, c % 500));
        Assert.All(new[] { laptop.MinCents, laptop.MaxCents }, c => Assert.Equal(0, c % 5_000));
    }

    [Fact]
    public void WithFewPricesInTheCategory_OrInTheCampus_ItSuggestsNothing()
    {
        var prices = CampusPrices();
        prices.Add(new PriceSample("x1", "Kalimba de 17 notas", "musica", "used:good", 45_000, Sold: false));
        var model = PriceModel.Train(prices, Now, evaluate: true);
        var tiny = PriceModel.Train(prices.Take(PriceModel.MinSamples - 1).ToList(), Now, evaluate: true);

        Assert.Null(model.Estimate("Kalimba", "musica", "used:good"));
        Assert.Null(model.Estimate("Lo que sea", "sin-precios", "new"));
        Assert.Null(tiny.Estimate("Calculadora científica Casio fx-991", "calculadoras", "used:good"));
        Assert.Null(tiny.Evaluation);
    }

    [Fact]
    public void WhatWasPaid_WeighsMoreThanWhatIsListed()
    {
        // Las mismas calculadoras se publican a $500, pero se pagan a $300.
        var listed = Enumerable.Range(0, 30).Select(i => new PriceSample($"l{i}", "Calculadora científica", "calculadoras", "used:good", 50_000, Sold: false));
        var paid = Enumerable.Range(0, 30).Select(i => new PriceSample($"s{i}", "Calculadora científica", "calculadoras", "used:good", 30_000, Sold: true));

        var range = PriceModel.Train([.. listed, .. paid], Now, evaluate: false).Estimate("Calculadora científica", "calculadoras", "used:good")!;

        // Con 3 a 1, el centro queda mas cerca de $300 que de $500.
        Assert.True((range.MinCents + range.MaxCents) / 2 < 40_000, $"{range.MinCents}–{range.MaxCents}");
    }

    [Theory]
    [InlineData(25_000, "good")]
    [InlineData(30_000, "fair")]
    [InlineData(40_000, "fair")]
    [InlineData(45_000, "high")]
    public void TheVerdict_ComparesThePriceWithTheRange(long price, string expected) =>
        Assert.Equal(expected, PriceVerdicts.For(price, new PriceRange(30_000, 40_000, 12)));

    [Fact]
    public async Task TheStore_JoinsWhatWasPaidWithItsListing_AndSkipsUnknownOnes()
    {
        var catalog = new FakePriceCatalog();
        catalog.Listed.Add(new PriceItem("p1", Uttt, "Calculadora científica", "calculadoras", "used:good", 50_000));
        catalog.Others.Add(new PriceItem("p2", Uttt, "Bata de laboratorio", "ropa", "new", 30_000));
        var sold = new FakeSoldPrices();
        sold.Paid.AddRange([new SoldPrice("p1", "Calculadora", 45_000), new SoldPrice("p2", "Bata", 28_000), new SoldPrice("p9", "Ya no existe", 1_000)]);
        var store = new PriceModelStore(catalog, sold, new ManualTimeProvider(Now), NullLogger<PriceModelStore>.Instance);

        var samples = await store.SamplesAsync(Uttt, Ct);

        Assert.Equal(
            [("p1", false, 50_000L), ("p1", true, 45_000L), ("p2", true, 28_000L)],
            samples.Select(s => (s.Key, s.Sold, s.PriceCents)));
        Assert.Equal(("ropa", "new"), (samples[2].CategorySlug, samples[2].Condition));
    }

    [Fact]
    public async Task AfterThirtyMinutes_TheStoreAnswersWithTheCurrentModel_AndRetrainsInTheBackground()
    {
        var catalog = new FakePriceCatalog();
        var time = new ManualTimeProvider(new DateTimeOffset(Now));
        var store = new PriceModelStore(catalog, new FakeSoldPrices(), time, NullLogger<PriceModelStore>.Instance);
        var first = await store.GetAsync(Uttt, Ct);
        catalog.Listed.AddRange(CampusPrices().Select(s => new PriceItem(s.Key, Uttt, s.Title, s.CategorySlug, s.Condition, s.PriceCents)));

        var early = await store.GetAsync(Uttt, Ct);
        time.Advance(PriceModelStore.Freshness);
        var stale = await store.GetAsync(Uttt, Ct);
        var fresh = stale;
        for (var i = 0; i < 100 && ReferenceEquals(fresh, first); i++)
        {
            await Task.Delay(50, Ct);
            fresh = await store.GetAsync(Uttt, Ct);
        }

        Assert.Same(first, early);
        Assert.Same(first, stale);
        Assert.NotSame(first, fresh);
        Assert.Equal(0, first.SampleCount);
        Assert.Equal(192, fresh.SampleCount);
    }

    [Fact]
    public async Task TheService_ValidatesTheRequest_AndWithoutDataAnswersWithoutARange()
    {
        var catalog = new FakePriceCatalog();
        var service = new PriceSuggestionService(
            new PriceModelStore(catalog, new FakeSoldPrices(), new ManualTimeProvider(Now), NullLogger<PriceModelStore>.Instance),
            catalog);

        var invalid = await service.SuggestAsync(Uttt, new PriceSuggestionRequest("ab", "calculadoras", "used", "good"), Ct);
        var noCondition = await service.SuggestAsync(Uttt, new PriceSuggestionRequest("Calculadora", "calculadoras", "broken", null), Ct);
        var empty = await service.SuggestAsync(Uttt, new PriceSuggestionRequest("Calculadora científica", "calculadoras", "used", "good"), Ct);
        var missing = await service.EstimateAsync("u1", Uttt, "p404", Ct);

        Assert.Equal("recommendations.invalid_price_request", invalid.Error!.Code);
        Assert.Equal("recommendations.invalid_price_request", noCondition.Error!.Code);
        Assert.Equal(new PriceSuggestionResponse(null, null, 0), empty.Value);
        Assert.Equal("listings.product_not_found", missing.Error!.Code);
    }

    [Fact]
    public async Task TheService_EstimatesAVisibleListing_WithItsVerdict()
    {
        var catalog = new FakePriceCatalog();
        catalog.Listed.AddRange(CampusPrices().Select(s => new PriceItem(s.Key, Uttt, s.Title, s.CategorySlug, s.Condition, s.PriceCents)));
        catalog.Visible["mine"] = new PriceItem("mine", Uttt, "Calculadora graficadora Texas TI-84", "calculadoras", "used:likeNew", 300_000);
        var service = new PriceSuggestionService(
            new PriceModelStore(catalog, new FakeSoldPrices(), new ManualTimeProvider(Now), NullLogger<PriceModelStore>.Instance),
            catalog);

        var estimate = (await service.EstimateAsync("u1", Uttt, "mine", Ct)).Value!;
        var suggestion = (await service.SuggestAsync(Uttt, new PriceSuggestionRequest("  Calculadora graficadora Texas TI-84 ", "calculadoras", "used", "likeNew"), Ct)).Value!;

        Assert.Equal("high", estimate.Verdict);
        Assert.Equal((estimate.MinCents, estimate.MaxCents), (suggestion.MinCents, suggestion.MaxCents));
    }

    private sealed class FakePriceCatalog : IPriceCatalog
    {
        public List<PriceItem> Listed { get; } = [];

        public List<PriceItem> Others { get; } = [];

        public Dictionary<string, PriceItem> Visible { get; } = [];

        public Task<IReadOnlyList<PriceItem>> PricedAsync(string communityId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PriceItem>>([.. Listed]);

        public Task<IReadOnlyList<PriceItem>> ByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PriceItem>>([.. Listed.Concat(Others).Where(p => ids.Contains(p.Id))]);

        public Task<PriceItem?> FindVisibleAsync(string viewerId, string viewerCommunityId, string productId, CancellationToken cancellationToken) =>
            Task.FromResult(Visible.GetValueOrDefault(productId));
    }

    private sealed class FakeSoldPrices : ISoldPrices
    {
        public List<SoldPrice> Paid { get; } = [];

        public Task<IReadOnlyList<SoldPrice>> RecentAsync(string communityId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SoldPrice>>([.. Paid]);
    }
}
