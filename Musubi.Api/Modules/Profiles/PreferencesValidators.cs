namespace Musubi.Api.Modules.Profiles;

internal static class PreferencesValidators
{
    public static bool IsValidTheme(string? theme) =>
        theme is "system" or "light" or "dark";
}
