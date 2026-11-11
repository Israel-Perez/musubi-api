using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.GenAi.Data;

namespace Musubi.TestShared.GenAi;

/// <summary>
/// HU-32/33/34: comportamiento esperado del repositorio de acuerdos. Corre contra el fake en memoria (UnitTest) y contra
/// MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class AgreementRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc);

    internal abstract IAgreementRepository Agreements { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AgreementDocument Agreement(string? conversationId = null, string? buyerId = null, string? sellerId = null, DateTime? expiresAt = null)
    {
        var buyer = buyerId ?? AuthIds.New();
        return new AgreementDocument
        {
            Id = AuthIds.New(),
            CommunityId = AuthIds.New(),
            ConversationId = conversationId ?? AuthIds.New(),
            BuyerId = buyer,
            SellerId = sellerId ?? AuthIds.New(),
            RequestedBy = buyer,
            Proposal = new AgreementProposal
            {
                Title = "Calculadora Casio fx-991ES Plus",
                PriceCents = 50_000,
                Quantity = 1,
                PointSlug = "biblioteca",
                PointName = "Biblioteca",
                Detail = "Planta baja",
            },
            NotIdentified = [AgreementFields.DeliveryAt],
            CreatedAt = Now,
            UpdatedAt = Now,
            ExpiresAt = expiresAt ?? Now.AddDays(30),
        };
    }

    [Fact]
    public async Task Replace_KeepsOnlyTheNewestAgreementOfTheConversation()
    {
        var conversation = AuthIds.New();
        var first = Agreement(conversation);
        var second = Agreement(conversation);

        await Agreements.ReplaceForConversationAsync(first, Ct);
        var saved = await Agreements.ReplaceForConversationAsync(second, Ct);

        Assert.Equal(second.Id, saved.Id);
        Assert.Null(await Agreements.FindByIdAsync(first.Id, Now, Ct));
        Assert.Equal(second.Id, (await Agreements.FindByConversationAsync(conversation, Now, Ct))?.Id);
        Assert.Equal([AgreementFields.DeliveryAt], saved.NotIdentified);
        Assert.Equal("Planta baja", saved.Proposal.Detail);
    }

    [Fact]
    public async Task AnExpiredAgreement_NoLongerCounts()
    {
        var agreement = Agreement(expiresAt: Now.AddMinutes(1));
        await Agreements.ReplaceForConversationAsync(agreement, Ct);

        Assert.NotNull(await Agreements.FindByIdAsync(agreement.Id, Now, Ct));
        Assert.Null(await Agreements.FindByIdAsync(agreement.Id, Now.AddMinutes(2), Ct));
        Assert.Null(await Agreements.FindByConversationAsync(agreement.ConversationId, Now.AddMinutes(2), Ct));
        Assert.Null(await Agreements.FindByIdAsync("no-es-un-id", Now, Ct));
    }

    [Fact]
    public async Task Update_RaisesTheRevision_AndClearsBothConfirmations_OnlyFromTheRevisionSeen()
    {
        var agreement = Agreement();
        await Agreements.ReplaceForConversationAsync(agreement, Ct);
        await Agreements.ConfirmAsync(agreement.Id, 1, AgreementRoles.Seller, Now, Ct);
        var proposal = new AgreementProposal
        {
            Title = "Calculadora Casio con funda",
            PriceCents = 45_000,
            Quantity = 1,
            PointSlug = "cafeteria",
            PointName = "Cafetería",
            At = Now.AddDays(1),
        };

        var changed = await Agreements.UpdateProposalAsync(agreement.Id, 1, proposal, Now.AddMinutes(1), Ct);
        var stale = await Agreements.UpdateProposalAsync(agreement.Id, 1, proposal, Now.AddMinutes(2), Ct);

        Assert.NotNull(changed);
        Assert.Equal(2, changed.Revision);
        Assert.Equal(AgreementStatuses.Proposed, changed.Status);
        Assert.Null(changed.SellerConfirmedAt);
        Assert.Null(changed.Proposal.Detail);
        Assert.Equal(45_000, changed.Proposal.PriceCents);
        Assert.Equal(Now.AddDays(1), changed.Proposal.At);
        Assert.Null(stale);
    }

    [Fact]
    public async Task Confirm_ByBoth_OnTheSameRevision_ConfirmsTheAgreement()
    {
        var agreement = Agreement();
        await Agreements.ReplaceForConversationAsync(agreement, Ct);

        var one = await Agreements.ConfirmAsync(agreement.Id, 1, AgreementRoles.Buyer, Now, Ct);
        var again = await Agreements.ConfirmAsync(agreement.Id, 1, AgreementRoles.Buyer, Now.AddSeconds(5), Ct);
        var both = await Agreements.ConfirmAsync(agreement.Id, 1, AgreementRoles.Seller, Now.AddMinutes(1), Ct);
        var wrong = await Agreements.ConfirmAsync(agreement.Id, 7, AgreementRoles.Seller, Now, Ct);

        Assert.Equal(AgreementStatuses.Proposed, one?.Status);
        Assert.NotNull(one?.BuyerConfirmedAt);
        Assert.Equal(AgreementStatuses.Proposed, again?.Status);
        Assert.Equal(AgreementStatuses.Confirmed, both?.Status);
        Assert.Equal(Now.AddMinutes(1), both?.SellerConfirmedAt);
        Assert.Null(wrong);
    }

    [Fact]
    public async Task Delete_AndDeleteByParticipant_RemoveOnlyTheirAgreements()
    {
        var ana = AuthIds.New();
        var mine = Agreement(buyerId: ana);
        var sold = Agreement(sellerId: ana);
        var other = Agreement();
        var discarded = Agreement();
        foreach (var a in new[] { mine, sold, other, discarded })
        {
            await Agreements.ReplaceForConversationAsync(a, Ct);
        }

        Assert.True(await Agreements.DeleteAsync(discarded.Id, Ct));
        Assert.False(await Agreements.DeleteAsync(discarded.Id, Ct));
        Assert.Equal(2, await Agreements.DeleteByParticipantAsync(ana, Ct));
        Assert.Null(await Agreements.FindByIdAsync(mine.Id, Now, Ct));
        Assert.Null(await Agreements.FindByIdAsync(sold.Id, Now, Ct));
        Assert.NotNull(await Agreements.FindByIdAsync(other.Id, Now, Ct));
    }
}
