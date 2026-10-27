using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Platform.Media;

/// <summary>
/// Sirve las imagenes guardadas en memoria (solo Development, sin S3). En la nube CloudFront atiende
/// <c>/media/*</c> desde el bucket y la peticion nunca llega aqui; con S3 esta ruta responde 404.
/// </summary>
[ApiController]
[ApiVersionNeutral]
[Route("media")]
[AllowAnonymous]
public sealed class MediaController : ControllerBase
{
    /// <summary>Devuelve una imagen por su llave.</summary>
    /// <param name="path">Resto de la llave despues de <c>media/</c>.</param>
    /// <returns>La imagen o 404.</returns>
    [HttpGet("{**path}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Get(string path)
    {
        var file = (HttpContext.RequestServices.GetRequiredService<IMediaStorage>() as InMemoryMediaStorage)?.Find("media/" + path);
        return file is null ? this.Problem(ApiErrors.NotFound) : File(file.Content, file.ContentType);
    }
}
