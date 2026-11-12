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
        Assert.Single(meta["applied"].AsBsonArray, v => v == "007-busqueda");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "008-reportes-bloqueos");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "009-solicitudes");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "010-filtros-precio");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "011-ofertas");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "012-ofertas-chat");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "013-favoritos");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "014-pedidos");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "015-acuerdos");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "016-publicacion-vendida");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "017-avisos");
        Assert.Equal(14, await db.GetCollection<BsonDocument>(MongoSchema.MeetingPoints).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: Ct));
        Assert.Single(meta["applied"].AsBsonArray, v => v == "018-preferencias");
        Assert.Single(meta["applied"].AsBsonArray, v => v == "019-pedidos-estados");
        var preferences = await IndexesAsync(db, MongoSchema.Preferences);
        Assert.Contains(preferences, i => i["name"] == "_id_");
        Assert.Contains(await IndexesAsync(db, MongoSchema.Offers), i => i["name"] == "ux_request_seller" && i.GetValue("unique", false).ToBoolean());
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
    public async Task Products_AcceptASoldProduct_AndStillRejectAnUnknownStatus()
    {
        // HU-08 (db/015): "Marcar como vendido" deja status "sold" con soldAt; el validador sigue cerrado.
        var db = await mongo.MigratedDatabaseAsync("script_vendida");
        var products = db.GetCollection<BsonDocument>(MongoSchema.Products);
        var seller = ObjectId.GenerateNewId();
        BsonDocument Product(string status) => new()
        {
            { "_id", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() }, { "sellerId", seller },
            { "seller", new BsonDocument("name", "Ana R.") }, { "title", "Calculadora" }, { "description", string.Empty },
            { "categorySlug", "calculadoras" }, { "priceCents", 45_000L }, { "condition", "new" }, { "stock", 1 },
            { "status", status }, { "photos", new BsonArray { $"media/products/{seller}/{Guid.NewGuid():N}.webp" } },
            { "schemaVersion", 1 }, { "createdAt", DateTime.UtcNow }, { "updatedAt", DateTime.UtcNow },
        };
        var sold = Product("sold");
        sold["soldAt"] = DateTime.UtcNow;

        await products.InsertOneAsync(sold, cancellationToken: Ct);
        var ex = await Assert.ThrowsAsync<MongoWriteException>(() => products.InsertOneAsync(Product("vendida"), cancellationToken: Ct));

        Assert.Equal(121, ex.WriteError.Code);
        Assert.Equal(1, await products.CountDocumentsAsync(new BsonDocument("status", "sold"), cancellationToken: Ct));
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
        // HU-10: indices para ordenar por precio sin ordenar en memoria; la direccion de _id es parte del contrato.
        var byName = indexes.ToDictionary(i => i["name"].AsString);
        Assert.Equal(
            new BsonDocument { { "communityId", 1 }, { "status", 1 }, { "priceCents", 1 }, { "_id", -1 } },
            byName["ix_community_status_price_id"]["key"].AsBsonDocument);
        Assert.Equal(
            new BsonDocument { { "communityId", 1 }, { "status", 1 }, { "priceCents", -1 }, { "_id", -1 } },
            byName["ix_community_status_price_desc_id"]["key"].AsBsonDocument);
        Assert.Equal(
            new BsonDocument { { "communityId", 1 }, { "categorySlug", 1 }, { "status", 1 }, { "priceCents", 1 }, { "_id", -1 } },
            byName["ix_community_category_status_price_id"]["key"].AsBsonDocument);
        Assert.Equal(
            new BsonDocument { { "communityId", 1 }, { "categorySlug", 1 }, { "status", 1 }, { "priceCents", -1 }, { "_id", -1 } },
            byName["ix_community_category_status_price_desc_id"]["key"].AsBsonDocument);
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
    public async Task Favorites_HaveTheUniquePairIndex_AndRejectAnIncompleteFavorite()
    {
        var db = await mongo.MigratedDatabaseAsync("script_favoritos");
        var indexes = (await IndexesAsync(db, MongoSchema.Favorites)).ToDictionary(i => i["name"].AsString);
        var incomplete = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() }, { "userId", ObjectId.GenerateNewId() },
        };

        var ex = await Assert.ThrowsAsync<MongoWriteException>(() =>
            db.GetCollection<BsonDocument>(MongoSchema.Favorites).InsertOneAsync(incomplete, cancellationToken: Ct));

        Assert.Equal(121, ex.WriteError.Code);
        Assert.True(indexes["ux_user_product"]["unique"].AsBoolean);
        Assert.Contains("ix_user_id", indexes.Keys);
    }

    [Fact]
    public async Task WantedRequests_HaveTheirIndexes_AndRejectAZeroBudgetOrAnUnknownStatus()
    {
        var db = await mongo.MigratedDatabaseAsync("script_solicitudes");
        var indexes = (await IndexesAsync(db, MongoSchema.WantedRequests)).Select(i => i["name"].AsString).ToList();
        BsonDocument Wanted(long budget, string status) => new()
        {
            { "_id", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() }, { "requesterId", ObjectId.GenerateNewId() },
            { "requesterName", "Ana R." }, { "title", "Calculadora" }, { "categorySlug", "calculadoras" }, { "categoryName", "Calculadoras" },
            { "features", new BsonArray() }, { "budgetCents", budget }, { "quantity", 1 }, { "expiresAt", DateTime.UtcNow.AddDays(30) },
            { "status", status }, { "offersCount", 0 }, { "createdAt", DateTime.UtcNow }, { "schemaVersion", 1 },
        };

        var free = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<BsonDocument>(MongoSchema.WantedRequests).InsertOneAsync(Wanted(0L, "active"), cancellationToken: Ct));
        var unknown = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<BsonDocument>(MongoSchema.WantedRequests).InsertOneAsync(Wanted(60_000L, "expired"), cancellationToken: Ct));
        await db.GetCollection<BsonDocument>(MongoSchema.WantedRequests).InsertOneAsync(Wanted(60_000L, "active"), cancellationToken: Ct);

        Assert.Equal(121, free.WriteError.Code);
        Assert.Equal(121, unknown.WriteError.Code);
        Assert.Contains("ix_community_status_id", indexes);
        Assert.Contains("ix_requester_id", indexes);
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

    [Fact]
    public async Task Orders_HaveTheirIndexes_SeedTheCampusPoints_AndRejectAnInvalidOrder()
    {
        var db = await mongo.MigratedDatabaseAsync("script_pedidos");
        var indexes = (await IndexesAsync(db, MongoSchema.Orders)).ToDictionary(i => i["name"].AsString);

        Assert.True(indexes["ux_conversation_open"]["unique"].AsBoolean);
        Assert.Contains("partialFilterExpression", indexes["ux_conversation_open"].Names);
        Assert.Contains("ix_conversation_id", indexes.Keys);
        Assert.Contains("ix_buyer_id", indexes.Keys);
        Assert.Contains("ix_seller_id", indexes.Keys);
        Assert.Contains("ix_product_status", indexes.Keys);
        var ti = await db.GetCollection<BsonDocument>(MongoSchema.MeetingPoints).Find(new BsonDocument("slug", "tecnologias-informacion")).SingleAsync(Ct);
        Assert.Contains("ing-software", ti["careerSlugs"].AsBsonArray.Select(c => c.AsString));

        var now = DateTime.UtcNow;
        BsonDocument Order(int quantity) => new()
        {
            { "_id", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() }, { "number", 1001 },
            { "buyerId", ObjectId.GenerateNewId() }, { "sellerId", ObjectId.GenerateNewId() },
            { "buyerName", "Ana" }, { "sellerName", "Diego" }, { "conversationId", ObjectId.GenerateNewId() },
            {
                "item", new BsonDocument
                {
                    { "source", "product" }, { "sourceId", ObjectId.GenerateNewId() }, { "title", "Calculadora" },
                    { "priceCents", 50_000L }, { "quantity", quantity }, { "totalCents", 50_000L * quantity },
                }
            },
            { "delivery", new BsonDocument { { "pointSlug", "biblioteca" }, { "pointName", "Biblioteca" }, { "at", now } } },
            { "status", "pending" },
            { "history", new BsonArray { new BsonDocument { { "status", "pending" }, { "at", now } } } },
            { "createdAt", now }, { "updatedAt", now }, { "schemaVersion", 1 },
        };
        var orders = db.GetCollection<BsonDocument>(MongoSchema.Orders);

        await orders.InsertOneAsync(Order(1), cancellationToken: Ct);
        await Assert.ThrowsAsync<MongoWriteException>(() => orders.InsertOneAsync(Order(11), cancellationToken: Ct));

        // 019 (HU-24): la entrega de cada parte y la cancelacion con un motivo de la lista.
        var completed = Order(1);
        completed["status"] = "completed";
        completed["handover"] = new BsonDocument { { "buyerAt", now }, { "sellerAt", now } };
        var cancelled = Order(1);
        cancelled["status"] = "cancelled";
        cancelled["cancellation"] = new BsonDocument { { "by", "seller" }, { "reason", "unavailable" }, { "at", now } };
        var unknownReason = Order(1);
        unknownReason["cancellation"] = new BsonDocument { { "by", "buyer" }, { "reason", "me-aburri" }, { "at", now } };
        var unknownParty = Order(1);
        unknownParty["cancellation"] = new BsonDocument { { "by", "admin" }, { "reason", "other" }, { "at", now } };
        await orders.InsertOneAsync(completed, cancellationToken: Ct);
        await orders.InsertOneAsync(cancelled, cancellationToken: Ct);
        await Assert.ThrowsAsync<MongoWriteException>(() => orders.InsertOneAsync(unknownReason, cancellationToken: Ct));
        await Assert.ThrowsAsync<MongoWriteException>(() => orders.InsertOneAsync(unknownParty, cancellationToken: Ct));
    }

    [Fact]
    public async Task Agreements_HaveTheirIndexes_ExpireAlone_AndMessagesAcceptOnlyNotices()
    {
        var db = await mongo.MigratedDatabaseAsync("script_acuerdos");
        var indexes = (await IndexesAsync(db, MongoSchema.Agreements)).ToDictionary(i => i["name"].AsString);

        Assert.True(indexes["ux_conversation"]["unique"].AsBoolean);
        Assert.Equal(0, indexes["ttl_expires_at"]["expireAfterSeconds"].ToInt32());
        Assert.Contains("ix_buyer_id", indexes.Keys);
        Assert.Contains("ix_seller_id", indexes.Keys);

        var now = DateTime.UtcNow;
        BsonDocument Agreement(int quantity) => new()
        {
            { "_id", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() }, { "conversationId", ObjectId.GenerateNewId() },
            { "buyerId", ObjectId.GenerateNewId() }, { "sellerId", ObjectId.GenerateNewId() }, { "requestedBy", ObjectId.GenerateNewId() },
            { "proposal", new BsonDocument { { "title", "Calculadora" }, { "priceCents", 50_000L }, { "quantity", quantity } } },
            { "notIdentified", new BsonArray { "deliveryAt" } }, { "revision", 1 }, { "status", "proposed" },
            { "createdAt", now }, { "updatedAt", now }, { "expiresAt", now.AddDays(30) }, { "schemaVersion", 1 },
        };
        var agreements = db.GetCollection<BsonDocument>(MongoSchema.Agreements);
        await agreements.InsertOneAsync(Agreement(1), cancellationToken: Ct);
        await Assert.ThrowsAsync<MongoWriteException>(() => agreements.InsertOneAsync(Agreement(11), cancellationToken: Ct));

        BsonDocument Message(string? kind)
        {
            var m = new BsonDocument
            {
                { "_id", ObjectId.GenerateNewId() }, { "conversationId", ObjectId.GenerateNewId() }, { "communityId", ObjectId.GenerateNewId() },
                { "senderId", ObjectId.GenerateNewId() }, { "text", "Ana pidió un resumen con IA" }, { "createdAt", now }, { "schemaVersion", 1 },
            };
            if (kind is not null)
            {
                m["kind"] = kind;
            }

            return m;
        }

        var messages = db.GetCollection<BsonDocument>(MongoSchema.Messages);
        await messages.InsertOneAsync(Message(null), cancellationToken: Ct);
        await messages.InsertOneAsync(Message("notice"), cancellationToken: Ct);
        await Assert.ThrowsAsync<MongoWriteException>(() => messages.InsertOneAsync(Message("system"), cancellationToken: Ct));
    }

    [Fact]
    public async Task Notifications_HaveTheirIndexes_AndRejectAnUnknownType()
    {
        var db = await mongo.MigratedDatabaseAsync("script_avisos");
        var notices = (await IndexesAsync(db, MongoSchema.Notifications)).ToDictionary(i => i["name"].AsString);
        var tokens = (await IndexesAsync(db, MongoSchema.DeviceTokens)).ToDictionary(i => i["name"].AsString);
        var wanted = (await IndexesAsync(db, MongoSchema.WantedRequests)).ToDictionary(i => i["name"].AsString);

        Assert.True(notices["ux_user_dedupe"]["unique"].AsBoolean);
        Assert.Contains("partialFilterExpression", notices["ux_user_dedupe"].Names);
        Assert.Contains("ix_user_id", notices.Keys);
        Assert.Contains("ix_user_category_id", notices.Keys);
        Assert.Equal(0, notices["ttl_expires_at"]["expireAfterSeconds"].ToInt32());
        Assert.True(tokens["ux_token"]["unique"].AsBoolean);
        Assert.Equal(0, tokens["ttl_expires_at"]["expireAfterSeconds"].ToInt32());
        Assert.Contains("ix_status_expires", wanted.Keys);

        var now = DateTime.UtcNow;
        BsonDocument Notice(string type) => new()
        {
            { "_id", ObjectId.GenerateNewId() }, { "userId", ObjectId.GenerateNewId() }, { "type", type }, { "category", "orders" },
            { "title", "Tienes un pedido nuevo" }, { "body", "Ana pidió 1 × Calculadora." },
            { "target", new BsonDocument { { "type", "order" }, { "id", ObjectId.GenerateNewId() } } },
            { "createdAt", now }, { "expiresAt", now.AddDays(60) }, { "schemaVersion", 1 },
        };
        var collection = db.GetCollection<BsonDocument>(MongoSchema.Notifications);
        await collection.InsertOneAsync(Notice("order_created"), cancellationToken: Ct);
        // 019 (HU-24): los avisos de aceptar, cancelar, entregar y completar.
        foreach (var type in new[] { "order_confirmed", "order_cancelled", "order_handover", "order_completed" })
        {
            await collection.InsertOneAsync(Notice(type), cancellationToken: Ct);
        }

        await Assert.ThrowsAsync<MongoWriteException>(() => collection.InsertOneAsync(Notice("spam"), cancellationToken: Ct));
    }
}
