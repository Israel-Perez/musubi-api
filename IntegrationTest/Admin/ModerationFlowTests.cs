using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Admin;
using Musubi.Api.Modules.Auth.Data;
using SkiaSharp;

namespace IntegrationTest.Admin;

/// <summary>
/// HU-39: el administrador ve los reportes con su motivo y lo reportado, retira la publicacion o descarta el reporte,
/// y cada accion queda en la bitacora. HU-40: suspende y reactiva una cuenta; la suspension cierra sus sesiones y
/// pausa sus publicaciones.
/// </summary>
public sealed class ModerationFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Member(HttpClient Client, string Id, string Email);

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
        return new Member(client, login.GetProperty("user").GetProperty("id").GetString()!, email);
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

    private static async Task<string> PublishAsync(Member seller)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString();
        var published = await seller.Client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title = "Calculadora " + Guid.NewGuid().ToString("N")[..6], description = "Original, con caja.", category = "laboratorio", priceCents = 25_000, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { key } },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static async Task<string> ReportAsync(Member reporter, string targetType, string? targetId, string reason = "not_as_described")
    {
        var response = await reporter.Client.PostAsJsonAsync(
            new Uri("/api/v1/reports", UriKind.Relative),
            new { targetType, targetId, reason, details = "Las fotos son de otra tienda." },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await AuthFlow.JsonAsync(response).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> InboxAsync(Member admin, string status = "open") =>
        await AuthFlow.JsonAsync(await admin.Client.GetAsync(new Uri($"/api/v1/admin/reports?status={status}", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);

    private static JsonElement? Find(JsonElement inbox, string reportId) =>
        inbox.GetProperty("items").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(r => r!.Value.GetProperty("id").GetString() == reportId);

    private static Task<HttpResponseMessage> ResolveAsync(Member admin, string reportId, string action, string? note = null) =>
        admin.Client.PostAsJsonAsync(new Uri($"/api/v1/admin/reports/{reportId}/resolution", UriKind.Relative), new { action, note }, Ct);

    private static Task<HttpResponseMessage> SuspendAsync(Member admin, string userId, string? reason = "Pidió pagos fuera de Musubi.") =>
        admin.Client.PostAsJsonAsync(new Uri($"/api/v1/admin/accounts/{userId}/suspension", UriKind.Relative), new { reason }, Ct);

    private static Task<HttpResponseMessage> ReactivateAsync(Member admin, string userId) =>
        admin.Client.DeleteAsync(new Uri($"/api/v1/admin/accounts/{userId}/suspension", UriKind.Relative), Ct);

    private static async Task<JsonElement> AccountAsync(Member admin, string userId) =>
        await AuthFlow.JsonAsync(await admin.Client.GetAsync(new Uri($"/api/v1/admin/accounts/{userId}", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);

    private static async Task<bool> InShowcaseAsync(Member viewer, string productId)
    {
        var page = await AuthFlow.JsonAsync(await viewer.Client.GetAsync(new Uri("/api/v1/products?limit=50", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return page.GetProperty("items").EnumerateArray().Any(p => p.GetProperty("id").GetString() == productId);
    }

    [Fact]
    public async Task Inbox_ShowsTheReasonTheReportedProductAndItsSeller_AndUpholdRemovesIt()
    {
        var admin = await AdminAsync();
        var seller = await MemberAsync();
        var buyer = await MemberAsync();
        var productId = await PublishAsync(seller);
        var reportId = await ReportAsync(buyer, "product", productId);

        var item = Find(await InboxAsync(admin), reportId);

        Assert.NotNull(item);
        var report = item.Value;
        Assert.Equal("not_as_described", report.GetProperty("reason").GetString());
        Assert.Equal("Las fotos son de otra tienda.", report.GetProperty("details").GetString());
        Assert.Equal("open", report.GetProperty("status").GetString());
        Assert.Equal(buyer.Id, report.GetProperty("reporter").GetProperty("id").GetString());
        Assert.Equal("product", report.GetProperty("target").GetProperty("type").GetString());
        Assert.Equal(productId, report.GetProperty("target").GetProperty("id").GetString());
        Assert.StartsWith("Calculadora", report.GetProperty("target").GetProperty("name").GetString(), StringComparison.Ordinal);
        Assert.Equal("active", report.GetProperty("target").GetProperty("status").GetString());
        Assert.Equal(seller.Id, report.GetProperty("subject").GetProperty("id").GetString());
        Assert.Equal(0, report.GetProperty("subject").GetProperty("upheldReports").GetInt64());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("review").ValueKind);
        Assert.True(await InShowcaseAsync(buyer, productId));

        var resolved = await ResolveAsync(admin, reportId, "uphold", "Fotos de internet.");
        var body = await AuthFlow.JsonAsync(resolved);
        var again = await ResolveAsync(admin, reportId, "dismiss");
        var detail = await buyer.Client.GetAsync(new Uri($"/api/v1/products/{productId}", UriKind.Relative), Ct);
        var closed = Find(await InboxAsync(admin, "closed"), reportId);
        var history = (await AccountAsync(admin, seller.Id)).GetProperty("history").EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal("upheld", body.GetProperty("status").GetString());
        Assert.Equal("removed", body.GetProperty("target").GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("subject").GetProperty("upheldReports").GetInt64());
        Assert.Equal(admin.Id, body.GetProperty("review").GetProperty("reviewer").GetProperty("id").GetString());
        Assert.Equal("Fotos de internet.", body.GetProperty("review").GetProperty("note").GetString());
        await AuthFlow.AssertCodeAsync(again, HttpStatusCode.Conflict, "admin.report_closed");
        Assert.False(await InShowcaseAsync(buyer, productId));
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Null(Find(await InboxAsync(admin), reportId));
        Assert.NotNull(closed);
        // RNF-04: queda en la bitacora, en el historial de quien vende.
        Assert.Equal(["product_removed", "report_upheld"], history.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Dismiss_ClosesTheReport_AndTheContentStays()
    {
        var admin = await AdminAsync();
        var seller = await MemberAsync();
        var buyer = await MemberAsync();
        var productId = await PublishAsync(seller);
        var reportId = await ReportAsync(buyer, "product", productId);
        var generalId = await ReportAsync(buyer, "general", null, "other");

        var dismissed = await AuthFlow.JsonAsync(await ResolveAsync(admin, reportId, "dismiss"));
        var general = await AuthFlow.JsonAsync(await ResolveAsync(admin, generalId, "uphold"));

        Assert.Equal("dismissed", dismissed.GetProperty("status").GetString());
        Assert.Equal("active", dismissed.GetProperty("target").GetProperty("status").GetString());
        Assert.Equal(0, dismissed.GetProperty("subject").GetProperty("upheldReports").GetInt64());
        Assert.True(await InShowcaseAsync(buyer, productId));
        Assert.Equal("upheld", general.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, general.GetProperty("subject").ValueKind);
        Assert.Equal("report_dismissed", Assert.Single((await AccountAsync(admin, seller.Id)).GetProperty("history").EnumerateArray()).GetProperty("action").GetString());
    }

    [Fact]
    public async Task ThreeUpheldReports_SuggestSuspending_AndSuspensionClosesSessionsAndPausesListings()
    {
        var admin = await AdminAsync();
        var seller = await MemberAsync();
        var refresh = await flow.SignInAsync(seller.Email);
        var reported = await PublishAsync(seller);
        for (var i = 0; i < ModerationService.SuspensionThreshold; i++)
        {
            // El primero retira la publicacion; los siguientes, ya retirada, solo cuentan como procedentes.
            var reportId = await ReportAsync(await MemberAsync(), "product", reported, "off_platform");
            Assert.Equal(HttpStatusCode.OK, (await ResolveAsync(admin, reportId, "uphold")).StatusCode);
        }

        var other = await MemberAsync();
        var productTwo = await PublishAsync(seller);
        Assert.True(await InShowcaseAsync(other, productTwo));

        var before = await AccountAsync(admin, seller.Id);
        var suspended = await SuspendAsync(admin, seller.Id);
        var body = await AuthFlow.JsonAsync(suspended);
        var twice = await SuspendAsync(admin, seller.Id);
        var login = await flow.LoginAsync(seller.Email);
        var renewed = await flow.RefreshAsync(refresh);
        var listed = await AuthFlow.JsonAsync(await admin.Client.GetAsync(new Uri("/api/v1/admin/accounts/suspended", UriKind.Relative), Ct));

        Assert.Equal(3, before.GetProperty("upheldReports").GetInt64());
        Assert.True(before.GetProperty("suspensionSuggested").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal("suspended", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("suspensionSuggested").GetBoolean());
        var last = body.GetProperty("history")[0];
        Assert.Equal("account_suspended", last.GetProperty("action").GetString());
        Assert.Equal("Pidió pagos fuera de Musubi.", last.GetProperty("reason").GetString());
        Assert.Equal(admin.Id, last.GetProperty("actor").GetProperty("id").GetString());
        await AuthFlow.AssertCodeAsync(twice, HttpStatusCode.Conflict, "admin.account_not_active");
        await AuthFlow.AssertCodeAsync(login, HttpStatusCode.Forbidden, "auth.account_suspended");
        await AuthFlow.AssertCodeAsync(renewed, HttpStatusCode.Unauthorized, "auth.session_expired");
        Assert.False(await InShowcaseAsync(other, productTwo));
        Assert.Contains(listed.GetProperty("items").EnumerateArray(), a => a.GetProperty("id").GetString() == seller.Id);

        var reactivated = await ReactivateAsync(admin, seller.Id);
        var again = await ReactivateAsync(admin, seller.Id);
        var back = await flow.LoginAsync(seller.Email);

        Assert.Equal("active", (await AuthFlow.JsonAsync(reactivated)).GetProperty("status").GetString());
        await AuthFlow.AssertCodeAsync(again, HttpStatusCode.Conflict, "admin.account_not_suspended");
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        // Sus publicaciones siguen pausadas hasta que las reactive desde "Mis productos".
        Assert.False(await InShowcaseAsync(other, productTwo));
        Assert.Equal("account_reactivated", (await AccountAsync(admin, seller.Id)).GetProperty("history")[0].GetProperty("action").GetString());
    }

    [Fact]
    public async Task Suspension_TheAccessTokenAlreadyIssuedStopsWorkingRightAway()
    {
        var admin = await AdminAsync();
        var seller = await MemberAsync();
        var me = new Uri("/api/v1/profiles/me", UriKind.Relative);
        Assert.Equal(HttpStatusCode.OK, (await seller.Client.GetAsync(me, Ct)).StatusCode);
        // SCRUM-120: la revocacion compara milisegundos; la suspension ocurre despues de su inicio de sesion.
        factory.Clock.Advance(TimeSpan.FromMilliseconds(5));

        Assert.Equal(HttpStatusCode.OK, (await SuspendAsync(admin, seller.Id)).StatusCode);

        // Sin esperar los 15 minutos del access token: la lista de revocacion lo corta en la siguiente peticion.
        Assert.Equal(HttpStatusCode.Unauthorized, (await seller.Client.GetAsync(me, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync(me, Ct)).StatusCode);
    }

    [Fact]
    public async Task Suspend_NeedsAReason_AndNeverTheAdminItselfOrAnotherAdmin()
    {
        var admin = await AdminAsync();
        var other = await AdminAsync();
        var member = await MemberAsync();

        await AuthFlow.AssertCodeAsync(await SuspendAsync(admin, member.Id, reason: "  "), HttpStatusCode.BadRequest, "admin.invalid_reason");
        await AuthFlow.AssertCodeAsync(await SuspendAsync(admin, member.Id, reason: new string('x', 501)), HttpStatusCode.BadRequest, "admin.invalid_reason");
        await AuthFlow.AssertCodeAsync(await SuspendAsync(admin, admin.Id), HttpStatusCode.BadRequest, "admin.invalid_account");
        await AuthFlow.AssertCodeAsync(await SuspendAsync(admin, other.Id), HttpStatusCode.BadRequest, "admin.invalid_account");
        await AuthFlow.AssertCodeAsync(await SuspendAsync(admin, AuthIds.New()), HttpStatusCode.NotFound, "admin.account_not_found");
        await AuthFlow.AssertCodeAsync(await ReactivateAsync(admin, member.Id), HttpStatusCode.Conflict, "admin.account_not_suspended");
        Assert.Equal("active", (await AccountAsync(admin, member.Id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Resolve_ValidatesTheActionTheNoteAndTheReport()
    {
        var admin = await AdminAsync();
        var member = await MemberAsync();
        var reportId = await ReportAsync(member, "general", null, "other");

        await AuthFlow.AssertCodeAsync(await ResolveAsync(admin, reportId, "delete"), HttpStatusCode.BadRequest, "admin.invalid_action");
        await AuthFlow.AssertCodeAsync(await ResolveAsync(admin, reportId, "dismiss", new string('x', 501)), HttpStatusCode.BadRequest, "admin.invalid_reason");
        await AuthFlow.AssertCodeAsync(await ResolveAsync(admin, AuthIds.New(), "dismiss"), HttpStatusCode.NotFound, "admin.report_not_found");
        await AuthFlow.AssertCodeAsync(await ResolveAsync(admin, "no-es-un-id", "dismiss"), HttpStatusCode.NotFound, "admin.report_not_found");
        await AuthFlow.AssertCodeAsync(
            await admin.Client.GetAsync(new Uri("/api/v1/admin/reports?status=todos", UriKind.Relative), Ct), HttpStatusCode.BadRequest, "admin.invalid_status");
        Assert.NotNull(Find(await InboxAsync(admin), reportId));
    }

    [Fact]
    public async Task AdminEmails_GiveTheAdminRoleAtLogin()
    {
        var email = AuthFlow.NewEmail();
        // Admin:Emails con otro formato del mismo correo: se compara normalizado.
        using var configured = factory.WithWebHostBuilder(b => b.UseSetting("Admin:Emails:0", email.ToUpperInvariant()));
        using var client = configured.CreateClient();
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Equipo Musubi", password = AuthFlow.Password, acceptTerms = true, acceptPrivacy = true },
            Ct);
        var verified = await AuthFlow.JsonAsync(await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = factory.Emails.LastCodeFor(email) }, Ct));
        var login = await client.PostAsJsonAsync(new Uri("/api/v1/auth/login", UriKind.Relative), new { email, password = AuthFlow.Password }, Ct);
        var session = await AuthFlow.JsonAsync(login);
        var student = await AuthFlow.JsonAsync(await flow.LoginAsync((await flow.RegisterActiveAsync())));

        Assert.Equal(["student", "admin"], verified.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(["student", "admin"], session.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(["student"], student.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Contains(UserRoles.Admin, (await configured.Services.GetRequiredService<IUserRepository>().FindByEmailAsync(email, Ct))!.Roles);
    }
}
