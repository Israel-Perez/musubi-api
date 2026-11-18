using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Admin;
using Musubi.Api.Modules.Admin.Data;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;
using UnitTest.Common;
using UnitTest.Listings;
using UnitTest.PurchaseRequests;

namespace UnitTest.Admin;

/// <summary>
/// HU-39 y HU-40 (SCRUM-120: faltaban sus pruebas unitarias): <see cref="ModerationService"/> sobre los repositorios en
/// memoria. Solo un administrador activo, solo su campus, cada decision cierra el reporte una vez, la bitacora
/// registra cada accion y suspender cierra las sesiones (refresh y access token) y oculta las publicaciones.
/// </summary>
public sealed class ModerationServiceTests
{
    private const string Campus = InMemoryCommunityRepository.UtttId;
    private const string OtherCampus = "000000000000000000000099";

    private readonly ListingsHarness listings = new();
    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryRefreshTokenRepository tokens = new();
    private readonly InMemoryReportRepository reports = new();
    private readonly InMemoryModerationLogRepository log = new();
    private readonly RecordingNotificationCenter notifications = new();
    private readonly RecordingRevocations revocations = new();
    private readonly ModerationService service;

    public ModerationServiceTests()
    {
        var accounts = new AccountModeration(users, tokens, revocations, listings.Time);
        service = new ModerationService(
            new AdminGate(accounts),
            accounts,
            new ReportInbox(reports),
            new ProductModeration(listings.Products, new RecordingOutputCacheStore(), listings.Time),
            log,
            notifications,
            listings.Time,
            NullLogger<ModerationService>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> UserAsync(bool admin = false, string community = Campus, string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        var now = listings.Time.GetUtcNow().UtcDateTime;
        await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = community,
                Email = id + "@uttt.edu.mx",
                EmailNormalized = id + "@uttt.edu.mx",
                FullName = admin ? "Equipo Musubi" : "Valeria Campos",
                Status = status,
                EmailVerifiedAt = now,
                Roles = admin ? [UserRoles.Student, UserRoles.Admin] : [UserRoles.Student],
                CreatedAt = now,
                UpdatedAt = now,
            },
            Ct).ConfigureAwait(false);
        return id;
    }

    private async Task<string> ReportAsync(string reporterId, string type, string? targetId, string community = Campus)
    {
        var report = new ReportDocument
        {
            Id = AuthIds.New(),
            CommunityId = community,
            ReporterId = reporterId,
            Target = new ReportTarget { Type = type, Id = targetId },
            Reason = ReportReasons.OffPlatform,
            CreatedAt = listings.Time.GetUtcNow().UtcDateTime,
        };
        await reports.InsertAsync(report, Ct).ConfigureAwait(false);
        return report.Id;
    }

