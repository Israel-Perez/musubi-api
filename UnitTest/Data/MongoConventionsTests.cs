using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Platform.Data;

namespace UnitTest.Data;

/// <summary>02-01: forma BSON de los documentos (debe coincidir con la validacion de db/001-identidad.js).</summary>
public sealed class MongoConventionsTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    public MongoConventionsTests() => MongoConventions.Register();

    private static UserDocument SampleUser() => new()
    {
        Id = MongoConventions.NewId(),
        CommunityId = MongoConventions.NewId(),
        Email = "Ana@UTTT.edu.mx",
        EmailNormalized = "ana@uttt.edu.mx",
        PasswordHash = "AQAAAAIAAYagAAAAEHashDePruebaNoReal==",
        FullName = "Ana Prueba",
        Consents = new UserConsents { TermsVersion = "2026-10", PrivacyVersion = "2026-10", AcceptedAt = Now },
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public void User_SerializesWithCamelCaseObjectIdsAndWithoutNulls()
    {
        var bson = SampleUser().ToBsonDocument();

        Assert.Equal(BsonType.ObjectId, bson["_id"].BsonType);
        Assert.Equal(BsonType.ObjectId, bson["communityId"].BsonType);
        Assert.Equal("ana@uttt.edu.mx", bson["emailNormalized"].AsString);
        Assert.Equal("pendingVerification", bson["status"].AsString);
        Assert.Equal(BsonType.Int32, bson["schemaVersion"].BsonType);
        Assert.Equal(BsonType.DateTime, bson["consents"]["acceptedAt"].BsonType);
        Assert.Equal(new BsonArray { "student" }, bson["roles"].AsBsonArray);
        Assert.False(bson.Contains("emailVerifiedAt"));
        Assert.DoesNotContain(bson.Names, name => char.IsUpper(name[0]));
    }

    [Fact]
    public void User_DeserializeIgnoresFieldsFromNewerVersions()
    {
        var bson = SampleUser().ToBsonDocument();
        bson["campoDeUnaVersionFutura"] = "x";

        var user = BsonSerializer.Deserialize<UserDocument>(bson);

        Assert.Equal("Ana Prueba", user.FullName);
        Assert.Equal(DateTimeKind.Utc, user.CreatedAt.Kind);
    }

    [Fact]
    public void RefreshToken_OptionalFieldsAreOmittedUntilSet()
    {
        var token = new RefreshTokenDocument
        {
            Id = MongoConventions.NewId(),
            UserId = MongoConventions.NewId(),
            FamilyId = MongoConventions.NewId(),
            TokenHash = new string('a', 64),
            ExpiresAt = Now,
            CreatedAt = Now,
        };

        var bson = token.ToBsonDocument();

        Assert.False(bson.Contains("usedAt"));
        Assert.False(bson.Contains("revokedAt"));
        Assert.False(bson.Contains("device"));
        Assert.Equal(BsonType.ObjectId, bson["familyId"].BsonType);
    }

    [Fact]
    public void Conversation_KeepsContextIdAsId_AndParticipantsAsObjectIds()
    {
        // HU-20: el driver convierte un "Id" anidado en "_id" si no se le indica lo contrario (db/005-chat.js pide context.id).
        var buyer = MongoConventions.NewId();
        var seller = MongoConventions.NewId();
        var bson = new ConversationDocument
        {
            Id = MongoConventions.NewId(),
            CommunityId = MongoConventions.NewId(),
            BuyerId = buyer,
            SellerId = seller,
            Participants = [buyer, seller],
            Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = MongoConventions.NewId(), Title = "Calculadora" },
            LastMessageAt = Now,
            CreatedAt = Now,
        }.ToBsonDocument();

        var context = bson["context"].AsBsonDocument;
        Assert.Equal(["type", "id", "title"], context.Names);
        Assert.Equal(BsonType.ObjectId, context["id"].BsonType);
        Assert.All(bson["participants"].AsBsonArray, p => Assert.Equal(BsonType.ObjectId, p.BsonType));
        Assert.False(bson.Contains("lastMessage"));
    }
}
