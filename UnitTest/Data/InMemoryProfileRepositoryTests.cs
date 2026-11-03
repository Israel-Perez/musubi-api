using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.TestShared.Profiles;

namespace UnitTest.Data;

/// <summary>HU-04: los fakes en memoria cumplen el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryProfileRepositoryTests : ProfileRepositoryContract
{
    internal override ICareerRepository Careers { get; } = new InMemoryCareerRepository();

    internal override IProfileRepository Profiles { get; } = new InMemoryProfileRepository();

    internal override Task<string> UtttIdAsync() => Task.FromResult(InMemoryCommunityRepository.UtttId);
}
