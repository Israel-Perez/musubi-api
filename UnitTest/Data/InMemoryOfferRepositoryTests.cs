using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.TestShared.PurchaseRequests;

namespace UnitTest.Data;

/// <summary>HU-16: el fake en memoria cumple el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryOfferRepositoryTests : OfferRepositoryContract
{
    internal override IOfferRepository Offers { get; } = new InMemoryOfferRepository();
}
