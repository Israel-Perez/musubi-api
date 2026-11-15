using System;
using System.Collections.Generic;
using System.Linq;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.PurchaseRequests;

namespace Musubi.Api.Modules.SellerSupport;

/// <summary>
/// "Ideas para venderla" (HU-37, RF-52): las tres sugerencias predefinidas (revisar el precio, mejorar la descripcion y
/// actualizar las fotos), primero las que piden los datos de la publicacion. Son orientativas: nunca se cambia un precio
/// ni una publicacion por su cuenta (RN-38).
/// </summary>
internal static class SalesTips
{
    /// <summary>Una descripcion con menos letras que estas se sugiere mejorar.</summary>
    public const int ShortDescription = 80;

    /// <summary>Con menos fotos que estas se sugiere agregar mas.</summary>
    public const int FewPhotos = 3;

    /// <summary>Las sugerencias de una publicacion.</summary>
    /// <param name="product">La publicacion.</param>
    /// <param name="demand">Quien la busca (HU-38).</param>
    /// <param name="range">El rango de precios de su categoria, o <c>null</c>.</param>
    /// <returns>Las tres, primero las que aplican (en este orden: precio, descripcion y fotos).</returns>
    public static IReadOnlyList<SalesTipResponse> For(ProductResponse product, ProductDemand demand, PriceRangeResponse? range)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(demand);
        SalesTipResponse[] tips = [Price(product, demand, range), Description(product), Photos(product)];
        return [.. tips.OrderByDescending(tip => tip.Applies)];
    }

    // RF-52: si el precio esta por encima de los presupuestos de las solicitudes similares, se sugiere revisarlo.
    private static SalesTipResponse Price(ProductResponse product, ProductDemand demand, PriceRangeResponse? range)
    {
        const string title = "Revisa el precio";
        if (demand.Requests.Count > 0)
        {
            var budget = demand.Requests.Max(request => request.BudgetCents);
            if (product.PriceCents > budget)
            {
                var who = demand.Requests.Count == 1 ? "Quien busca algo así ofrece" : "Quienes buscan algo así ofrecen";
                return new(SalesTipKinds.Price, title, $"{who} hasta {OfferService.Pesos(budget)} y tú pides {OfferService.Pesos(product.PriceCents)}.", Applies: true);
            }
        }

        if (range is not null)
        {
            var between = $"Productos parecidos se venden entre {OfferService.Pesos(range.LowCents)} y {OfferService.Pesos(range.HighCents)}.";
            return product.PriceCents > range.HighCents
                ? new(SalesTipKinds.Price, title, between, Applies: true)
                : new(SalesTipKinds.Price, title, between + " Tu precio está dentro.", Applies: false);
        }

        return new(SalesTipKinds.Price, title, "Un precio un poco más bajo hace que te escriban antes.", Applies: false);
    }

    private static SalesTipResponse Description(ProductResponse product)
    {
        const string title = "Mejora la descripción";
        return product.Description.Trim().Length < ShortDescription
            ? new(SalesTipKinds.Description, title, "Es muy corta. Cuenta el estado, la marca o la medida para que te escriban sin tantas dudas.", Applies: true)
            : new(SalesTipKinds.Description, title, "Agrega lo que más te preguntan: medidas, detalles del estado y dónde la puedes entregar.", Applies: false);
    }

    private static SalesTipResponse Photos(ProductResponse product)
    {
        const string title = "Actualiza las fotos";
        var count = product.Photos.Count;
        var photos = count == 1 ? "1 foto" : $"{count} fotos";
        return count < FewPhotos
            ? new(SalesTipKinds.Photos, title, $"Tiene {photos}. Agrega más, con luz natural y desde varios ángulos.", Applies: true)
            : new(SalesTipKinds.Photos, title, "Una foto nueva, con luz natural y fondo limpio, la hace ver como recién publicada.", Applies: false);
    }
}
