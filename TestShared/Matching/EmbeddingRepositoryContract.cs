using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Matching.Data;

namespace Musubi.TestShared.Matching;

/// <summary>
/// HU-60: comportamiento esperado de los vectores de las coincidencias. Corre contra el fake en memoria (UnitTest) y
/// contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class EmbeddingRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 25, 18, 0, 0, DateTimeKind.Utc);

    internal abstract IEmbeddingRepository Embeddings { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EmbeddingDocument Vector(string kind, string itemId, string ownerId, string hash, params float[] values) => new()
    {
        Id = EmbeddingDocument.KeyOf(kind, itemId),
        Kind = kind,
        ItemId = itemId,
        CommunityId = InMemoryCommunityRepository.UtttId,
        OwnerId = ownerId,
        TextHash = hash,
        Vector = values,
        UpdatedAt = Now,
    };

    [Fact]
    public async Task SavesAndReplaces_AndFindsOnlyTheAskedOnes()
    {
        var (product, wanted, other, diego, ana) = (AuthIds.New(), AuthIds.New(), AuthIds.New(), AuthIds.New(), AuthIds.New());

        await Embeddings.UpsertAsync(Vector(EmbeddingKinds.Product, product, diego, new string('a', 32), 0.25f, -0.5f, 1f), Ct);
        await Embeddings.UpsertAsync(Vector(EmbeddingKinds.Wanted, wanted, ana, new string('b', 32), 1f, 0f, 0f), Ct);
        await Embeddings.UpsertAsync(Vector(EmbeddingKinds.Product, other, diego, new string('c', 32), 0f, 1f, 0f), Ct);
        // La publicacion se edito: el vector se reemplaza.
        await Embeddings.UpsertAsync(Vector(EmbeddingKinds.Product, product, diego, new string('d', 32), 0.5f, 0.5f, 0.5f), Ct);

        var found = await Embeddings.FindAsync([EmbeddingDocument.KeyOf(EmbeddingKinds.Product, product), EmbeddingDocument.KeyOf(EmbeddingKinds.Wanted, wanted), "product:000000000000000000000000"], Ct);

        Assert.Equal(2, found.Count);
        var mine = Assert.Single(found, e => e.Kind == EmbeddingKinds.Product);
        Assert.Equal((product, diego, new string('d', 32)), (mine.ItemId, mine.OwnerId, mine.TextHash));
        Assert.Equal([0.5f, 0.5f, 0.5f], mine.Vector);
        Assert.Equal(Now, mine.UpdatedAt);
        Assert.Empty(await Embeddings.FindAsync([], Ct));
    }

    [Fact]
    public async Task DeletingAnAccount_RemovesOnlyItsVectors()
    {
        var (diego, ana) = (AuthIds.New(), AuthIds.New());
        var (hisProduct, hisRequest, hers) = (AuthIds.New(), AuthIds.New(), AuthIds.New());
        await Embeddings.UpsertAsync(Vector(EmbeddingKinds.Product, hisProduct, diego, new string('a', 32), 1f), Ct);
        await Embeddings.UpsertAsync(Vector(EmbeddingKinds.Wanted, hisRequest, diego, new string('a', 32), 1f), Ct);
        await Embeddings.UpsertAsync(Vector(EmbeddingKinds.Product, hers, ana, new string('a', 32), 1f), Ct);

        Assert.Equal(2, await Embeddings.DeleteByOwnerAsync(diego, Ct));
        Assert.Equal(0, await Embeddings.DeleteByOwnerAsync("no-es-un-id", Ct));

        var left = await Embeddings.FindAsync(
            [EmbeddingDocument.KeyOf(EmbeddingKinds.Product, hisProduct), EmbeddingDocument.KeyOf(EmbeddingKinds.Wanted, hisRequest), EmbeddingDocument.KeyOf(EmbeddingKinds.Product, hers)],
            Ct);
        Assert.Equal(hers, Assert.Single(left).ItemId);
    }
}
