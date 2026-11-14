using System.ComponentModel.DataAnnotations;

namespace Musubi.Api.Modules.Profiles;

/// <summary>Preferencias de la cuenta devueltas al cliente.</summary>
/// <param name="Theme">Tema: <c>system</c>, <c>light</c> o <c>dark</c>.</param>
/// <param name="Recommendations">"Recomendarte productos según tu actividad" (HU-35; <c>true</c> si nunca se cambio).</param>
public sealed record PreferenceResponse(string Theme, bool Recommendations);

/// <summary>Solicitud para actualizar el tema de apariencia.</summary>
/// <param name="Theme">Tema permitido: <c>system</c>, <c>light</c> o <c>dark</c>.</param>
/// <param name="Recommendations">
/// HU-35: recomendaciones segun la actividad; <c>null</c> lo deja igual (la app anterior solo manda el tema). Apagarlo
/// borra el historial que las alimenta.
/// </param>
public sealed record UpdatePreferenceRequest([Required] [MaxLength(10)] string Theme, bool? Recommendations = null);
