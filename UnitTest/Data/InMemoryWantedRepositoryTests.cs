using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.TestShared.PurchaseRequests;

namespace UnitTest.Data;

/// <summary>HU-13: el fake en memoria cumple el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryWantedRepositoryTests : WantedRepositoryContract
{
    internal override IWantedRepository Requests { get; } = new InMemoryWantedRepository();
}
