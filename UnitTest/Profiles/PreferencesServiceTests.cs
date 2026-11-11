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
}
