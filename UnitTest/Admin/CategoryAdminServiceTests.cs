using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Admin;
using Musubi.Api.Modules.Admin.Data;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Errors;
using Musubi.TestShared;
using UnitTest.PurchaseRequests;

namespace UnitTest.Admin;

/// <summary>HU-41: solo un administrador activo cambia categorias, de su campus, y cada cambio va a la bitacora (RNF-04).</summary>
public sealed class CategoryAdminServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 18, 0, 0, DateTimeKind.Utc);
    private const string Uttt = InMemoryCommunityRepository.UtttId;

    private readonly Accounts accounts = new();
    private readonly RecordingLog log = new();
    private readonly InMemoryCategoryRepository repository = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Cuentas fijas: lo unico que usa el servicio es <c>FindAsync</c>.</summary>
    private sealed class Accounts : IAccountModeration
    {
        public Dictionary<string, ModeratedAccount> ById { get; } = [];

        public Task<ModeratedAccount?> FindAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(ById.GetValueOrDefault(userId));

        public Task<IReadOnlyDictionary<string, ModeratedAccount>> FindManyAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ModeratedAccount>> ListSuspendedAsync(string communityId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> SuspendAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> ReactivateAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingLog : IModerationLogRepository
    {
        public List<ModerationLogDocument> Entries { get; } = [];

        public Task InsertAsync(ModerationLogDocument entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ModerationLogDocument>> FindBySubjectAsync(string subjectId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private string Account(bool admin = true, string status = UserStatuses.Active, string? communityId = null)
    {
        var id = AuthIds.New();
        accounts.ById[id] = new ModeratedAccount(id, communityId ?? Uttt, "Equipo Musubi", status, admin, Now, Now);
        return id;
    }

    private CategoryAdminService Service() => new(
        new AdminGate(accounts),
        new CategoryCatalog(repository, new RecordingOutputCacheStore()),
        log,
        new ManualTimeProvider(Now),
        NullLogger<CategoryAdminService>.Instance);

    [Fact]
    public async Task Create_LogsTheNewName()
    {
        var admin = Account();

        var result = await Service().CreateAsync(admin, new CreateCategoryRequest("Ropa y batas"), Ct);

        Assert.Equal(new AdminCategoryResponse("ropa-y-batas", "Ropa y batas", true, 7), result.Value);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(
            (ModerationActions.CategoryCreated, ModerationTargets.Category, admin, "Ropa y batas", Now, Uttt),
            (entry.Action, entry.TargetType, entry.ActorId, entry.Reason, entry.At, entry.CommunityId));
        Assert.Equal((await repository.ListAllAsync(Uttt, Ct))[^1].Id, entry.TargetId);
        Assert.Null(entry.SubjectId);
    }

    [Fact]
    public async Task RenameAndDeactivate_AtOnce_LogTwoActions()
    {
        var result = await Service().UpdateAsync(Account(), "dibujo", new UpdateCategoryRequest("Dibujo técnico", false), Ct);

        Assert.Equal(new AdminCategoryResponse("dibujo", "Dibujo técnico", false, 5), result.Value);
        Assert.Equal(
            [(ModerationActions.CategoryRenamed, "Dibujo"), (ModerationActions.CategoryDeactivated, null)],
            log.Entries.Select(e => (e.Action, e.Reason)));
    }

    [Fact]
    public async Task Reactivate_LogsIt_AndAnUpdateWithoutChanges_LogsNothing()
    {
        var admin = Account();
        await Service().UpdateAsync(admin, "laptops", new UpdateCategoryRequest(null, false), Ct);
        await Service().UpdateAsync(admin, "laptops", new UpdateCategoryRequest(null, true), Ct);
        await Service().UpdateAsync(admin, "laptops", new UpdateCategoryRequest("Laptops", true), Ct);

        Assert.Equal(
            [ModerationActions.CategoryDeactivated, ModerationActions.CategoryReactivated],
            log.Entries.Select(e => e.Action));
    }

    [Fact]
    public async Task Update_WithNothingToChange_IsRejected()
    {
        var result = await Service().UpdateAsync(Account(), "libros", new UpdateCategoryRequest(null, null), Ct);

        Assert.Equal(AdminErrors.NoCategoryChanges, result.Error);
    }

    [Theory]
    [InlineData("Libros", null, "admin.category_exists")]
    [InlineData("x", null, "admin.invalid_category_name")]
    public async Task Update_Errors_HaveTheirCode(string name, bool? active, string code)
    {
        var result = await Service().UpdateAsync(Account(), "dibujo", new UpdateCategoryRequest(name, active), Ct);

        Assert.Equal(code, result.Error!.Code);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Categories_OfAnotherCampus_AreNotSeenOrChanged()
    {
        var other = Account(communityId: AuthIds.New());

        Assert.Empty((await Service().ListAsync(other, Ct)).Value!.Items);
        Assert.Equal(AdminErrors.CategoryNotFound, (await Service().UpdateAsync(other, "libros", new UpdateCategoryRequest(null, false), Ct)).Error);
        Assert.Equal(6, (await repository.ListActiveAsync(Uttt, Ct)).Count);
    }

    [Theory]
    [InlineData(false, UserStatuses.Active)]
    [InlineData(true, UserStatuses.Suspended)]
    public async Task WithoutTheRoleInTheBase_OrSuspended_IsForbidden(bool admin, string status)
    {
        var id = Account(admin, status);

        Assert.Equal(ApiErrors.Forbidden, (await Service().ListAsync(id, Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await Service().CreateAsync(id, new CreateCategoryRequest("Batas"), Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await Service().UpdateAsync(id, "libros", new UpdateCategoryRequest(null, false), Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await Service().ListAsync(AuthIds.New(), Ct)).Error);
        Assert.Empty(log.Entries);
    }
}