    [Fact]
    public async Task OnlyAnActiveAdmin_CanUseAnyAction()
    {
        var student = await UserAsync();
        var target = await UserAsync();

        Assert.Equal(ApiErrors.Forbidden, (await service.ListReportsAsync(student, null, Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await service.ResolveAsync(student, "x", new ResolveReportRequest("dismiss", null), Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await service.ListSuspendedAsync(student, Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await service.GetAccountAsync(student, target, Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await service.SuspendAsync(student, target, new SuspendAccountRequest("Motivo"), Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await service.ReactivateAsync(student, target, Ct)).Error);
    }

    [Fact]
    public async Task ListReports_OnlyTheAdminsCampus_AndAKnownStatus()
    {
        var admin = await UserAsync(admin: true);
        var reporter = await UserAsync();
        var mine = await ReportAsync(reporter, ReportTargetTypes.General, null);
        await ReportAsync(reporter, ReportTargetTypes.General, null, OtherCampus);

        var open = (await service.ListReportsAsync(admin, null, Ct)).Value!;
        var closed = (await service.ListReportsAsync(admin, "closed", Ct)).Value!;

        Assert.Equal([mine], open.Items.Select(r => r.Id));
        Assert.Empty(closed.Items);
        Assert.Equal(AdminErrors.InvalidStatus, (await service.ListReportsAsync(admin, "todos", Ct)).Error);
    }

    [Fact]
    public async Task Resolve_ValidatesTheActionTheNoteAndTheReport()
    {
        var admin = await UserAsync(admin: true);
        var reporter = await UserAsync();
        var report = await ReportAsync(reporter, ReportTargetTypes.General, null);
        var elsewhere = await ReportAsync(reporter, ReportTargetTypes.General, null, OtherCampus);

        Assert.Equal(AdminErrors.InvalidAction, (await service.ResolveAsync(admin, report, new ResolveReportRequest("borrar", null), Ct)).Error);
        Assert.Equal(AdminErrors.InvalidReason, (await service.ResolveAsync(admin, report, new ResolveReportRequest("dismiss", new string('x', ModerationService.MaxReasonLength + 1)), Ct)).Error);
        Assert.Equal(AdminErrors.ReportNotFound, (await service.ResolveAsync(admin, AuthIds.New(), new ResolveReportRequest("dismiss", null), Ct)).Error);
        // Otro campus es como si no existiera (R1).
        Assert.Equal(AdminErrors.ReportNotFound, (await service.ResolveAsync(admin, elsewhere, new ResolveReportRequest("dismiss", null), Ct)).Error);
    }

    [Fact]
    public async Task Dismiss_ClosesItOnce_LogsIt_AndThanksTheReporter()
    {
        var admin = await UserAsync(admin: true);
        var reporter = await UserAsync();
        var person = await UserAsync();
        var report = await ReportAsync(reporter, ReportTargetTypes.User, person);

        var dismissed = (await service.ResolveAsync(admin, report, new ResolveReportRequest("dismiss", "  Sin pruebas.  "), Ct)).Value!;
        var again = await service.ResolveAsync(admin, report, new ResolveReportRequest("uphold", null), Ct);

        Assert.Equal(ReportStatuses.Dismissed, dismissed.Status);
        Assert.Equal(AdminErrors.ReportClosed, again.Error);
        var entry = Assert.Single(await log.FindBySubjectAsync(person, 10, Ct));
        Assert.Equal((ModerationActions.ReportDismissed, admin, "Sin pruebas."), (entry.Action, entry.ActorId, entry.Reason));
        var notice = Assert.Single(notifications.Sent);
        Assert.Equal((reporter, NotificationTypes.ReportReviewed), (notice.UserId, notice.Type));
    }

    [Fact]
    public async Task UpholdAProduct_RemovesIt_AndTellsTheSeller()
    {
        var admin = await UserAsync(admin: true);
        var reporter = await UserAsync();
        var seller = await UserAsync();
        var product = await listings.ProductAsync(seller);
        var report = await ReportAsync(reporter, ReportTargetTypes.Product, product.Id);

        var upheld = (await service.ResolveAsync(admin, report, new ResolveReportRequest("uphold", null), Ct)).Value!;

        Assert.Equal(ReportStatuses.Upheld, upheld.Status);
        Assert.Equal(ProductStatuses.Removed, (await listings.Products.FindByIdAsync(product.Id, Ct))!.Status);
        Assert.Equal(
            [ModerationActions.ProductRemoved, ModerationActions.ReportUpheld],
            (await log.FindBySubjectAsync(seller, 10, Ct)).Select(e => e.Action).Order(StringComparer.Ordinal));
        Assert.Contains(notifications.Sent, n => n.UserId == seller && n.Type == NotificationTypes.ProductRemoved);
        Assert.Contains(notifications.Sent, n => n.UserId == reporter && n.Type == NotificationTypes.ReportReviewed);
    }

    [Fact]
    public async Task Suspend_NeedsAReason_AndNeverTheAdminAnotherAdminOrAnotherCampus()
    {
        var admin = await UserAsync(admin: true);
        var otherAdmin = await UserAsync(admin: true);
        var elsewhere = await UserAsync(community: OtherCampus);
        var student = await UserAsync();

        Assert.Equal(AdminErrors.InvalidReason, (await service.SuspendAsync(admin, student, new SuspendAccountRequest("   "), Ct)).Error);
        Assert.Equal(AdminErrors.InvalidAccount, (await service.SuspendAsync(admin, admin, new SuspendAccountRequest("Motivo"), Ct)).Error);
        Assert.Equal(AdminErrors.InvalidAccount, (await service.SuspendAsync(admin, otherAdmin, new SuspendAccountRequest("Motivo"), Ct)).Error);
        Assert.Equal(AdminErrors.AccountNotFound, (await service.SuspendAsync(admin, elsewhere, new SuspendAccountRequest("Motivo"), Ct)).Error);
        Assert.Empty(revocations.Users);
    }

    [Fact]
    public async Task Suspend_ClosesEverySession_HidesTheListings_AndLogsIt()
    {
        var admin = await UserAsync(admin: true);
        var seller = await UserAsync();
        var product = await listings.ProductAsync(seller);

        var suspended = (await service.SuspendAsync(admin, seller, new SuspendAccountRequest("Pidió pagos fuera de Musubi."), Ct)).Value!;
        var twice = await service.SuspendAsync(admin, seller, new SuspendAccountRequest("Otra vez"), Ct);
        var listed = (await service.ListSuspendedAsync(admin, Ct)).Value!;

        Assert.Equal(UserStatuses.Suspended, suspended.Status);
        Assert.Equal(("account_suspended", "Pidió pagos fuera de Musubi."), (suspended.History[0].Action, suspended.History[0].Reason));
        // SCRUM-120: ademas del refresh token, el access token que ya tenia deja de servir.
        Assert.Equal([seller], revocations.Users);
        Assert.NotEqual(ProductStatuses.Active, (await listings.Products.FindByIdAsync(product.Id, Ct))!.Status);
        Assert.Equal(AdminErrors.AccountNotActive, twice.Error);
        Assert.Equal([seller], listed.Items.Select(a => a.Id));
    }

    [Fact]
    public async Task Reactivate_OnlyASuspendedAccount_AndItIsLogged()
    {
        var admin = await UserAsync(admin: true);
        var student = await UserAsync();

        var notSuspended = await service.ReactivateAsync(admin, student, Ct);
        await service.SuspendAsync(admin, student, new SuspendAccountRequest("Motivo"), Ct);
        var reactivated = (await service.ReactivateAsync(admin, student, Ct)).Value!;
        var account = (await service.GetAccountAsync(admin, student, Ct)).Value!;

        Assert.Equal(AdminErrors.AccountNotSuspended, notSuspended.Error);
        Assert.Equal(UserStatuses.Active, reactivated.Status);
        Assert.Equal(ModerationActions.AccountReactivated, account.History[0].Action);
        Assert.Empty((await service.ListSuspendedAsync(admin, Ct)).Value!.Items);
    }

    [Fact]
    public async Task ThreeUpheldReports_SuggestSuspending()
    {
        var admin = await UserAsync(admin: true);
        var person = await UserAsync();
        for (var i = 0; i < ModerationService.SuspensionThreshold; i++)
        {
            var report = await ReportAsync(await UserAsync(), ReportTargetTypes.User, person);
            Assert.Null((await service.ResolveAsync(admin, report, new ResolveReportRequest("uphold", null), Ct)).Error);
        }

        var account = (await service.GetAccountAsync(admin, person, Ct)).Value!;

        Assert.Equal(ModerationService.SuspensionThreshold, account.UpheldReports);
        Assert.True(account.SuspensionSuggested);
    }

    private sealed class RecordingRevocations : IAccessTokenRevocations
    {
        public List<string> Users { get; } = [];

        public Task RevokeTokenAsync(string tokenId, DateTime expiresAt, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RevokeUserAsync(string userId, CancellationToken cancellationToken)
        {
            Users.Add(userId);
            return Task.CompletedTask;
        }

        public Task<bool> IsRevokedAsync(string? tokenId, string? userId, long issuedAtMs, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
