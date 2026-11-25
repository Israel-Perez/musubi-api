using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;

namespace UnitTest.Listings;

/// <summary>
/// HU-59: "Llenar con IA" en <c>Vender-1</c>. Solo con una foto propia ya subida; la IA recibe la foto y las categorias
/// activas del campus, y lo que propone se modera. Sin IA, o si la propuesta no pasa la moderacion, se llena a mano
/// (RNF-15). Nada se guarda (RN-38).
/// </summary>
public sealed class PhotoDraftServiceTests
{
    private static readonly byte[] Photo = [0x52, 0x49, 0x46, 0x46, 0x10, 0x20];

    private readonly InMemoryUserRepository users = new();

    private readonly InMemoryCategoryRepository categories = new();

    private readonly InMemoryMediaStorage media = new();

    private readonly RecordingWriter writer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOwnPhoto_GetsTheProposal_WithTheCampusCategories()
    {
        var diego = await MemberAsync();
        var key = await PhotoAsync(diego);

        var result = await Service().DraftAsync(diego, new PhotoDraftRequest(key), Ct);

        Assert.Equal(
            new PhotoDraftResponse(InMemoryPhotoDraftWriter.Title, "calculadoras", "used", "good", writer.Description),
            result.Value);
        var prompt = Assert.Single(writer.Prompts);
        Assert.Equal(Photo, prompt.Photo.ToArray());
        Assert.Equal(InMemoryCategoryRepository.Seed.Select(c => c.Slug), prompt.Categories.Select(c => c.Slug));
        Assert.Equal("Calculadoras", prompt.Categories[0].Name);
    }

    [Fact]
    public async Task AnInactiveCategory_IsNotOffered()
    {
        var diego = await MemberAsync();
        var key = await PhotoAsync(diego);
        await categories.UpdateAsync(InMemoryCommunityRepository.UtttId, "calculadoras", "Calculadoras", active: false, Ct);

        await Service().DraftAsync(diego, new PhotoDraftRequest(key), Ct);

        Assert.DoesNotContain("calculadoras", writer.Prompts.Single().Categories.Select(c => c.Slug));
    }

    [Fact]
    public async Task SomeoneElsesPhoto_OrAnyOtherKey_IsNotFound_AndNothingLeaves()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var hers = await PhotoAsync(ana);
        var service = Service();

        foreach (var key in new[] { hers, "media/products/../secreto.webp", "chat/abc.webp", "   ", null! })
        {
            var result = await service.DraftAsync(diego, new PhotoDraftRequest(key), Ct);
            Assert.Equal("listings.photo_not_found", result.Error?.Code);
        }

        // Una llave suya con la forma correcta, pero que nunca subio.
        var missing = await service.DraftAsync(diego, new PhotoDraftRequest(ListingService.NewPhotoKey(diego)), Ct);
        Assert.Equal("listings.photo_not_found", missing.Error?.Code);
        Assert.Empty(writer.Prompts);
    }

    [Fact]
    public async Task ASuspendedAccount_IsForbidden()
    {
        var diego = await MemberAsync(UserStatuses.Suspended);
        var key = await PhotoAsync(diego);

        var result = await Service().DraftAsync(diego, new PhotoDraftRequest(key), Ct);

        Assert.Equal(403, result.Error?.Status);
        Assert.Empty(writer.Prompts);
    }

    [Fact]
    public async Task WithoutAi_ItIsFilledByHand_AndThePhotoIsNotRead()
    {
        var diego = await MemberAsync();
        var key = await PhotoAsync(diego);
        writer.Enabled = false;

        var result = await Service().DraftAsync(diego, new PhotoDraftRequest(key), Ct);

        Assert.Equal(("listings.draft_unavailable", 503), (result.Error?.Code, result.Error?.Status));
        Assert.Empty(writer.Prompts);
    }

    [Fact]
    public async Task WhenTheAiDoesNotAnswer_ItIsFilledByHand()
    {
        var diego = await MemberAsync();
        var key = await PhotoAsync(diego);
        writer.Answer = false;

        var result = await Service().DraftAsync(diego, new PhotoDraftRequest(key), Ct);

        Assert.Equal("listings.draft_unavailable", result.Error?.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AProposalThatDoesNotPassModeration_IsNotShown(bool inTheTitle)
    {
        // 02-07: el texto de la IA se modera como si lo hubiera escrito quien vende.
        var diego = await MemberAsync();
        var key = await PhotoAsync(diego);
        writer.Title = inTheTitle ? $"Calculadora {InMemoryContentModerator.RejectMarker}" : InMemoryPhotoDraftWriter.Title;
        writer.Description = inTheTitle ? "Bien." : $"Funciona. {InMemoryContentModerator.RejectMarker}";

        var result = await Service().DraftAsync(diego, new PhotoDraftRequest(key), Ct);

        Assert.Equal("listings.draft_unavailable", result.Error?.Code);
    }

    private PhotoDraftService Service() => new(
        new MemberDirectory(new InMemoryCommunityRepository(), users),
        categories,
        media,
        new InMemoryContentModerator(),
        writer);

    private async Task<string> PhotoAsync(string userId)
    {
        var key = ListingService.NewPhotoKey(userId);
        await media.PutAsync(key, Photo, "image/webp", Ct).ConfigureAwait(false);
        return key;
    }

    private async Task<string> MemberAsync(string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        Assert.True(await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"u.{id}@uttt.edu.mx",
                EmailNormalized = $"u.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Diego Hernández",
                Status = status,
                EmailVerifiedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 10, 1, 8, 55, 0, DateTimeKind.Utc),
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private sealed class RecordingWriter : IPhotoDraftWriter
    {
        public List<PhotoDraftPrompt> Prompts { get; } = [];

        public bool Enabled { get; set; } = true;

        public bool Answer { get; set; } = true;

        public string Title { get; set; } = InMemoryPhotoDraftWriter.Title;

        public string Description { get; set; } = "Calculadora con su tapa.";

        public Task<PhotoDraft?> SuggestAsync(PhotoDraftPrompt prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(Answer ? new PhotoDraft(Title, prompt.Categories[0].Slug, "used", "good", Description) : null);
        }
    }
}
