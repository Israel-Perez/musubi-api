using System;
using System.Threading;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;

namespace Musubi.Api.Platform.Data;

/// <summary>Convenciones BSON de MUSUBI (02-01, docs/modelo-datos.md): camelCase, sin nulos y tolerante a campos nuevos.</summary>
internal static class MongoConventions
{
    private static int registered;

    /// <summary>Registra las convenciones una sola vez por proceso (el registro del driver es global).</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) == 1)
        {
            return;
        }

        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            // Una version vieja de la API no falla con documentos que traen campos nuevos (R4).
            new IgnoreExtraElementsConvention(true),
            // Los opcionales se omiten: la validacion $jsonSchema los declara sin "required".
            new IgnoreIfNullConvention(true),
        };
        ConventionRegistry.Register("musubi", pack, type => type.Namespace?.StartsWith("Musubi.Api", StringComparison.Ordinal) == true);
    }

    /// <summary>Genera un id nuevo (ObjectId: ordenado por tiempo y sin secuencia central, R2).</summary>
    /// <returns>El id en texto hexadecimal de 24 caracteres.</returns>
    public static string NewId() => ObjectId.GenerateNewId().ToString();
}
