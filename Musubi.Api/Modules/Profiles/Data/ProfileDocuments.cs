using System;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.Profiles.Data;

// Documentos de persistencia de M-02 (HU-04). La validacion del servidor vive en db/004-perfiles.js;
// fila canonica en docs/modelo-datos.md.

/// <summary>Carrera de una comunidad (la administra HU-41; por ahora la siembra el script).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class CareerDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Identificador estable (<c>ing-mecatronica</c>); lo guarda el perfil.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Nombre oficial ("Ingeniería en Mecatrónica").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Lo que muestra el perfil ("Ing. Mecatrónica").</summary>
    public string ShortName { get; init; } = string.Empty;

    /// <summary>Ver <see cref="CareerLevels"/>.</summary>
    public string Level { get; init; } = CareerLevels.Tsu;

    /// <summary>Primer periodo de la carrera.</summary>
    public int FirstTerm { get; init; } = 1;

    /// <summary>Ultimo periodo de la carrera.</summary>
    public int LastTerm { get; init; } = 1;

    /// <summary>Orden en la lista.</summary>
    public int Order { get; init; }

    /// <summary>Se puede elegir.</summary>
    public bool Active { get; init; } = true;

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Niveles de una carrera.</summary>
internal static class CareerLevels
{
    /// <summary>Tecnico Superior Universitario.</summary>
    public const string Tsu = "tsu";

    /// <summary>Ingenieria.</summary>
    public const string Engineering = "engineering";

    /// <summary>Licenciatura.</summary>
    public const string Bachelor = "bachelor";
}

/// <summary>
/// Perfil de una persona (HU-04). <c>_id</c> es el id del usuario (R13). Separado de <c>users</c>: las
/// credenciales nunca viajan con el perfil (R10). Lo crea el primer cambio.
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class ProfileDocument
{
    /// <summary>Id del usuario.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Copia del nombre (R8; no cambia: viene del registro).</summary>
    public string FullName { get; init; } = string.Empty;

    /// <summary>Carrera (<see cref="CareerDocument.Slug"/>), o ninguna.</summary>
    public string? CareerSlug { get; init; }

    /// <summary>Periodo actual (1 al tope del campus), o ninguno.</summary>
    public int? Term { get; init; }

    /// <summary>Llave de S3 de la foto (<c>media/avatars/{userId}/{32hex}.webp</c>), o ninguna.</summary>
    public string? AvatarKey { get; init; }

    /// <summary>"Quien te ve" (maqueta <c>Privacidad</c>).</summary>
    public ProfileVisibility Visibility { get; init; } = new();

    /// <summary>Contadores (los actualizan HU-23 y HU-27).</summary>
    public ProfileStats Stats { get; init; } = new();

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Ultimo cambio (UTC).</summary>
    public DateTime UpdatedAt { get; init; }
}

/// <summary>Que muestra el perfil publico. Como en la maqueta: carrera y ventas encendidas, "en linea" apagado (RNF-05).</summary>
internal sealed class ProfileVisibility
{
    /// <summary>"Mostrar mi carrera" (carrera y periodo).</summary>
    public bool Career { get; init; } = true;

    /// <summary>"Mostrar mis ventas".</summary>
    public bool Sales { get; init; } = true;

    /// <summary>"Mostrar si estoy en línea" (NOTE (HU-21): lo aplica el encabezado del chat).</summary>
    public bool Online { get; init; }
}

/// <summary>Estadisticas del perfil (maqueta <c>Perfil</c>).</summary>
internal sealed class ProfileStats
{
    /// <summary>Promedio de calificaciones (1 a 5), o ninguno.</summary>
    public double? RatingAvg { get; init; }

    /// <summary>Calificaciones recibidas.</summary>
    public int RatingCount { get; init; }

    /// <summary>Suma de las estrellas recibidas (HU-27, HU-45): el promedio es la suma entre el numero.</summary>
    public double RatingSum { get; init; }

    /// <summary>Calificaciones con "Puntual": de aqui sale "A tiempo" de <c>Perfil-vendedor</c>.</summary>
    public int OnTimeCount { get; init; }

    /// <summary>Ventas completadas.</summary>
    public int Sales { get; init; }

    /// <summary>Compras completadas.</summary>
    public int Purchases { get; init; }
}
