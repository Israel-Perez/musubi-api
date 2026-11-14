using System.Collections.Generic;
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

    /// <summary>Slugs de las categorias que le interesan (HU-66). Privados: el perfil publico no lee esta coleccion.</summary>
    public IReadOnlyList<string> Interests { get; init; } = [];

    public int SchemaVersion { get; init; } = 1;
}
