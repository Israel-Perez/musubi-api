using MongoDB.Driver;
using Musubi.Api.Modules.Orders.Data;
using Musubi.TestShared.Orders;

namespace DatabaseTest;

/// <summary>HU-52: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado (db/028).</summary>
public sealed class MongoReservationRepositoryTests(MongoContainerFixture mongo) : ReservationRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IReservationRepository Reservations => new MongoReservationRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("reservations_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
