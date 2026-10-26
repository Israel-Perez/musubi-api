using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace IntegrationTest.Auth;

/// <summary>Pasos HTTP repetidos de M-01 (registro + verificacion, login) sobre una <see cref="AuthApiFactory"/>.</summary>
internal sealed class AuthFlow(AuthApiFactory factory)
{
    public const string Password = "musubi2026";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string NewEmail() => $"ana.{Guid.NewGuid():N}@uttt.edu.mx";

    public async Task<HttpResponseMessage> PostAsync(string path, object body)
    {
        using var client = factory.CreateClient();
        return await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct).ConfigureAwait(false);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    public static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response).ConfigureAwait(false)).GetProperty("code").GetString());
    }

    /// <summary>Registra la cuenta sin verificarla (estado pendiente).</summary>
    public async Task<string> RegisterPendingAsync()
    {
        var email = NewEmail();
        var response = await PostAsync(
            "/api/v1/auth/register",
            new { email, fullName = "Ana Sofía Prueba", password = Password, acceptTerms = true, acceptPrivacy = true }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    /// <summary>Registra y verifica la cuenta (estado activo).</summary>
    public async Task<string> RegisterActiveAsync()
    {
        var email = await RegisterPendingAsync().ConfigureAwait(false);
        var confirmed = await PostAsync("/api/v1/auth/verification/confirm", new { email, code = factory.Emails.LastCodeFor(email) }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        return email;
    }

    public Task<HttpResponseMessage> LoginAsync(string email, string password = Password) =>
        PostAsync("/api/v1/auth/login", new { email, password });

    /// <summary>Inicia sesion y devuelve el refresh token.</summary>
    public async Task<string> SignInAsync(string email, string password = Password)
    {
        var response = await LoginAsync(email, password).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await JsonAsync(response).ConfigureAwait(false)).GetProperty("refreshToken").GetString()!;
    }

    public Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        PostAsync("/api/v1/auth/refresh", new { refreshToken });
}
