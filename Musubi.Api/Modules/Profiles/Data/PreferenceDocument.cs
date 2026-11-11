using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.Profiles.Data;

/// <summary>Preferencias de interfaz de una cuenta, identificadas por el usuario.</summary>
internal sealed class PreferenceDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; init; } = string.Empty;

    public string Theme { get; init; } = "system";

    public int SchemaVersion { get; init; } = 1;
}
