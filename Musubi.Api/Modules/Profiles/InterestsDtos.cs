using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Musubi.Api.Modules.Profiles;

/// <summary>Categorias que le interesan a la cuenta (HU-66); solo las ve la misma cuenta.</summary>
/// <param name="Categories">Slugs de categorias activas del campus, en el orden en que se eligieron.</param>
public sealed record InterestsResponse(IReadOnlyList<string> Categories);

/// <summary>Reemplaza los intereses de la cuenta autenticada; una lista vacia los borra.</summary>
/// <param name="Categories">Slugs de categorias activas del campus (hasta 20; los repetidos cuentan una vez).</param>
public sealed record UpdateInterestsRequest([Required] [MaxLength(InterestsService.MaxCategories)] IReadOnlyList<string> Categories);
