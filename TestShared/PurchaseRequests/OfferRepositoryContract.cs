using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.PurchaseRequests.Data;

namespace Musubi.TestShared.PurchaseRequests;

/// <summary>
/// HU-16: comportamiento esperado del repositorio de ofertas. Corre contra el fake en memoria (UnitTest) y contra
/// MongoDB real con db/ aplicado (DatabaseTest).
/// </summary>
public abstract class OfferRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    internal abstract IOfferRepository Offers { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OfferDocument Offer(string requestId, string sellerId, string? requesterId = null, string kind = OfferKinds.Direct) => new()
    {
        Id = AuthIds.New(),
        CommunityId = AuthIds.New(),
        RequestId = requestId,
        RequesterId = requesterId ?? AuthIds.New(),
        SellerId = sellerId,
        SellerName = "Ana R.",
        Kind = kind,
        ProductId = kind == OfferKinds.Listing ? AuthIds.New() : null,
        Title = "Bata blanca de algodón, talla M",
        PriceCents = 28_000,
        Condition = "used",
        PhotoKey = "media/products/6716a0000000000000000001/0123456789abcdef0123456789abcdef.webp",
        Message = "Te la llevo al edificio de laboratorios.",
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public async Task Insert_ThenFindMine_RoundTripsAllFields()
    {
        var request = AuthIds.New();
        var seller = AuthIds.New();
        var offer = Offer(request, seller, kind: OfferKinds.Listing);

        Assert.True(await Offers.TryInsertAsync(offer, Ct));
        var read = (await Offers.FindMineAsync(request, seller, Ct))!;

        Assert.Equal((offer.Id, offer.Kind, offer.ProductId, offer.Title), (read.Id, read.Kind, read.ProductId, read.Title));
        Assert.Equal((28_000L, "used", offer.PhotoKey, offer.Message), (read.PriceCents, read.Condition, read.PhotoKey, read.Message));
        Assert.Equal((OfferStatuses.Sent, Now, Now), (read.Status, read.CreatedAt, read.UpdatedAt));
        Assert.Null(await Offers.FindMineAsync(request, AuthIds.New(), Ct));
        Assert.Null(await Offers.FindMineAsync("no-es-un-id", seller, Ct));
    }

    [Fact]
    public async Task OnePerPersonAndRequest_ButReplaceable()
    {
        var request = AuthIds.New();
        var seller = AuthIds.New();
        var first = Offer(request, seller);
        Assert.True(await Offers.TryInsertAsync(first, Ct));

        // RN-14: una segunda con otro id no entra.
        Assert.False(await Offers.TryInsertAsync(Offer(request, seller), Ct));
        Assert.True(await Offers.TryInsertAsync(Offer(request, AuthIds.New()), Ct));

        await Offers.ReplaceAsync(
            new OfferDocument
            {
                Id = first.Id, CommunityId = first.CommunityId, RequestId = request, RequesterId = first.RequesterId, SellerId = seller,
                SellerName = "Ana R.", Kind = OfferKinds.Direct, Title = "Bata talla M, como nueva", PriceCents = 25_000,
                Condition = "new", CreatedAt = Now, UpdatedAt = Now.AddHours(1),
            },
            Ct);
        var read = (await Offers.FindMineAsync(request, seller, Ct))!;

        Assert.Equal(("Bata talla M, como nueva", 25_000L, "new", Now.AddHours(1)), (read.Title, read.PriceCents, read.Condition, read.UpdatedAt));
        Assert.Null(read.Message);
    }

    [Fact]
    public async Task DeleteByPerson_RemovesTheOnesMadeAndReceived()
    {
        var me = AuthIds.New();
        var made = Offer(AuthIds.New(), me);
        var received = Offer(AuthIds.New(), AuthIds.New(), requesterId: me);
        var others = Offer(AuthIds.New(), AuthIds.New());
        foreach (var o in new[] { made, received, others })
        {
            Assert.True(await Offers.TryInsertAsync(o, Ct));
        }

        Assert.Equal(2, await Offers.DeleteByPersonAsync(me, Ct));
        Assert.Null(await Offers.FindMineAsync(made.RequestId, me, Ct));
        Assert.NotNull(await Offers.FindMineAsync(others.RequestId, others.SellerId, Ct));
    }
}
