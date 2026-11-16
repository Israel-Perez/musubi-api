using Musubi.Api.Modules.Orders.Data;
using Musubi.TestShared.Orders;

namespace UnitTest.Data;

/// <summary>HU-52: el fake en memoria cumple el mismo contrato que MongoDB (ver DatabaseTest).</summary>
public sealed class InMemoryReservationRepositoryTests : ReservationRepositoryContract
{
    internal override IReservationRepository Reservations { get; } = new InMemoryReservationRepository();
}
