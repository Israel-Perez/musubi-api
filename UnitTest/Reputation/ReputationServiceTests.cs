using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared;

namespace UnitTest.Reputation;

/// <summary>
/// HU-28: que se puede reportar y que no (motivo, detalle, otro campus, uno mismo, conversacion ajena) y el aviso
/// al equipo. HU-57: bloquear (idempotente, mismo campus) y "Usuarios bloqueados" con su foto.
/// </summary>
public sealed class ReputationServiceTests
{
    private const string OtherCommunity = "000000000000000000000002";

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryReportRepository reports = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly InMemoryProfileRepository profiles = new();
    private readonly FakeProducts products = new();
    private readonly FakeConversations conversations = new();
    private readonly RecordingEmailSender email = new();
    private readonly ReportOptions options = new() { NotifyEmail = "reportes@musubiapp.com.mx" };
    private readonly ReportService reportService;
    private readonly BlockService blockService;

    public ReputationServiceTests()
    {
        var members = new MemberDirectory(new InMemoryCommunityRepository(), users);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
        reportService = new ReportService(reports, members, products, conversations, email, Options.Create(options), NullLogger<ReportService>.Instance, time);
        blockService = new BlockService(blocks, members, new ProfileDirectory(profiles, new InMemoryCareerRepository()), time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync(string communityId = InMemoryCommunityRepository.UtttId, string status = UserStatuses.Active, string name = "Diego Hernández")
    {
        var id = AuthIds.New();
        Assert.True(await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = communityId,
                Email = $"diego.{id}@uttt.edu.mx",
                EmailNormalized = $"diego.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = name,
                Status = status,
                EmailVerifiedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 10, 1, 8, 55, 0, DateTimeKind.Utc),
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    [Fact]
    public async Task Report_AUser_IsSavedOpen_WithTheTrimmedDetail_AndTheTeamIsEmailed()
    {
        var me = await MemberAsync();
        var other = await MemberAsync();

        var result = await reportService.CreateAsync(me, new CreateReportRequest("user", other, null, "off_platform", "  Me pidió depositarle antes.  "), Ct);

        var saved = Assert.Single(await reports.FindByReporterAsync(me, Ct));
        Assert.Equal(saved.Id, result.Value!.Id);
        Assert.Equal((ReportTargetTypes.User, other, "off_platform", "Me pidió depositarle antes.", ReportStatuses.Open), (saved.Target.Type, saved.Target.Id, saved.Reason, saved.Details, saved.Status));
        var sent = Assert.Single(email.Sent);
        Assert.Equal("reportes@musubiapp.com.mx", sent.To);
        Assert.Contains(saved.Id, sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("Me pidió depositarle antes.", sent.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_General_FromSettings_NeedsNoTarget_AndAnEmptyDetailIsOmitted()
    {
        var me = await MemberAsync();

        var result = await reportService.CreateAsync(me, new CreateReportRequest("general", null, null, "other", "   "), Ct);

        Assert.Null(result.Error);
        var saved = Assert.Single(await reports.FindByReporterAsync(me, Ct));
        Assert.Equal((ReportTargetTypes.General, (string?)null, (string?)null), (saved.Target.Type, saved.Target.Id, saved.Details));
    }

    [Fact]
    public async Task Report_WithoutNotifyEmail_IsStillSaved()
    {
        options.NotifyEmail = string.Empty;
        var me = await MemberAsync();

        var result = await reportService.CreateAsync(me, new CreateReportRequest("general", null, null, "payment", null), Ct);

        Assert.Null(result.Error);
        Assert.Empty(email.Sent);
        Assert.Single(await reports.FindByReporterAsync(me, Ct));
    }

    [Theory]
    [InlineData("spam")]
    [InlineData("")]
    [InlineData("PAYMENT")]
    public async Task Report_WithAReasonOutsideTheMockup_IsRejected(string reason)
    {
        var result = await reportService.CreateAsync(await MemberAsync(), new CreateReportRequest("general", null, null, reason, null), Ct);

        Assert.Equal(ReputationErrors.InvalidReason, result.Error);
    }

    [Fact]
    public async Task Report_WithMoreThan1000Characters_IsRejected()
    {
        var result = await reportService.CreateAsync(await MemberAsync(), new CreateReportRequest("general", null, null, "other", new string('a', 1001)), Ct);

        Assert.Equal(ReputationErrors.InvalidDetails, result.Error);
    }

    [Fact]
    public async Task Report_Yourself_AnUnknownType_OrAGeneralWithATarget_IsAnInvalidTarget()
    {
        var me = await MemberAsync();

        Assert.Equal(ReputationErrors.InvalidTarget, (await reportService.CreateAsync(me, new CreateReportRequest("user", me, null, "other", null), Ct)).Error);
        Assert.Equal(ReputationErrors.InvalidTarget, (await reportService.CreateAsync(me, new CreateReportRequest("message", AuthIds.New(), null, "other", null), Ct)).Error);
        Assert.Equal(ReputationErrors.InvalidTarget, (await reportService.CreateAsync(me, new CreateReportRequest("general", AuthIds.New(), null, "other", null), Ct)).Error);
        Assert.Equal(ReputationErrors.InvalidTarget, (await reportService.CreateAsync(me, new CreateReportRequest("user", null, null, "other", null), Ct)).Error);
    }

    [Fact]
    public async Task Report_AUserOfAnotherCampus_OrMissing_IsTheSame404()
    {
        var me = await MemberAsync();
        var elsewhere = await MemberAsync(OtherCommunity);

        foreach (var target in new[] { elsewhere, AuthIds.New(), "no-es-un-id" })
        {
            Assert.Equal(ReputationErrors.TargetNotFound, (await reportService.CreateAsync(me, new CreateReportRequest("user", target, null, "no_show", null), Ct)).Error);
        }

        Assert.Empty(await reports.FindByReporterAsync(me, Ct));
    }

    [Fact]
    public async Task Report_FromAConversation_OnlyIfBothParticipate()
    {
        var me = await MemberAsync();
        var other = await MemberAsync();
        var mine = AuthIds.New();
        var foreign = AuthIds.New();
        conversations.Items[mine] = [me, other];
        conversations.Items[foreign] = [other, AuthIds.New()];

        var ok = await reportService.CreateAsync(me, new CreateReportRequest("user", other, mine, "payment", null), Ct);
        var notMine = await reportService.CreateAsync(me, new CreateReportRequest("user", other, foreign, "payment", null), Ct);
        var missing = await reportService.CreateAsync(me, new CreateReportRequest("user", other, AuthIds.New(), "payment", null), Ct);

        Assert.Null(ok.Error);
        Assert.Equal(mine, Assert.Single(await reports.FindByReporterAsync(me, Ct)).ConversationId);
        Assert.Equal(ReputationErrors.TargetNotFound, notMine.Error);
        Assert.Equal(ReputationErrors.TargetNotFound, missing.Error);
    }

    [Fact]
    public async Task Report_AProduct_OfMyCampusButNotMine()
    {
        var me = await MemberAsync();
        var seller = await MemberAsync();
        var theirs = new ProductSummary(AuthIds.New(), InMemoryCommunityRepository.UtttId, seller, "Calculadora", 45_000, "used", "good", null, true);
        var own = theirs with { Id = AuthIds.New(), SellerId = me };
        var elsewhere = theirs with { Id = AuthIds.New(), CommunityId = OtherCommunity };
        foreach (var p in new[] { theirs, own, elsewhere })
        {
            products.Items[p.Id] = p;
        }

        Assert.Null((await reportService.CreateAsync(me, new CreateReportRequest("product", theirs.Id, null, "not_as_described", null), Ct)).Error);
        Assert.Equal(ReputationErrors.InvalidTarget, (await reportService.CreateAsync(me, new CreateReportRequest("product", own.Id, null, "not_as_described", null), Ct)).Error);
        Assert.Equal(ReputationErrors.TargetNotFound, (await reportService.CreateAsync(me, new CreateReportRequest("product", elsewhere.Id, null, "not_as_described", null), Ct)).Error);
    }

    [Fact]
    public async Task Report_ByASuspendedAccount_IsForbidden()
    {
        var result = await reportService.CreateAsync(await MemberAsync(status: UserStatuses.Suspended), new CreateReportRequest("general", null, null, "other", null), Ct);

        Assert.Equal("auth.forbidden", result.Error?.Code);
    }

    [Fact]
    public async Task Block_TwiceIsIdempotent_AndTheListHasTheNameAndPhoto()
    {
        var me = await MemberAsync();
        var other = await MemberAsync(name: "Diego Hernández");
        var key = ProfileService.NewAvatarKey(other);
        await profiles.SetAvatarAsync(new ProfileOwner(other, InMemoryCommunityRepository.UtttId, "Diego Hernández"), key, DateTime.UtcNow, Ct);

        Assert.Null((await blockService.BlockAsync(me, new BlockRequest(other), Ct)).Error);
        Assert.Null((await blockService.BlockAsync(me, new BlockRequest(other), Ct)).Error);

        var list = await blockService.ListAsync(me, Ct);
        var item = Assert.Single(list.Items);
        Assert.Equal((other, "Diego Hernández", "/" + key, new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc)), (item.Id, item.Name, item.Photo, item.BlockedAt));
        Assert.Empty((await blockService.ListAsync(other, Ct)).Items);
    }

    [Fact]
    public async Task Block_Yourself_OrSomeoneOfAnotherCampus_OrMissing_IsRejected()
    {
        var me = await MemberAsync();
        var elsewhere = await MemberAsync(OtherCommunity);

        Assert.Equal(ReputationErrors.BlockSelf, (await blockService.BlockAsync(me, new BlockRequest(me), Ct)).Error);
        Assert.Equal(ReputationErrors.UserNotFound, (await blockService.BlockAsync(me, new BlockRequest(elsewhere), Ct)).Error);
        Assert.Equal(ReputationErrors.UserNotFound, (await blockService.BlockAsync(me, new BlockRequest(AuthIds.New()), Ct)).Error);
        Assert.Equal(ReputationErrors.UserNotFound, (await blockService.BlockAsync(me, new BlockRequest("no-es-un-id"), Ct)).Error);
        Assert.Empty((await blockService.ListAsync(me, Ct)).Items);
    }

    [Fact]
    public async Task Unblock_RemovesIt_AndIsIdempotent()
    {
        var me = await MemberAsync();
        var other = await MemberAsync();
        await blockService.BlockAsync(me, new BlockRequest(other), Ct);

        await blockService.UnblockAsync(me, other, Ct);
        await blockService.UnblockAsync(me, other, Ct);

        Assert.Empty((await blockService.ListAsync(me, Ct)).Items);
        Assert.False(await new BlockDirectory(blocks).BetweenAsync(me, other, Ct));
    }

    private sealed class FakeProducts : IProductDirectory
    {
        public Dictionary<string, ProductSummary> Items { get; } = new(StringComparer.Ordinal);

        public Task<ProductSummary?> FindAsync(string productId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(productId));
    }

    private sealed class FakeConversations : IConversationDirectory
    {
        public Dictionary<string, IReadOnlyList<string>> Items { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<string>?> FindParticipantsAsync(string conversationId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(conversationId));

        public Task<ConversationSummary?> FindAsync(string conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<ConversationSummary?>(null);
    }
}
