using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace DatabaseTest;

/// <summary>02-01: el script que se entrega (db/) es idempotente, valida en el servidor y deja la base lista.</summary>
public sealed class SchemaScriptTests(MongoContainerFixture mongo)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<BsonDocument>> IndexesAsync(IMongoDatabase db, string collection)
    {
        var cursor = await db.GetCollection<BsonDocument>(collection).Indexes.ListAsync(Ct).ConfigureAwait(false);
        return await cursor.ToListAsync(Ct).ConfigureAwait(false);
    }

    [Fact]
    public async Task Scripts_RunTwice_AreIdempotent()
    {
        const string name = "script_idempotente";
        await mongo.ApplyScriptsAsync(name);
        var db = mongo.EmptyDatabase(name);
        var indexesBefore = (await IndexesAsync(db, MongoSchema.Users)).Count;

        await mongo.ApplyScriptsAsync(name);

        var meta = await db.GetCollection<BsonDocument>(MongoSchema.Meta).Find(new BsonDocument("_id", "schema")).SingleAsync(Ct);
        Assert.Equal(MongoSchema.ExpectedVersion, meta["version"].AsInt32);
        Assert.Single(meta["applied"].AsBsonArray, v => v == "001-identidad");
        Assert.Equal(indexesBefore, (await IndexesAsync(db, MongoSchema.Users)).Count);
        Assert.Equal(1, await db.GetCollection<BsonDocument>(MongoSchema.Communities).CountDocumentsAsync(new BsonDocument("slug", "uttt"), cancellationToken: Ct));
    }

    [Fact]
    public async Task Scripts_CreateUniqueAndTtlIndexes()
    {
        var db = await mongo.MigratedDatabaseAsync("script_indices");

        var users = await IndexesAsync(db, MongoSchema.Users);
        var codes = await IndexesAsync(db, MongoSchema.VerificationCodes);
        var tokens = await IndexesAsync(db, MongoSchema.RefreshTokens);

        Assert.Contains(users, i => i["name"] == "ux_emailNormalized" && i.GetValue("unique", false).ToBoolean());
        Assert.Contains(codes, i => i["name"] == "ttl_expiresAt" && i["expireAfterSeconds"].ToInt32() == 0);
        Assert.Contains(tokens, i => i["name"] == "ux_tokenHash" && i.GetValue("unique", false).ToBoolean());
        Assert.Contains(tokens, i => i["name"] == "ttl_expiresAt");
    }

    [Fact]
    public async Task Validator_RejectsUserMissingRequiredFields()
    {
        var db = await mongo.MigratedDatabaseAsync("script_validacion");
        var users = db.GetCollection<BsonDocument>(MongoSchema.Users);

        var ex = await Assert.ThrowsAsync<MongoWriteException>(() =>
            users.InsertOneAsync(new BsonDocument { { "email", "x@uttt.edu.mx" } }, cancellationToken: Ct));

        Assert.Equal(ServerErrorCategory.Uncategorized, ex.WriteError.Category);
        Assert.Equal(121, ex.WriteError.Code); // DocumentValidationFailure
    }

    [Fact]
    public async Task Validator_RejectsUnknownStatus()
    {
        var db = await mongo.MigratedDatabaseAsync("script_validacion");
        var communities = db.GetCollection<BsonDocument>(MongoSchema.Communities);
        var bad = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() }, { "slug", "otra" }, { "name", "Otra" }, { "shortName", "OT" },
            { "emailDomains", new BsonArray { "otra.edu.mx" } }, { "status", "borrada" },
            { "schemaVersion", 1 }, { "createdAt", DateTime.UtcNow },
        };

        var ex = await Assert.ThrowsAsync<MongoWriteException>(() => communities.InsertOneAsync(bad, cancellationToken: Ct));

        Assert.Equal(121, ex.WriteError.Code);
    }

    [Fact]
    public async Task Readiness_IsUnhealthyUntilScriptsAreApplied()
    {
        var empty = mongo.EmptyDatabase("readiness_sin_scripts");
        var migrated = await mongo.MigratedDatabaseAsync("readiness_con_scripts");

        var before = await new MongoHealthCheck(empty).CheckHealthAsync(new HealthCheckContext(), Ct);
        var after = await new MongoHealthCheck(migrated).CheckHealthAsync(new HealthCheckContext(), Ct);

        Assert.Equal(HealthStatus.Unhealthy, before.Status);
        Assert.Equal("Esquema de datos desactualizado.", before.Description);
        Assert.Equal(HealthStatus.Healthy, after.Status);
    }
}
