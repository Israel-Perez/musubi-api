using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.SellerSupport;

/// <summary>Errores del apoyo a vendedores (HU-37; catalogo canonico: docs/errores.md).</summary>
internal static class SellerSupportErrors
{
    /// <summary>RNF-15: sin proveedor, sin red, cuota excedida o respuesta con otra forma. Quedan las ideas predefinidas.</summary>
    public static readonly ApiError IdeasUnavailable = new(
        "seller_support.ideas_unavailable", StatusCodes.Status503ServiceUnavailable, "La IA no está disponible ahora. Usa estas ideas o inténtalo más tarde.");
}
