using System.ComponentModel.DataAnnotations;

namespace Musubi.Api.Modules.Profiles;

public sealed record PreferenceResponse(string Theme);

public sealed record UpdatePreferenceRequest([Required] [MaxLength(10)] string Theme);
