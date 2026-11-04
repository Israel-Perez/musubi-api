using Musubi.Api.Modules.Chat.Data;
using Musubi.TestShared.Chat;

namespace UnitTest.Data;

/// <summary>HU-20: los fakes en memoria cumplen el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryChatRepositoryTests : ChatRepositoryContract
{
    internal override IConversationRepository Conversations { get; } = new InMemoryConversationRepository();

    internal override IMessageRepository Messages { get; } = new InMemoryMessageRepository();
}
