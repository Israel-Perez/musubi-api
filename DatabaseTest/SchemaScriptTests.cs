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
        Assert.Single(meta["applied"].AsBsonArray, v => v == "002-publicaciones");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "003-dominio-revision");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "004-perfiles");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "005-chat");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "006-chat-bandeja");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "008-reportes-bloqueos");
        Assert.Equal(6, await db.GetCollection<BsonDocument>(MongoSchema.Categories).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: Ct));
        Assert.Equal(38, await db.GetCollection<BsonDocument>(MongoSchema.Careers).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: Ct));
        Assert.Equal(indexesBefore, (await IndexesAsync(db, MongoSchema.Users)).Count);
        Assert.Equal(1, await db.GetCollection<BsonDocument>(MongoSchema.Communities).CountDocumentsAsync(new BsonDocument("slug", "uttt"), cancellationToken: Ct));
        var uttt = await db.GetCollection<BsonDocument>(MongoSchema.Communities).Find(new BsonDocument("slug", "uttt")).SingleAsync(Ct);
        Assert.Equal(["uttt.edu.mx", "musubiapp.com.mx"], uttt["emailDomains"].AsBsonArray.Select(d => d.AsString));
        Assert.Equal(new BsonDocument { { "unit", "cuatrimestre" }, { "count", 11 } }, uttt["academicTerm"].AsBsonDocument);
    }

    [Fact]
    public async Task Chat_HasItsIndexes_AndRejectsAnEmptyMessageAndAThirdParticipant()
    {
        var db = await mongo.MigratedDatabaseAsync("script_chat");
        var conversationIndexes = (await IndexesAsync(db, MongoSchema.Conversations)).ToDictionary(i => i["name"].AsString);
        var messageIndexes = (await IndexesAsync(db, MongoSchema.Messages)).Select(i => i["name"].AsString);

        Assert.True(conversationIndexes["ux_context_buyer"]["unique"].AsBoolean);
        Assert.Contains("ix_participant_lastAt", conversationIndexes.Keys);
        Assert.Contains("ix_conversation_id", messageIndexes);

        var buyer = ObjectId.GenerateNewId();
        var seller = ObjectId.GenerateNewId();
        var crowded = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() },
            { "buyerId", buyer }, { "sellerId", seller },
            { "participants", new BsonArray { buyer, seller, ObjectId.GenerateNewId() } },
            { "context", new BsonDocument { { "type", "product" }, { "id", ObjectId.GenerateNewId() }, { "title", "Calculadora" } } },
            { "lastMessageAt", DateTime.UtcNow }, { "createdAt", DateTime.UtcNow }, { "schemaVersion", 1 },
        };
        var empty = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() }, { "conversationId", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() },
            { "senderId", buyer }, { "text", string.Empty }, { "createdAt", DateTime.UtcNow }, { "schemaVersion", 1 },
        };

        var third = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<BsonDocument>(MongoSchema.Conversations).InsertOneAsync(crowded, cancellationToken: Ct));
        var blank = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<BsonDocument>(MongoSchema.Messages).InsertOneAsync(empty, cancellationToken: Ct));

        Assert.Equal(121, third.WriteError.Code);
        Assert.Equal(121, blank.WriteError.Code);

        // 006 (HU-22): los no leidos nunca son negativos.
        var negative = new BsonDocument(crowded)
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["participants"] = new BsonArray { buyer, seller },
            ["sellerUnread"] = -1,
        };
        var below = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<BsonDocument>(MongoSchema.Conversations).InsertOneAsync(negative, cancellationToken: Ct));
        Assert.Equal(121, below.WriteError.Code);
    }

    [Fact]
    public async Task Profiles_HaveTheirIndexes_AndRejectATermOfZero()
    {
        var db = await mongo.MigratedDatabaseAsync("script_perfiles");
        var profiles = db.GetCollection<BsonDocument>(MongoSchema.Profiles);
        var bad = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() }, { "fullName", "Ana Sofía" },
            { "term", 0 },
            { "visibility", new BsonDocument { { "career", true }, { "sales", true }, { "online", true } } },
            { "stats", new BsonDocument { { "ratingCount", 0 }, { "sales", 0 }, { "purchases", 0 } } },
            { "schemaVersion", 1 }, { "updatedAt", DateTime.UtcNow },
        };

        var ex = await Assert.ThrowsAsync<MongoWriteException>(() => profiles.InsertOneAsync(bad, cancellationToken: Ct));

        Assert.Equal(121, ex.WriteError.Code);
        Assert.Contains(await IndexesAsync(db, MongoSchema.Profiles), i => i["name"] == "ix_community");
        Assert.Contains(await IndexesAsync(db, MongoSchema.Careers), i => i["name"] == "ux_community_slug" && i.GetValue("unique", false).ToBoolean());
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
    public async Task Products_HaveTheShowcaseIndexes_AndRejectAFreeProduct()
    {
        var db = await mongo.MigratedDatabaseAsync("script_productos");
        var indexes = await IndexesAsync(db, MongoSchema.Products);
        var products = db.GetCollection<BsonDocument>(MongoSchema.Products);
        var seller = ObjectId.GenerateNewId();
        var free = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() }, { "sellerId", seller },
            { "seller", new BsonDocument("name", "Ana R.") }, { "title", "Calculadora" }, { "description", string.Empty },
            { "categorySlug", "calculadoras" }, { "priceCents", 0L }, { "condition", "new" }, { "stock", 1 },
            { "status", "active" }, { "photos", new BsonArray { $"media/products/{seller}/{Guid.NewGuid():N}.webp" } },
            { "schemaVersion", 1 }, { "createdAt", DateTime.UtcNow }, { "updatedAt", DateTime.UtcNow },
        };

        var ex = await Assert.ThrowsAsync<MongoWriteException>(() => products.InsertOneAsync(free, cancellationToken: Ct));

        Assert.Equal(121, ex.WriteError.Code);
        Assert.Contains(indexes, i => i["name"] == "ix_community_status_id");
        Assert.Contains(indexes, i => i["name"] == "ix_community_category_status_id");
        Assert.Contains(indexes, i => i["name"] == "ix_seller_id");
        // HU-09: indice de las palabras normalizadas para la busqueda por prefijo.
        Assert.Contains(indexes, i => i["name"] == "ix_community_status_search");
    }

    [Fact]
    public async Task ReportsAndBlocks_HaveTheirIndexes_AndRejectAnUnknownReasonOrAnEmptyName()
    {
        var db = await mongo.MigratedDatabaseAsync("script_reputacion");
        var reportIndexes = (await IndexesAsync(db, MongoSchema.Reports)).Select(i => i["name"].AsString);
        var blockIndexes = (await IndexesAsync(db, MongoSchema.Blocks)).ToDictionary(i => i["name"].AsString);
        var spam = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() }, { "reporterId", ObjectId.GenerateNewId() },
            { "target", new BsonDocument("type", "general") }, { "reason", "spam" }, { "status", "open" },
            { "createdAt", DateTime.UtcNow }, { "schemaVersion", 1 },
        };
        var nameless = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() }, { "blockerId", ObjectId.GenerateNewId() },
            { "blockedId", ObjectId.GenerateNewId() }, { "blockedName", string.Empty }, { "createdAt", DateTime.UtcNow }, { "schemaVersion", 1 },
        };

        var badReason = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<BsonDocument>(MongoSchema.Reports).InsertOneAsync(spam, cancellationToken: Ct));
        var badName = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<BsonDocument>(MongoSchema.Blocks).InsertOneAsync(nameless, cancellationToken: Ct));

        Assert.Equal(121, badReason.WriteError.Code);
        Assert.Equal(121, badName.WriteError.Code);
        Assert.Contains("ix_community_status", reportIndexes);
        Assert.Contains("ix_reporter", reportIndexes);
        Assert.True(blockIndexes["ux_blocker_blocked"]["unique"].AsBoolean);
        Assert.Contains("ix_blocked", blockIndexes.Keys);
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
