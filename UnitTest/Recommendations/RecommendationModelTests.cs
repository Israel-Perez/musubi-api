using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Recommendations;
using Musubi.Api.Modules.Recommendations.Data;

namespace UnitTest.Recommendations;

/// <summary>
/// HU-35 (L-16i): el modelo de ML. TF-IDF aprendido del catalogo para "Ver parecidos", el perfil de cada persona con
/// pesos (comprar &gt; chat &gt; favorito &gt; ver) para "Para ti", filtrado colaborativo con datos suficientes, lo general
/// sin historial (RN-46) y la evaluacion leave-one-out contra el azar.
/// </summary>
public sealed class RecommendationModelTests
{
    private const string Campus = "6716a0000000000000000c01";

    private static readonly DateTime Now = new(2026, 11, 14, 18, 0, 0, DateTimeKind.Utc);

    private static readonly string Seller = AuthIds.New();

    private static CatalogItem Item(string title, string category, long price, string? description = null, string? seller = null, int hoursAgo = 1) =>
        new(AuthIds.New(), Campus, seller ?? Seller, title, description ?? string.Empty, category, price, Now.AddHours(-hoursAgo));

    private static InteractionDocument Did(string user, CatalogItem item, string kind = InteractionKinds.View, int daysAgo = 1) => new()
    {
        Id = AuthIds.New(),
        UserId = user,
        CommunityId = Campus,
        ProductId = item.Id,
        Kind = kind,
        At = Now.AddDays(-daysAgo),
        ExpiresAt = Now.AddDays(89),
    };

    // Dos grupos claros de gustos y algo de relleno.
    private static List<CatalogItem> Catalog() =>
    [
        Item("Calculadora Casio fx-991ES Plus", "calculadoras", 45_000, "Científica, con tapa y pilas."),
        Item("Calculadora científica Casio fx-82", "calculadoras", 28_000, "Para Cálculo y Álgebra."),
        Item("Calculadora graficadora TI-84 Plus", "calculadoras", 165_000, "Graficadora con cable."),
        Item("Calculadoras Casio fx-570 (dos piezas)", "calculadoras", 30_000, "Científicas usadas."),
        Item("Bata de laboratorio blanca talla M", "laboratorio", 25_000, "Manga larga, algodón."),
        Item("Bata de laboratorio talla S", "laboratorio", 22_000, "Poco uso."),
        Item("Gogles de seguridad para laboratorio", "laboratorio", 9_000, "Sin rayones."),
        Item("Guantes de nitrilo para laboratorio", "laboratorio", 6_000, "Caja de 100."),
        Item("Cálculo de una variable, Stewart", "libros", 38_000, "Octava edición."),
        Item("Laptop Lenovo IdeaPad 5", "laptops", 1_180_000, "Ryzen 7, 16 GB."),
    ];

    [Fact]
    public void Similar_FindsTheSameKindOfProduct_AndNeverTheSameOne()
    {
        var items = Catalog();
        var model = RecommendationModel.Train(Campus, items, [], Now, evaluate: false);
        var casio = items[0];

        var similar = model.Similar(casio, _ => true, 3);

        Assert.DoesNotContain(casio.Id, similar);
        Assert.Equal(3, similar.Count);
        Assert.All(similar, id => Assert.Equal("calculadoras", items.Single(i => i.Id == id).CategorySlug));
        // El mismo modelo con otro numero queda antes que la graficadora de otra marca.
        Assert.Equal(items[1].Id, similar[0]);
    }

    [Fact]
    public void Similar_UnderstandsPluralsAndAccents_ThroughLetterTrigrams()
    {
        var items = Catalog();
        var model = RecommendationModel.Train(Campus, items, [], Now, evaluate: false);
        var unseen = Item("calculadora cientifica", "otro", 30_000);

        var similar = model.Similar(unseen, _ => true, 4);

        Assert.Contains(items[1].Id, similar);
        Assert.Contains(items[3].Id, similar);
        Assert.DoesNotContain(items[4].Id, similar);
    }

    [Fact]
    public void Similar_RespectsWhatIsAllowed_AndSaysNothingWhenNothingIsSimilar()
    {
        var items = Catalog();
        var model = RecommendationModel.Train(Campus, items, [], Now, evaluate: false);

        var noFx82 = model.Similar(items[0], i => i.Id != items[1].Id, 5);
        var laptop = model.Similar(items[9], _ => true, 5);

        Assert.DoesNotContain(items[1].Id, noFx82);
        Assert.Empty(laptop);
    }

