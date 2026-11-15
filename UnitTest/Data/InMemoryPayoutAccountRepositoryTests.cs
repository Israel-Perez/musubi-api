using Musubi.Api.Modules.Orders.Data;
using Musubi.TestShared.Orders;

namespace UnitTest.Data;

/// <summary>HU-55: el fake en memoria cumple el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryPayoutAccountRepositoryTests : PayoutAccountRepositoryContract
{
    internal override IPayoutAccountRepository Accounts { get; } = new InMemoryPayoutAccountRepository();
}
