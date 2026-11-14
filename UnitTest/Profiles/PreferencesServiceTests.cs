using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;

namespace UnitTest.Profiles;

public sealed class PreferencesServiceTests
{
    [Theory]
    [InlineData("system")]
    [InlineData("light")]
    [InlineData("dark")]
    public void Validator_AcceptsTheSupportedThemes(string theme) =>
        Assert.True(PreferencesValidators.IsValidTheme(theme));

    [Theory]
    [InlineData("sepia")]
    [InlineData("")]
    [InlineData(null)]
    public void Validator_RejectsUnknownThemes(string? theme) =>
        Assert.False(PreferencesValidators.IsValidTheme(theme));

    [Fact]
    public async Task InMemoryRepository_StoresPreferencesPerUserAndDeletesThem()
    {
        var repository = new InMemoryPreferenceRepository();
        var cancellationToken = TestContext.Current.CancellationToken;
        await repository.SetThemeAsync("user-a", "dark", cancellationToken);
        await repository.SetThemeAsync("user-b", "light", cancellationToken);

        Assert.Equal("dark", (await repository.FindAsync("user-a", cancellationToken))?.Theme);
        Assert.Equal("light", (await repository.FindAsync("user-b", cancellationToken))?.Theme);

        await repository.DeleteAsync("user-a", cancellationToken);

        Assert.Null(await repository.FindAsync("user-a", cancellationToken));
        Assert.Equal("light", (await repository.FindAsync("user-b", cancellationToken))?.Theme);
    }

    [Fact]
    public async Task InMemoryRepository_ThemeAndRecommendationsAreIndependent()
    {
        // HU-35: cambiar el tema no toca la preferencia de recomendaciones, ni al reves.
        var repository = new InMemoryPreferenceRepository();
        var cancellationToken = TestContext.Current.CancellationToken;

        await repository.SetRecommendationsAsync("user-a", false, cancellationToken);
        await repository.SetThemeAsync("user-a", "dark", cancellationToken);
        var saved = await repository.FindAsync("user-a", cancellationToken);
        await repository.SetRecommendationsAsync("user-b", false, cancellationToken);

        Assert.Equal(("dark", false), (saved?.Theme, saved?.Recommendations));
        Assert.Equal("system", (await repository.FindAsync("user-b", cancellationToken))?.Theme);
        Assert.True(new PreferenceDocument().Recommendations);
    }
}
