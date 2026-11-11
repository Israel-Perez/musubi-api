using System.ComponentModel.DataAnnotations;

namespace Musubi.Api.Modules.Profiles;

/// <summary>Preferencia de apariencia devuelta al cliente.</summary>
/// <param name="Theme">Tema: <c>system</c>, <c>light</c> o <c>dark</c>.</param>
public sealed record PreferenceResponse(string Theme);

/// <summary>Solicitud para actualizar el tema de apariencia.</summary>
/// <param name="Theme">Tema permitido: <c>system</c>, <c>light</c> o <c>dark</c>.</param>
public sealed record UpdatePreferenceRequest([Required] [MaxLength(10)] string Theme);
