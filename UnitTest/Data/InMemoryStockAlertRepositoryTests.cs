using Musubi.Api.Modules.Listings.Data;
using Musubi.TestShared.Listings;

namespace UnitTest.Data;

/// <summary>HU-51: el fake en memoria cumple el mismo contrato que MongoDB.</summary>
public sealed class InMemoryStockAlertRepositoryTests : StockAlertRepositoryContract
{
    internal override IStockAlertRepository Alerts { get; } = new InMemoryStockAlertRepository();
}
