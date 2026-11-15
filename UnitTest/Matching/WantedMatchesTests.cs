using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Matching;

namespace UnitTest.Matching;

/// <summary>
/// HU-18 (decision de la usuaria, 14-Nov-2026): "Ya coinciden con lo que buscas" muestra primero las exactas (reglas de
/// HU-19, "Dentro de tu presupuesto") y despues las parecidas (RF-24, "Modelo parecido").
/// </summary>
public sealed class WantedMatchesTests
{
    private static ProductCandidate Candidate(string title, long price, string? id = null) =>
        new(id ?? AuthIds.New(), AuthIds.New(), title, price, SearchWord.Words(title));

    [Fact]
    public void ExactOnesFirst_ThenSimilar_ByWordsAndThenTheClosestPrice()
    {
        var terms = MatchRules.Terms("Calculadora Casio fx-991ES Plus");
        var cheapExact = Candidate("Calculadora Casio fx-991ES Plus usada", 45_000);
        var closeExact = Candidate("Calculadora Casio fx-991ES Plus nueva", 58_000);
        var similar = Candidate("Calculadora científica Sharp", 38_000);
        var tooExpensive = Candidate("Calculadora Casio fx-991ES Plus sellada", 80_000);
        var unrelated = Candidate("Bata de laboratorio", 10_000);

        var ranked = WantedMatchesService.Rank([unrelated, similar, cheapExact, tooExpensive, closeExact], terms, 60_000).ToList();

        Assert.Equal(
            [
                (closeExact.Id, WantedMatchKinds.Exact),
                (cheapExact.Id, WantedMatchKinds.Exact),
                (tooExpensive.Id, WantedMatchKinds.Similar),
                (similar.Id, WantedMatchKinds.Similar),
            ],
            ranked);
    }

    [Theory]
    [InlineData(60_000, 90_000)]
    [InlineData(1, 1)]
    public void ASimilarOne_CanCostUpToHalfMoreThanTheBudget(long budget, long max) =>
        Assert.Equal(max, WantedMatchesService.MaxSimilarPrice(budget));
}
