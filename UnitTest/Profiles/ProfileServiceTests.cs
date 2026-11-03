using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.TestShared;

namespace UnitTest.Profiles;

/// <summary>HU-04: valores por defecto, reglas de carrera y periodo, y lo que el perfil publico oculta (OWASP API3).</summary>
public sealed class ProfileServiceTests
{
    private const string OtherCommunity = "000000000000000000000002";

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryProfileRepository profiles = new();
    private readonly ProfileService service;

    public ProfileServiceTests()
    {
        service = new ProfileService(
            profiles,
            new InMemoryCareerRepository(),
            new MemberDirectory(new InMemoryCommunityRepository(), users),
            new ManualTimeProvider(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero)));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync(string communityId = InMemoryCommunityRepository.UtttId, string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        var user = new UserDocument
        {
            Id = id,
            CommunityId = communityId,
            Email = $"ana.{id}@uttt.edu.mx",
            EmailNormalized = $"ana.{id}@uttt.edu.mx",
            PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
            FullName = "Ana Sofía Ramírez",
            Status = status,
            EmailVerifiedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 10, 1, 8, 55, 0, DateTimeKind.Utc),
        };
        Assert.True(await users.TryInsertAsync(user, Ct).ConfigureAwait(false));
        return id;
    }

    [Fact]
    public async Task Me_WithoutProfile_IsTheDefaults_AndCreatesNothing()
    {
        var id = await MemberAsync();

        var me = (await service.MeAsync(id, Ct)).Value!;

        Assert.Equal(("Ana Sofía Ramírez", "UTTT"), (me.FullName, me.Campus.ShortName));
        Assert.Equal(new AcademicTermResponse("cuatrimestre", 11), me.AcademicTerm);
        Assert.Null(me.Career);
        Assert.Null(me.Term);
        Assert.Null(me.Photo);
        Assert.Equal(new VisibilityResponse(true, true, true), me.Visibility);
        Assert.Equal(new MyStatsResponse(null, 0, 0, 0), me.Stats);
        Assert.Null(await profiles.FindAsync(id, Ct));
    }

    [Fact]
    public async Task Me_OfAPendingAccount_IsForbidden()
    {
        var id = await MemberAsync(status: UserStatuses.PendingVerification);

        Assert.Equal("auth.forbidden", (await service.MeAsync(id, Ct)).Error!.Code);
    }

    [Fact]
    public async Task Update_WithACareerOfTheCampus_ReturnsItsShortName()
    {
        var id = await MemberAsync();

        var me = (await service.UpdateAsync(id, new UpdateProfileRequest("ing-mecatronica", 7), Ct)).Value!;

        Assert.Equal(new CareerResponse("ing-mecatronica", "Ingeniería en Mecatrónica", "Ing. Mecatrónica", 7, 11), me.Career);
        Assert.Equal(7, me.Term);
    }

    [Fact]
    public async Task Update_CareerThatIsNotInTheCampus_IsInvalidCareer()
    {
        var id = await MemberAsync();

        var result = await service.UpdateAsync(id, new UpdateProfileRequest("ing-sistemas", 6), Ct);

        Assert.Equal("profiles.invalid_career", result.Error!.Code);
    }

    [Theory]
    [InlineData("tsu-ti-software", 7)]
    [InlineData("ing-software", 6)]
    [InlineData(null, 12)]
    [InlineData(null, 0)]
    public async Task Update_TermOutsideTheCareerOrTheCampus_IsInvalidTerm(string? career, int term)
    {
        var id = await MemberAsync();

        var result = await service.UpdateAsync(id, new UpdateProfileRequest(career, term), Ct);

        Assert.Equal("profiles.invalid_term", result.Error!.Code);
    }

    [Fact]
    public async Task Update_WithoutCareer_AcceptsUpToTheCampusCount_AndNullClearsBoth()
    {
        var id = await MemberAsync();
        await service.UpdateAsync(id, new UpdateProfileRequest("tsu-ti-software", 3), Ct);

        var onlyTerm = (await service.UpdateAsync(id, new UpdateProfileRequest(null, 11), Ct)).Value!;
        var cleared = (await service.UpdateAsync(id, new UpdateProfileRequest(null, null), Ct)).Value!;

        Assert.Equal((null, 11), (onlyTerm.Career, onlyTerm.Term));
        Assert.Equal((null, (int?)null), (cleared.Career, cleared.Term));
    }

    [Fact]
    public async Task Public_HidesWhatVisibilityTurnsOff()
    {
        var owner = await MemberAsync();
        await service.UpdateAsync(owner, new UpdateProfileRequest("ing-software", 8), Ct);

        var shown = (await service.PublicAsync(InMemoryCommunityRepository.UtttId, owner, Ct)).Value!;
        await service.SetVisibilityAsync(owner, new VisibilityRequest(Career: false, Sales: false, Online: true), Ct);
        var hidden = (await service.PublicAsync(InMemoryCommunityRepository.UtttId, owner, Ct)).Value!;

        Assert.Equal(("ing-software", 8, 0), (shown.Career?.Slug, shown.Term, shown.Stats.Sales));
        Assert.Null(hidden.Career);
        Assert.Null(hidden.Term);
        Assert.Null(hidden.Stats.Sales);
        Assert.Equal("Ana Sofía Ramírez", hidden.FullName);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), hidden.MemberSince);
    }

    [Fact]
    public async Task Public_OfAnotherCampusOrAMissingOrPendingAccount_IsTheSame404()
    {
        var elsewhere = await MemberAsync(OtherCommunity);
        var pending = await MemberAsync(status: UserStatuses.PendingVerification);

        foreach (var id in new[] { elsewhere, pending, AuthIds.New(), "no-es-un-id" })
        {
            Assert.Equal("profiles.not_found", (await service.PublicAsync(InMemoryCommunityRepository.UtttId, id, Ct)).Error!.Code);
        }
    }

    [Fact]
    public async Task Careers_AreTheCampusListWithItsTerm()
    {
        var id = await MemberAsync();

        var careers = (await service.CareersAsync(id, Ct)).Value!;

        Assert.Equal(new AcademicTermResponse("cuatrimestre", 11), careers.AcademicTerm);
        Assert.Equal(38, careers.Careers.Count);
        Assert.Equal(new CareerOption("tsu-ti-software", "TSU en Tecnologías de la Información, área Desarrollo de Software Multiplataforma", "TSU Desarrollo de Software", "tsu", 1, 6), careers.Careers.Single(c => c.Slug == "tsu-ti-software"));
    }
}
