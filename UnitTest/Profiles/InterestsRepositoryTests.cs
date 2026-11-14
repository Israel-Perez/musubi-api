using Musubi.Api.Modules.Profiles.Data;

namespace UnitTest.Profiles;

/// <summary>HU-66: los intereses comparten documento con el tema (HU-54) sin pisarse.</summary>
public sealed class InterestsRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Interests_WithoutDocument_CreateItWithTheSystemTheme()
    {
        var repository = new InMemoryPreferenceRepository();

        var saved = await repository.SetInterestsAsync("user-a", ["libros", "laptops"], Ct);

        Assert.Equal("system", saved.Theme);
        Assert.Equal(["libros", "laptops"], saved.Interests);
        Assert.Equal(["libros", "laptops"], (await repository.FindAsync("user-a", Ct))?.Interests);
    }

    [Fact]
    public async Task ThemeAndInterests_KeepEachOther()
    {
        var repository = new InMemoryPreferenceRepository();
        await repository.SetThemeAsync("user-a", "dark", Ct);
        await repository.SetInterestsAsync("user-a", ["dibujo"], Ct);
        await repository.SetThemeAsync("user-a", "light", Ct);

        var preference = await repository.FindAsync("user-a", Ct);

        Assert.Equal("light", preference?.Theme);
        Assert.Equal(["dibujo"], preference?.Interests);
    }

    [Fact]
    public async Task Interests_ArePerUser_AndLeaveWithTheAccount()
    {
        var repository = new InMemoryPreferenceRepository();
        await repository.SetInterestsAsync("user-a", ["libros"], Ct);
        await repository.SetInterestsAsync("user-b", ["electronica"], Ct);

        await repository.DeleteAsync("user-a", Ct);

        Assert.Null(await repository.FindAsync("user-a", Ct));
        Assert.Equal(["electronica"], (await repository.FindAsync("user-b", Ct))?.Interests);
    }

    [Fact]
    public async Task SavedInterests_DoNotChangeWhenTheCallerReusesItsList()
    {
        var repository = new InMemoryPreferenceRepository();
        var chosen = new List<string> { "libros" };
        await repository.SetInterestsAsync("user-a", chosen, Ct);

        chosen.Add("laptops");

        Assert.Equal(["libros"], (await repository.FindAsync("user-a", Ct))?.Interests);
    }
}
