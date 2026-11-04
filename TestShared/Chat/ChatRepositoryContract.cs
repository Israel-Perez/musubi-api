using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat.Data;

namespace Musubi.TestShared.Chat;

/// <summary>
/// HU-20: comportamiento esperado de los repositorios del chat. Corre contra los fakes en memoria
/// (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids:
/// la base de Mongo se comparte en la clase.
/// </summary>
public abstract class ChatRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    internal abstract IConversationRepository Conversations { get; }

    internal abstract IMessageRepository Messages { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ConversationDocument NewConversation(string buyer, string seller, string productId) => new()
    {
        Id = AuthIds.New(),
        CommunityId = AuthIds.New(),
        BuyerId = buyer,
        SellerId = seller,
        Participants = [buyer, seller],
        Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = productId, Title = "Calculadora Casio fx-991ES Plus" },
        LastMessageAt = Now,
        CreatedAt = Now,
    };

    private static MessageDocument NewMessage(string conversationId, string sender, string text = "¿Sigue disponible?") => new()
    {
        Id = AuthIds.New(),
        ConversationId = conversationId,
        CommunityId = AuthIds.New(),
        SenderId = sender,
        Text = text,
        CreatedAt = Now,
    };

    [Fact]
    public async Task InsertOrGet_TheSameBuyerAndProductTwice_ReturnsTheFirstConversation()
    {
        var buyer = AuthIds.New();
        var seller = AuthIds.New();
        var product = AuthIds.New();
        var first = NewConversation(buyer, seller, product);

        var (created, isNew) = await Conversations.InsertOrGetAsync(first, Ct);
        var (again, isNewAgain) = await Conversations.InsertOrGetAsync(NewConversation(buyer, seller, product), Ct);

        Assert.True(isNew);
        Assert.False(isNewAgain);
        Assert.Equal(created.Id, again.Id);
        Assert.Equal([buyer, seller], (await Conversations.FindByIdAsync(first.Id, Ct))!.Participants);
    }

    [Fact]
    public async Task InsertOrGet_AnotherBuyerOfTheSameProduct_CreatesItsOwnConversation()
    {
        var seller = AuthIds.New();
        var product = AuthIds.New();

        var (ana, _) = await Conversations.InsertOrGetAsync(NewConversation(AuthIds.New(), seller, product), Ct);
        var (diego, created) = await Conversations.InsertOrGetAsync(NewConversation(AuthIds.New(), seller, product), Ct);

        Assert.True(created);
        Assert.NotEqual(ana.Id, diego.Id);
    }

    [Fact]
    public async Task FindById_UnknownOrNotAnObjectId_IsNull()
    {
        Assert.Null(await Conversations.FindByIdAsync(AuthIds.New(), Ct));
        Assert.Null(await Conversations.FindByIdAsync("no-es-un-id", Ct));
    }

    [Fact]
    public async Task SetLastMessage_KeepsTheCopy_AndNeverMovesActivityBack()
    {
        var conversation = NewConversation(AuthIds.New(), AuthIds.New(), AuthIds.New());
        await Conversations.InsertOrGetAsync(conversation, Ct);
        var later = new LastMessage { SenderId = conversation.BuyerId, Text = "¿Sigue disponible?", At = Now.AddMinutes(5) };
        var earlier = new LastMessage { SenderId = conversation.SellerId, Text = "Sí", At = Now.AddMinutes(1) };

        await Conversations.SetLastMessageAsync(conversation.Id, later, Ct);
        await Conversations.SetLastMessageAsync(conversation.Id, earlier, Ct);

        var saved = (await Conversations.FindByIdAsync(conversation.Id, Ct))!;
        Assert.Equal("Sí", saved.LastMessage!.Text);
        Assert.Equal(Now.AddMinutes(5), saved.LastMessageAt);
    }

    [Fact]
    public async Task Messages_AreNewestFirst_AndTheCursorContinues()
    {
        var conversation = AuthIds.New();
        var sender = AuthIds.New();
        var inserted = new List<MessageDocument>();
        for (var i = 0; i < 3; i++)
        {
            var message = NewMessage(conversation, sender, $"Mensaje {i}");
            inserted.Add(message);
            await Messages.InsertAsync(message, Ct);
        }

        await Messages.InsertAsync(NewMessage(AuthIds.New(), sender), Ct);

        var first = await Messages.FindPageAsync(conversation, null, 2, Ct);
        var second = await Messages.FindPageAsync(conversation, first[^1].Id, 2, Ct);

        Assert.Equal([inserted[2].Id, inserted[1].Id], first.Select(m => m.Id));
        Assert.Equal([inserted[0].Id], second.Select(m => m.Id));
    }

    [Fact]
    public async Task DeleteByParticipant_RemovesTheirConversationsAndMessages_AndNothingElse()
    {
        var leaving = AuthIds.New();
        var other = AuthIds.New();
        var asBuyer = NewConversation(leaving, other, AuthIds.New());
        var asSeller = NewConversation(other, leaving, AuthIds.New());
        var unrelated = NewConversation(other, AuthIds.New(), AuthIds.New());
        foreach (var conversation in new[] { asBuyer, asSeller, unrelated })
        {
            await Conversations.InsertOrGetAsync(conversation, Ct);
            await Messages.InsertAsync(NewMessage(conversation.Id, other), Ct);
        }

        var ids = await Conversations.FindIdsByParticipantAsync(leaving, Ct);
        var deletedMessages = await Messages.DeleteByConversationsAsync(ids, Ct);
        var deleted = await Conversations.DeleteByParticipantAsync(leaving, Ct);

        Assert.Equal([asBuyer.Id, asSeller.Id], ids.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(2, deletedMessages);
        Assert.Equal(2, deleted);
        Assert.NotNull(await Conversations.FindByIdAsync(unrelated.Id, Ct));
        Assert.Single(await Messages.FindPageAsync(unrelated.Id, null, 10, Ct));
        Assert.Equal(0, await Messages.DeleteByConversationsAsync([], Ct));
    }
}
