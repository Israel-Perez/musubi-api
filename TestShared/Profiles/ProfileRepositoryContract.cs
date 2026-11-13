using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Profiles.Data;

namespace Musubi.TestShared.Profiles;

/// <summary>
/// HU-04: comportamiento esperado de los repositorios de perfiles y carreras. Corre contra los fakes en memoria
/// (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa un usuario propio: la base
/// de Mongo se comparte en la clase.
/// </summary>
public abstract class ProfileRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    internal abstract ICareerRepository Careers { get; }

    internal abstract IProfileRepository Profiles { get; }

    /// <summary>Id de la UTTT en esa base (lo genera el script en Mongo).</summary>
    internal abstract Task<string> UtttIdAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProfileOwner NewOwner() => new(AuthIds.New(), AuthIds.New(), "Ana Sofía Ramírez");

    private static string AvatarKey(string userId) => $"media/avatars/{userId}/{Guid.NewGuid():N}.webp";

    [Fact]
    public async Task Careers_OfUttt_AreTheThirtyEightOfTheSeedInOrder()
    {
        var careers = await Careers.ListActiveAsync(await UtttIdAsync(), Ct);

        Assert.Equal(38, careers.Count);
        Assert.Equal("tsu-energias-renovables-solar", careers[0].Slug);
        Assert.Equal("lic-mercadotecnia", careers[^1].Slug);
        Assert.All(careers.Where(c => c.Level == CareerLevels.Tsu), c => Assert.Equal((1, 6), (c.FirstTerm, c.LastTerm)));
        Assert.All(careers.Where(c => c.Level != CareerLevels.Tsu), c => Assert.Equal((7, 11), (c.FirstTerm, c.LastTerm)));
        Assert.Equal(22, careers.Count(c => c.Level == CareerLevels.Tsu));
        Assert.Equal("Ing. Mecatrónica", careers.Single(c => c.Slug == "ing-mecatronica").ShortName);
    }

    [Fact]
    public async Task Careers_OfAnotherCommunity_AreEmpty()
    {
        Assert.Empty(await Careers.ListActiveAsync(AuthIds.New(), Ct));
    }

    [Fact]
    public async Task Find_WithoutProfile_IsNull()
    {
        Assert.Null(await Profiles.FindAsync(AuthIds.New(), Ct));
    }

    [Fact]
    public async Task SetDetails_CreatesTheProfileWithDefaults_AndNullClearsThem()
    {
        var owner = NewOwner();

        var created = await Profiles.SetDetailsAsync(owner, "ing-mecatronica", 7, Now, Ct);
        var cleared = await Profiles.SetDetailsAsync(owner, null, null, Now.AddMinutes(1), Ct);

        Assert.Equal(owner.UserId, created.Id);
        Assert.Equal(owner.CommunityId, created.CommunityId);
        Assert.Equal("Ana Sofía Ramírez", created.FullName);
        Assert.Equal(("ing-mecatronica", 7), (created.CareerSlug, created.Term));
        Assert.Equal((true, true, false), (created.Visibility.Career, created.Visibility.Sales, created.Visibility.Online));
        Assert.Equal((null, 0, 0, 0), (created.Stats.RatingAvg, created.Stats.RatingCount, created.Stats.Sales, created.Stats.Purchases));
        Assert.Null(cleared.CareerSlug);
        Assert.Null(cleared.Term);
        Assert.Equal(Now.AddMinutes(1), cleared.UpdatedAt);
        Assert.Equal(cleared.CareerSlug, (await Profiles.FindAsync(owner.UserId, Ct))!.CareerSlug);
    }

    [Fact]
    public async Task SetVisibility_KeepsTheDetails()
    {
        var owner = NewOwner();
        await Profiles.SetDetailsAsync(owner, "tsu-ti-software", 3, Now, Ct);

        var saved = await Profiles.SetVisibilityAsync(owner, new ProfileVisibility { Career = false, Sales = true, Online = false }, Now, Ct);

        Assert.Equal((false, true, false), (saved.Visibility.Career, saved.Visibility.Sales, saved.Visibility.Online));
        Assert.Equal(("tsu-ti-software", 3), (saved.CareerSlug, saved.Term));
    }

    [Fact]
    public async Task SetVisibility_OnANewProfile_StoresIt()
    {
        var owner = NewOwner();

        await Profiles.SetVisibilityAsync(owner, new ProfileVisibility { Career = false, Sales = false, Online = true }, Now, Ct);

        var found = await Profiles.FindAsync(owner.UserId, Ct);
        Assert.Equal((false, false, true), (found!.Visibility.Career, found.Visibility.Sales, found.Visibility.Online));
        Assert.Null(found.CareerSlug);
    }

    [Fact]
    public async Task SetAvatar_ReturnsThePreviousKey()
    {
        var owner = NewOwner();
        var first = AvatarKey(owner.UserId);
        var second = AvatarKey(owner.UserId);

        var none = await Profiles.SetAvatarAsync(owner, first, Now, Ct);
        var replaced = await Profiles.SetAvatarAsync(owner, second, Now, Ct);
        var removed = await Profiles.SetAvatarAsync(owner, null, Now, Ct);

        Assert.Null(none);
        Assert.Equal(first, replaced);
        Assert.Equal(second, removed);
        Assert.Null((await Profiles.FindAsync(owner.UserId, Ct))!.AvatarKey);
    }

    [Fact]
    public async Task FindMany_ReturnsOnlyExistingProfiles_AndIgnoresBadIds()
    {
        var withPhoto = NewOwner();
        var withoutPhoto = NewOwner();
        var key = AvatarKey(withPhoto.UserId);
        await Profiles.SetAvatarAsync(withPhoto, key, Now, Ct);
        await Profiles.SetDetailsAsync(withoutPhoto, null, 2, Now, Ct);

        var found = await Profiles.FindManyAsync([withPhoto.UserId, withoutPhoto.UserId, withPhoto.UserId, NewOwner().UserId, "no-es-un-id"], Ct);

        Assert.Equal(
            [withPhoto.UserId, withoutPhoto.UserId],
            found.Select(p => p.Id).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.Equal(key, found.Single(p => p.Id == withPhoto.UserId).AvatarKey);
        Assert.Empty(await Profiles.FindManyAsync([], Ct));
    }

    [Fact]
    public async Task Delete_RemovesTheProfile_AndIsIdempotent()
    {
        var owner = NewOwner();
        await Profiles.SetDetailsAsync(owner, null, 2, Now, Ct);

        await Profiles.DeleteAsync(owner.UserId, Ct);
        await Profiles.DeleteAsync(owner.UserId, Ct);

        Assert.Null(await Profiles.FindAsync(owner.UserId, Ct));
    }

    [Fact]
    public async Task AddRating_CreatesTheProfileIfNeeded_AndKeepsTheAverage()
    {
        // HU-27/HU-45: suma, numero, promedio y "Puntual" quedan juntos; el perfil nace con sus valores por defecto.
        var owner = NewOwner();

        await Profiles.AddRatingAsync(owner, 5, onTime: true, Now, Ct);
        await Profiles.AddRatingAsync(owner, 4, onTime: false, Now.AddHours(1), Ct);
        var profile = await Profiles.FindAsync(owner.UserId, Ct);

        Assert.Equal((2, 9.0, 4.5, 1), (profile!.Stats.RatingCount, profile.Stats.RatingSum, profile.Stats.RatingAvg, profile.Stats.OnTimeCount));
        Assert.Equal((owner.FullName, true, 0), (profile.FullName, profile.Visibility.Career, profile.Stats.Sales));
    }

    [Fact]
    public async Task AddTrade_CountsSalesAndPurchases_AndCreatesTheProfileIfNeeded()
    {
        // HU-26: "Ventas" y "Compras" del Perfil.
        var owner = NewOwner();

        await Profiles.AddTradeAsync(owner, asSeller: true, Now, Ct);
        await Profiles.AddTradeAsync(owner, asSeller: true, Now, Ct);
        await Profiles.AddTradeAsync(owner, asSeller: false, Now, Ct);
        var profile = await Profiles.FindAsync(owner.UserId, Ct);

        Assert.Equal((2, 1, 0), (profile!.Stats.Sales, profile.Stats.Purchases, profile.Stats.RatingCount));
        Assert.Equal(owner.FullName, profile.FullName);
    }
}
