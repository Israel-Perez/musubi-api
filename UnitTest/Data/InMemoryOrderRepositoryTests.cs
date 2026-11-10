using Musubi.Api.Modules.Orders.Data;
using Musubi.TestShared.Orders;

namespace UnitTest.Data;

/// <summary>HU-23: el fake en memoria cumple el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryOrderRepositoryTests : OrderRepositoryContract
{
    internal override IOrderRepository Orders { get; } = new InMemoryOrderRepository();
}
