namespace Musubi.Api.Platform.Data;

/// <summary>Version del esquema y nombres de colecciones (catalogo canonico: docs/modelo-datos.md).</summary>
internal static class MongoSchema
{
    /// <summary>Version que esta API necesita; debe ser igual al numero del ultimo script de db/ (SchemaVersionTests).</summary>
    public const int ExpectedVersion = 1;

    /// <summary>Coleccion de metadatos (documento "schema").</summary>
    public const string Meta = "_meta";

    /// <summary>Campus / universidades (RNF-21).</summary>
    public const string Communities = "communities";

    /// <summary>Identidad y credenciales.</summary>
    public const string Users = "users";

    /// <summary>Codigos de verificacion (TTL).</summary>
    public const string VerificationCodes = "verificationCodes";

    /// <summary>Refresh tokens (solo hash; TTL).</summary>
    public const string RefreshTokens = "refreshTokens";
}
