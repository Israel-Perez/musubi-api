using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.GenAi.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-32/33/34 / RNF-03 / OWASP API1, API4 y RNF-06: el acuerdo exige sesion; solo sus partes lo ven o lo tocan (a
/// cualquier otra persona, 404 sin cambios); "Resumir acuerdo" tiene un tope por persona; sin llave de Gemini no sale
/// nada del servidor y los cuerpos tienen su tope.
/// </summary>
public sealed class AgreementsAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string AnyId = "6716a0000000000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Routes() => new()
    {
        { "POST", "/api/v1/agreements" },
        { "GET", "/api/v1/agreements?conversationId=" + AnyId },
        { "PUT", "/api/v1/agreements/" + AnyId },
        { "POST", "/api/v1/agreements/" + AnyId + "/confirm" },
        { "DELETE", "/api/v1/agreements/" + AnyId },
    };

    private IAgreementRepository Agreements => production.Services.GetRequiredService<IAgreementRepository>();

    private static HttpRequestMessage Request(string method, string path, object? body = null) =>
        new(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = method is "POST" or "PUT" ? JsonContent.Create(body ?? new { revision = 1 }) : null,
        };

    private async Task<string> MemberAsync(string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        Assert.True(await production.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"ana.{id}@uttt.edu.mx",
                EmailNormalized = $"ana.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Ana Sofía Ramírez",
                Status = status,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private async Task<string> ConversationAsync(string buyer, string seller)
    {
        var (conversation, _) = await production.Services.GetRequiredService<IConversationRepository>().InsertOrGetAsync(
            new ConversationDocument
            {
                Id = AuthIds.New(),
                CommunityId = InMemoryCommunityRepository.UtttId,
                BuyerId = buyer,
                SellerId = seller,
                Participants = [buyer, seller],
                Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = AuthIds.New(), Title = "Calculadora" },
                LastMessageAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false);
        return conversation.Id;
    }

    private async Task<AgreementDocument> AgreementAsync(string buyer, string seller)
    {
        var now = DateTime.UtcNow;
        return await Agreements.ReplaceForConversationAsync(
            new AgreementDocument
            {
                Id = AuthIds.New(),
                CommunityId = InMemoryCommunityRepository.UtttId,
                ConversationId = await ConversationAsync(buyer, seller).ConfigureAwait(false),
                BuyerId = buyer,
                SellerId = seller,
                RequestedBy = buyer,
                Proposal = new AgreementProposal { Title = "Calculadora", PriceCents = 50_000, Quantity = 1 },
                NotIdentified = [AgreementFields.MeetingPoint, AgreementFields.DeliveryAt],
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = now.AddDays(30),
            },
            Ct).ConfigureAwait(false);
    }

    private HttpClient ClientFor(string userId)
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, InMemoryCommunityRepository.UtttId, ["student"]);
        var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = Request(method, path);

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SomeoneElse_Gets404_OnEverything_AndNothingChanges()
    {
        var agreement = await AgreementAsync(await MemberAsync(), await MemberAsync());
        using var outsider = ClientFor(await MemberAsync());
        using var edit = Request("PUT", $"/api/v1/agreements/{agreement.Id}", new { revision = 1, priceCents = 100 });
        using var confirm = Request("POST", $"/api/v1/agreements/{agreement.Id}/confirm");
        using var discard = Request("DELETE", $"/api/v1/agreements/{agreement.Id}");
        using var summarize = Request("POST", "/api/v1/agreements", new { conversationId = agreement.ConversationId });

        var read = await outsider.GetAsync(new Uri($"/api/v1/agreements?conversationId={agreement.ConversationId}", UriKind.Relative), Ct);
        var edited = await outsider.SendAsync(edit, Ct);
        var confirmed = await outsider.SendAsync(confirm, Ct);
        var discarded = await outsider.SendAsync(discard, Ct);
        var summarized = await outsider.SendAsync(summarize, Ct);

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, discarded.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, summarized.StatusCode);
        Assert.Contains("agreements.not_found", await edited.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var after = await Agreements.FindByIdAsync(agreement.Id, DateTime.UtcNow, Ct);
        Assert.Equal((1, 50_000L), (after!.Revision, after.Proposal.PriceCents!.Value));
        Assert.Null(after.BuyerConfirmedAt);
    }

    [Fact]
    public async Task ASuspendedAccount_IsForbidden_EvenOnItsOwnAgreement()
    {
        var buyer = await MemberAsync(UserStatuses.Suspended);
        var agreement = await AgreementAsync(buyer, await MemberAsync());
        using var client = ClientFor(buyer);
        using var confirm = Request("POST", $"/api/v1/agreements/{agreement.Id}/confirm");
        using var summarize = Request("POST", "/api/v1/agreements", new { conversationId = agreement.ConversationId });

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(confirm, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(summarize, Ct)).StatusCode);
    }

    [Fact]
    public async Task Summarizing_HasAPerPersonLimit()
    {
        var buyer = await MemberAsync();
        var conversation = await ConversationAsync(buyer, await MemberAsync());
        using var client = ClientFor(buyer);
        var limit = new GenAiOptions().PermitLimit;

        for (var i = 0; i < limit; i++)
        {
            using var allowed = Request("POST", "/api/v1/agreements", new { conversationId = conversation });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.SendAsync(allowed, Ct)).StatusCode);
        }

        using var over = Request("POST", "/api/v1/agreements", new { conversationId = conversation });
        var rejected = await client.SendAsync(over, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("rate_limit.exceeded", await rejected.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public void InProduction_WithoutAGeminiKey_TheAiIsOff()
    {
        // RNF-06/RNF-15: Production usa Gemini; sin la llave (Parameter Store) no se llama a nadie.
        using var scope = production.Services.CreateScope();
        var extractor = scope.ServiceProvider.GetRequiredService<IAgreementExtractor>();

        Assert.IsType<GeminiAgreementExtractor>(extractor);
        Assert.False(extractor.Enabled);
    }

    [Theory]
    [InlineData(nameof(AgreementsController.Summarize))]
    [InlineData(nameof(AgreementsController.Update))]
    [InlineData(nameof(AgreementsController.Confirm))]
    public void TheBodies_HaveTheirOwnSizeLimit(string action)
    {
        var method = typeof(AgreementsController).GetMethod(action)!;

        Assert.Single(method.GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: false));
        Assert.Equal(16 * 1024, AgreementsController.MaxBodyBytes);
    }
}