    [Fact]
    public void ForUser_LearnsTheTaste_AndLeavesOutWhatTheyAlreadySaw()
    {
        var items = Catalog();
        var ana = AuthIds.New();
        var model = RecommendationModel.Train(Campus, items, [], Now, evaluate: false);
        List<InteractionDocument> history = [Did(ana, items[4]), Did(ana, items[5], InteractionKinds.Favorite)];

        // Quedan dos del laboratorio que no ha visto.
        var forAna = model.ForUser(ana, history, _ => true, 2, Now);

        Assert.Equal(2, forAna.Count);
        Assert.DoesNotContain(items[4].Id, forAna);
        Assert.DoesNotContain(items[5].Id, forAna);
        Assert.All(forAna, id => Assert.Equal("laboratorio", items.Single(i => i.Id == id).CategorySlug));
    }

    [Fact]
    public void ForUser_APurchaseWeighsMoreThanViews_AndOldInterestsFade()
    {
        var items = Catalog();
        var ana = AuthIds.New();
        var model = RecommendationModel.Train(Campus, items, [], Now, evaluate: false);

        // Vio dos batas hace tiempo, pero compro una calculadora ayer.
        var bought = model.ForUser(
            ana,
            [Did(ana, items[4], daysAgo: 60), Did(ana, items[5], daysAgo: 60), Did(ana, items[0], InteractionKinds.Purchase)],
            _ => true,
            1,
            Now);

        Assert.Equal("calculadoras", items.Single(i => i.Id == bought[0]).CategorySlug);
    }

    [Fact]
    public void ForUser_WithoutUsefulHistory_IsEmpty()
    {
        var items = Catalog();
        var model = RecommendationModel.Train(Campus, items, [], Now, evaluate: false);
        var ana = AuthIds.New();
        // Una interaccion con algo que ya no esta disponible no dice nada del catalogo actual.
        var gone = Item("Vendida hace mucho", "otro", 1_000);

        Assert.Empty(model.ForUser(ana, [], _ => true, 5, Now));
        Assert.Empty(model.ForUser(ana, [Did(ana, gone)], _ => true, 5, Now));
    }

    [Fact]
    public void General_IsTheMostActiveOfTheCampus_ThenTheNewest()
    {
        var items = Catalog();
        var someone = AuthIds.New();
        var model = RecommendationModel.Train(
            Campus,
            items,
            [Did(someone, items[8], InteractionKinds.Purchase), Did(someone, items[2]), Did(someone, items[2], InteractionKinds.Favorite)],
            Now,
            evaluate: false);

        var general = model.General(_ => true, 3);

        Assert.Equal([items[8].Id, items[2].Id], general.Take(2));
    }

    [Fact]
    public void WithEnoughData_ItAlsoLearnsFromPeopleWithSimilarTastes()
    {
        var (items, interactions) = TwoTastes(users: 8);

        var model = RecommendationModel.Train(Campus, items, interactions, Now, evaluate: false);
        var few = RecommendationModel.Train(Campus, items, interactions.Take(5).ToList(), Now, evaluate: false);

        Assert.True(model.UsesCollaborative);
        Assert.False(few.UsesCollaborative);
    }

    [Fact]
    public void Evaluation_LeaveOneOut_BeatsRandomByFar()
    {
        var (items, interactions) = TwoTastes(users: 10);

        var evaluation = RecommendationModel.Evaluate(Campus, items, interactions, Now, k: 3);

        Assert.NotNull(evaluation);
        Assert.Equal(10, evaluation.Users);
        Assert.True(evaluation.HitRate >= 0.8, $"hit@3 = {evaluation.HitRate:0.00}");
        Assert.True(evaluation.HitRate >= 2 * evaluation.RandomBaseline, $"azar = {evaluation.RandomBaseline:0.00}");
    }

    [Fact]
    public void Evaluation_WithoutAnyoneToEvaluate_IsNull() =>
        Assert.Null(RecommendationModel.Evaluate(Campus, Catalog(), [], Now, k: 5));

    [Fact]
    public void Training_WithAnEmptyCatalog_Works() =>
        Assert.Empty(RecommendationModel.Train(Campus, [], [], Now, evaluate: true).General(_ => true, 5));

    // La mitad prefiere calculadoras y la otra mitad laboratorio: cada quien vio tres de su grupo en dias distintos.
    private static (List<CatalogItem> Items, List<InteractionDocument> Interactions) TwoTastes(int users)
    {
        var items = Catalog();
        var calculators = items.Where(i => i.CategorySlug == "calculadoras").ToList();
        var lab = items.Where(i => i.CategorySlug == "laboratorio").ToList();
        var interactions = new List<InteractionDocument>();
        for (var u = 0; u < users; u++)
        {
            var user = AuthIds.New();
            var group = u % 2 == 0 ? calculators : lab;
            for (var j = 0; j < 3; j++)
            {
                interactions.Add(Did(user, group[(u + j) % group.Count], daysAgo: 3 - j));
            }
        }

        return (items, interactions);
    }
}
