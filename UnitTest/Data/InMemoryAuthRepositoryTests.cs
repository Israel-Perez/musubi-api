using Musubi.Api.Modules.Auth.Data;
using Musubi.TestShared.Auth;

namespace UnitTest.Data;

/// <summary>02-01: los fakes en memoria cumplen el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryAuthRepositoryTests : AuthRepositoryContract
{
    internal override ICommunityRepository Communities { get; } = new InMemoryCommunityRepository();

    internal override IUserRepository Users { get; } = new InMemoryUserRepository();

    internal override IVerificationCodeRepository Codes { get; } = new InMemoryVerificationCodeRepository();

    internal override IRefreshTokenRepository Tokens { get; } = new InMemoryRefreshTokenRepository();
}
