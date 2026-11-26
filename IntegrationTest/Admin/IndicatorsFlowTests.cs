using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using SkiaSharp;

namespace IntegrationTest.Admin;

/// <summary>
/// HU-42 de punta a punta: el administrador abre "Indicadores de uso" y ve lo que pasa en su campus. Una estudiante
/// nueva publica una calculadora y una solicitud "Se busca", y los indicadores lo cuentan. Nadie mas los ve. La fabrica
/// se comparte en la clase, asi que se mide lo que cambia.
/// </summary>
public sealed class IndicatorsFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Member(HttpClient Client, string Id);

    private void CatchUpClock()
    {
        // El JWT se emite con el reloj de la fabrica, pero JwtBearer lo valida con la hora real.
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }
    }

    private async Task<Member> SignInAsync(string email)
    {
        CatchUpClock();
        var login = await AuthFlow.JsonAsync(await flow.LoginAsync(email).ConfigureAwait(false)).ConfigureAwait(false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return new Member(client, login.GetProperty("user").GetProperty("id").GetString()!);
    }

    private async Task<Member> MemberAsync() => await SignInAsync(await flow.RegisterActiveAsync().ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>Un administrador del campus, como lo deja <c>db/README.md</c> (rol en la base).</summary>
    private async Task<Member> AdminAsync()
    {
        var email = AuthFlow.NewEmail();
        var community = await factory.Services.GetRequiredService<ICommunityRepository>().FindByEmailDomainAsync("uttt.edu.mx", Ct).ConfigureAwait(false);
        var now = factory.Clock.Now.UtcDateTime;
        await factory.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = AuthIds.New(),
                CommunityId = community!.Id,
                Email = email,
                EmailNormalized = email,
                PasswordHash = factory.Services.GetRequiredService<Musubi.Api.Modules.Auth.PasswordHashing>().Hash(AuthFlow.Password),
                FullName = "Equipo Musubi",
                Status = UserStatuses.Active,
                EmailVerifiedAt = now,
                Roles = [UserRoles.Student, UserRoles.Admin],
                CreatedAt = now,
                UpdatedAt = now,
            },
            Ct).ConfigureAwait(false);
        return await SignInAsync(email).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, JsonElement>> IndicatorsAsync(Member admin, string query = "")
    {
        var response = await admin.Client.GetAsync(new Uri($"/api/v1/admin/indicators{query}", UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await AuthFlow.JsonAsync(response).ConfigureAwait(false);
        return page.GetProperty("areas").EnumerateArray().SelectMany(a => a.GetProperty("items").EnumerateArray()).ToDictionary(i => i.GetProperty("key").GetString()!);
    }

    private static double Value(Dictionary<string, JsonElement> all, string key) => all[key].GetProperty("value").GetDouble();

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Teal);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task TheAdmin_SeesWhatHappensOnTheCampus_AndNobodyElse()
    {
        var admin = await AdminAsync();
        var before = await IndicatorsAsync(admin);
        var ana = await MemberAsync();

        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var photo = await AuthFlow.JsonAsync(await ana.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct));
        var published = await ana.Client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title = "Calculadora Casio fx-991", category = "calculadoras", priceCents = 45_000, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { photo.GetProperty("key").GetString() } },
            Ct);
        var wanted = await ana.Client.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Bata de laboratorio talla M", category = "laboratorio", budgetCents = 30_000, quantity = 1 },
            Ct);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        Assert.Equal(HttpStatusCode.Created, wanted.StatusCode);

        var after = await IndicatorsAsync(admin);
        var month = await IndicatorsAsync(admin, "?period=month");
        var theirs = await ana.Client.GetAsync(new Uri("/api/v1/admin/indicators", UriKind.Relative), Ct);

        Assert.Equal(1, Value(after, "users_registered") - Value(before, "users_registered"));
        Assert.Equal(1, Value(after, "products_created") - Value(before, "products_created"));
        Assert.Equal(1, Value(after, "products_active") - Value(before, "products_active"));
        Assert.Equal(1, Value(after, "requests_created") - Value(before, "requests_created"));
        Assert.Contains(after["products_created"].GetProperty("breakdown").EnumerateArray(), b => b.GetProperty("label").GetString() == "Calculadoras");
        Assert.Contains(after["requests_created"].GetProperty("breakdown").EnumerateArray(), b => b.GetProperty("label").GetString() == "Laboratorio");
        Assert.True(Value(after, "users_active") >= 1);
        Assert.True(Value(month, "products_created") >= Value(after, "products_created"));
        Assert.Equal(HttpStatusCode.Forbidden, theirs.StatusCode);
    }
}
