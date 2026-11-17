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
/// HU-41: el administrador agrega, renombra, desactiva y vuelve a activar categorias. Las activas son las que salen al
/// publicar productos y solicitudes y en los filtros (tambien en la cache del visitante); una desactivada deja sus
/// publicaciones visibles y con su nombre. Cada prueba usa categorias nuevas: la fabrica se comparte en la clase.
/// </summary>
public sealed class CategoryAdminFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
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

    private static string NewName() => "Ropa " + Guid.NewGuid().ToString("N")[..8];

    private static async Task<JsonElement> CreateAsync(Member admin, string name)
    {
        var response = await admin.Client.PostAsJsonAsync(new Uri("/api/v1/admin/categories", UriKind.Relative), new { name }, Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    private static Task<HttpResponseMessage> PatchAsync(Member admin, string slug, object body) =>
        admin.Client.PatchAsJsonAsync(new Uri($"/api/v1/admin/categories/{slug}", UriKind.Relative), body, Ct);

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await AuthFlow.JsonAsync(response).ConfigureAwait(false)).GetProperty("code").GetString()!;

    /// <summary>"Vender-3" y "Filtrar" del visitante (pasa por la cache de salida).</summary>
    private async Task<IReadOnlyList<string>> PublicSlugsAsync()
    {
        using var anonymous = factory.CreateClient();
        var list = await AuthFlow.JsonAsync(await anonymous.GetAsync(new Uri("/api/v1/categories", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return list.EnumerateArray().Select(c => c.GetProperty("slug").GetString()!).ToList();
    }

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

    private static object Product(string category, string key) =>
        new { title = "Bata de laboratorio talla M", description = "Lavada y planchada.", category, priceCents = 25_000, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { key } };

    private static async Task<string> PhotoAsync(Member seller)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        return (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString()!;
    }

    private static async Task<HttpResponseMessage> PublishAsync(Member seller, string category) =>
        await seller.Client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), Product(category, await PhotoAsync(seller).ConfigureAwait(false)), Ct).ConfigureAwait(false);

    private static Task<HttpResponseMessage> WantedAsync(Member buyer, string category) =>
        buyer.Client.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Bata de laboratorio talla M", category, budgetCents = 30_000, quantity = 1 },
            Ct);

    [Fact]
    public async Task NewCategory_ShowsUpForPublishingAndFilters_RightAway()
    {
        var admin = await AdminAsync();
        var seller = await MemberAsync();
        Assert.DoesNotContain("ropa", await PublicSlugsAsync()); // calienta la cache del visitante

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await CreateAsync(admin, "Ropa  y batás " + suffix);
        var slug = created.GetProperty("slug").GetString()!;

        Assert.Equal("ropa-y-batas-" + suffix, slug);
        Assert.Equal("Ropa y batás " + suffix, created.GetProperty("name").GetString());
        Assert.True(created.GetProperty("active").GetBoolean());
        Assert.Equal(slug, (await PublicSlugsAsync())[^1]);
        Assert.Equal(HttpStatusCode.Created, (await PublishAsync(seller, slug)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await WantedAsync(seller, slug)).StatusCode);
        var list = await AuthFlow.JsonAsync(await admin.Client.GetAsync(new Uri("/api/v1/admin/categories", UriKind.Relative), Ct));
        Assert.Contains(list.GetProperty("items").EnumerateArray(), c => c.GetProperty("slug").GetString() == slug);
    }

    [Fact]
    public async Task Deactivated_LeavesPublishingAndFilters_ButItsProductsKeepTheirName()
    {
        var admin = await AdminAsync();
        var seller = await MemberAsync();
        var slug = (await CreateAsync(admin, NewName())).GetProperty("slug").GetString()!;
        var published = await PublishAsync(seller, slug);
        var productId = (await AuthFlow.JsonAsync(published)).GetProperty("id").GetString()!;
        var newName = "Batas " + Guid.NewGuid().ToString("N")[..8];

        var renamed = await PatchAsync(admin, slug, new { name = newName, active = false });

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var body = await AuthFlow.JsonAsync(renamed);
        Assert.Equal((slug, newName, false), (body.GetProperty("slug").GetString(), body.GetProperty("name").GetString(), body.GetProperty("active").GetBoolean()));
        Assert.DoesNotContain(slug, await PublicSlugsAsync());
        Assert.Equal("listings.invalid_category", await CodeAsync(await PublishAsync(seller, slug)));
        Assert.Equal("wanted.invalid_category", await CodeAsync(await WantedAsync(seller, slug)));

        var detail = await AuthFlow.JsonAsync(await seller.Client.GetAsync(new Uri($"/api/v1/products/{productId}", UriKind.Relative), Ct));
        Assert.Equal(newName, detail.GetProperty("category").GetProperty("name").GetString());

        // Quien ya la usaba puede editar su publicacion sin cambiarla.
        var edit = await seller.Client.PutAsJsonAsync(
            new Uri($"/api/v1/products/{productId}", UriKind.Relative),
            new { title = "Bata de laboratorio talla M", description = "Rebajada.", category = slug, priceCents = 20_000, condition = "used", conditionDetail = "good" },
            Ct);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var back = await PatchAsync(admin, slug, new { active = true });
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Contains(slug, await PublicSlugsAsync());
    }

    [Fact]
    public async Task DeactivatedCategory_CannotBeChosen_ForAnotherProduct()
    {
        var admin = await AdminAsync();
        var seller = await MemberAsync();
        var slug = (await CreateAsync(admin, NewName())).GetProperty("slug").GetString()!;
        var other = (await AuthFlow.JsonAsync(await PublishAsync(seller, "libros"))).GetProperty("id").GetString()!;
        await PatchAsync(admin, slug, new { active = false });

        var moved = await seller.Client.PutAsJsonAsync(
            new Uri($"/api/v1/products/{other}", UriKind.Relative),
            new { title = "Bata de laboratorio talla M", description = "Lavada.", category = slug, priceCents = 20_000, condition = "used", conditionDetail = "good" },
            Ct);

        Assert.Equal("listings.invalid_category", await CodeAsync(moved));
    }

    [Fact]
    public async Task Errors_HaveTheirCodes()
    {
        var admin = await AdminAsync();
        var slug = (await CreateAsync(admin, NewName())).GetProperty("slug").GetString()!;

        var repeated = await admin.Client.PostAsJsonAsync(new Uri("/api/v1/admin/categories", UriKind.Relative), new { name = "LIBROS" }, Ct);
        Assert.Equal((HttpStatusCode.Conflict, "admin.category_exists"), (repeated.StatusCode, await CodeAsync(repeated)));

        var shortName = await admin.Client.PostAsJsonAsync(new Uri("/api/v1/admin/categories", UriKind.Relative), new { name = "x" }, Ct);
        Assert.Equal((HttpStatusCode.BadRequest, "admin.invalid_category_name"), (shortName.StatusCode, await CodeAsync(shortName)));

        var nothing = await PatchAsync(admin, slug, new { });
        Assert.Equal((HttpStatusCode.BadRequest, "admin.no_category_changes"), (nothing.StatusCode, await CodeAsync(nothing)));

        var takenName = await PatchAsync(admin, slug, new { name = "Calculadoras" });
        Assert.Equal((HttpStatusCode.Conflict, "admin.category_exists"), (takenName.StatusCode, await CodeAsync(takenName)));

        var unknown = await PatchAsync(admin, "no-existe", new { active = false });
        Assert.Equal((HttpStatusCode.NotFound, "admin.category_not_found"), (unknown.StatusCode, await CodeAsync(unknown)));
    }

    [Fact]
    public async Task Student_CannotManageCategories()
    {
        var student = await MemberAsync();

        var created = await student.Client.PostAsJsonAsync(new Uri("/api/v1/admin/categories", UriKind.Relative), new { name = NewName() }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PatchAsync(student, "libros", new { active = false })).StatusCode);
        Assert.Contains("libros", await PublicSlugsAsync());
    }
}
