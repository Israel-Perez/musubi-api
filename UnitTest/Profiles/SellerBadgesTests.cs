using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;

namespace UnitTest.Profiles;

/// <summary>
/// HU-64 (decisiones de la usuaria, 16-Nov-2026): "Vendedor confiable" con 4.5 estrellas o mas y al menos 5 ventas;
/// "Responde en ~N" es la mediana de las respuestas de los ultimos 30 dias, con al menos 3. Sin datos, nada.
/// </summary>
public sealed class SellerBadgesTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc);

    private static ResponseSample Sample(double minutes, int daysAgo = 1) => new() { At = Now.AddDays(-daysAgo), Minutes = minutes };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(4.5, 5, true)]
    [InlineData(4.9, 21, true)]
    [InlineData(4.49, 30, false)]
    [InlineData(5.0, 4, false)]
    public void Trusted_Needs45StarsAndFiveSales(double rating, int sales, bool trusted) =>
        Assert.Equal(trusted, SellerBadges.Trusted(new ProfileStats { RatingAvg = rating, RatingCount = 3, Sales = sales }));

    [Fact]
    public void Trusted_WithoutRatings_IsFalse() =>
        Assert.False(SellerBadges.Trusted(new ProfileStats { Sales = 10 }));

    [Fact]
    public void ResponseMinutes_IsTheMedianOfTheLast30Days()
    {
        List<ResponseSample> samples = [Sample(5), Sample(60), Sample(600), Sample(30), Sample(1, daysAgo: 31), Sample(2, daysAgo: 40)];

        // Quedan 5, 30, 60 y 600: la mediana es (30 + 60) / 2.
        Assert.Equal(45, SellerBadges.ResponseMinutes(samples, Now));
        Assert.Equal(60, SellerBadges.ResponseMinutes([Sample(5), Sample(60), Sample(600)], Now));
    }

    [Fact]
    public void ResponseMinutes_NeedsThreeRecentReplies_AndIsAtLeastOneMinute()
    {
        Assert.Null(SellerBadges.ResponseMinutes([Sample(5), Sample(60)], Now));
        Assert.Null(SellerBadges.ResponseMinutes([Sample(5), Sample(60), Sample(7, daysAgo: 31)], Now));
        Assert.Equal(1, SellerBadges.ResponseMinutes([Sample(0.1), Sample(0.2), Sample(0.3)], Now));
    }

    [Fact]
    public async Task TheRepository_KeepsTheLast20_AndCreatesTheProfile()
    {
        var profiles = new InMemoryProfileRepository();
        var responses = new ProfileResponses(profiles);
        var seller = AuthIds.New();

        for (var i = 1; i <= 25; i++)
        {
            await responses.RecordAsync(seller, AuthIds.New(), "Valeria Cruz", TimeSpan.FromMinutes(i), Now, Ct);
        }

        var profile = await profiles.FindAsync(seller, Ct);
        Assert.Equal(ResponseSample.Kept, profile!.Responses.Count);
        Assert.Equal(6, profile.Responses[0].Minutes);
        Assert.Equal("Valeria Cruz", profile.FullName);
    }
}
