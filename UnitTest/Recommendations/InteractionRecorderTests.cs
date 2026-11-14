using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Recommendations;
using Musubi.Api.Modules.Recommendations.Data;
using Musubi.TestShared;

namespace UnitTest.Recommendations;

/// <summary>HU-35 (RF-48): las senales se guardan 90 dias, ids invalidos no se guardan y se borran con la cuenta (HU-46).</summary>
public sealed class InteractionRecorderTests
{
    private const string Campus = "6716a0000000000000000c01";

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryInteractionRepository interactions = new();
    private readonly InteractionRecorder recorder;

    public InteractionRecorderTests() =>
        recorder = new InteractionRecorder(interactions, new ManualTimeProvider(Now), NullLogger<InteractionRecorder>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASignal_LastsNinetyDays_AndRepeatingItCounts()
    {
        var ana = AuthIds.New();
        var product = AuthIds.New();

        await recorder.RecordAsync(new Interaction(ana, Campus, product, InteractionKinds.View), Ct);
        await recorder.RecordAsync(new Interaction(ana, Campus, product, InteractionKinds.View), Ct);

        var saved = Assert.Single(await interactions.FindByUserAsync(ana, 10, Ct));
        Assert.Equal((2, Now.UtcDateTime.AddDays(90)), (saved.Count, saved.ExpiresAt));
    }

    [Fact]
    public async Task InvalidIds_AreIgnored()
    {
        await recorder.RecordAsync(new Interaction("no-es-un-id", Campus, AuthIds.New(), InteractionKinds.View), Ct);
        await recorder.RecordAsync(new Interaction(AuthIds.New(), Campus, "x", InteractionKinds.View), Ct);

        Assert.Empty(await interactions.FindByCommunityAsync(Campus, Now.UtcDateTime.AddDays(-1), 10, Ct));
    }

    [Fact]
    public async Task DeletingTheAccount_ErasesTheHistory()
    {
        var ana = AuthIds.New();
        await recorder.RecordAsync(new Interaction(ana, Campus, AuthIds.New(), InteractionKinds.Favorite), Ct);

        await recorder.EraseAsync(ana, Ct);

        Assert.Empty(await interactions.FindByUserAsync(ana, 10, Ct));
    }

    [Theory]
    [InlineData(InteractionKinds.Purchase, 5)]
    [InlineData(InteractionKinds.Chat, 3)]
    [InlineData(InteractionKinds.Favorite, 2)]
    [InlineData(InteractionKinds.View, 1)]
    public void Weights_PurchaseChatFavoriteView(string kind, double weight) => Assert.Equal(weight, InteractionKinds.Weight(kind));
}
