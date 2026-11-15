using System;
using System.Collections.Generic;
using Musubi.Api.Modules.Listings;

namespace Musubi.Api.Modules.SellerSupport;

/// <summary>"Llevan tiempo sin venderse" (maqueta <c>Mis-productos</c>, HU-36).</summary>
/// <param name="Items">De la que lleva mas dias a la que lleva menos.</param>
/// <param name="ThresholdDays">Dias sin venderse a partir de los que aparece una (21: "mas de 3 semanas").</param>
public sealed record SlowListingList(IReadOnlyList<SlowListingResponse> Items, int ThresholdDays);

/// <summary>Una publicacion que lleva tiempo sin venderse.</summary>
/// <param name="Product">La publicacion.</param>
/// <param name="DaysListed">Dias desde que se publico ("Publicada hace 34 días").</param>
/// <param name="Views">Vistas de otras personas en los ultimos 30 dias ("96 vistas").</param>
/// <param name="Messages">Conversaciones que se abrieron desde ella ("0 mensajes").</param>
/// <param name="Demand">Quien la busca (HU-38).</param>
/// <param name="Tips">"Ideas para venderla" (HU-37): primero las que mas aplican.</param>
public sealed record SlowListingResponse(
    ProductResponse Product,
    int DaysListed,
    long Views,
    int Messages,
    DemandSummaryResponse Demand,
    IReadOnlyList<SalesTipResponse> Tips);

/// <summary>Cuantas personas buscan algo similar ("3 estudiantes la buscan").</summary>
/// <param name="People">Personas: con una solicitud activa o con una busqueda guardada (RN-37: estas solo se cuentan).</param>
/// <param name="Requests">De esas, cuantas tienen una solicitud "Se busca" (a esas se les puede ofrecer).</param>
public sealed record DemandSummaryResponse(int People, int Requests);

/// <summary>Una sugerencia para vender (HU-37, RN-38: solo orienta; nunca cambia nada).</summary>
/// <param name="Kind"><c>price</c>, <c>description</c> o <c>photos</c>.</param>
/// <param name="Title">"Revisa el precio".</param>
/// <param name="Body">Por que y que hacer.</param>
/// <param name="Applies">Si los datos de la publicacion la piden (precio arriba, descripcion corta o pocas fotos).</param>
public sealed record SalesTipResponse(string Kind, string Title, string Body, bool Applies);

/// <summary>"Quienes la buscan" (HU-38).</summary>
/// <param name="People">Personas que la buscan.</param>
/// <param name="Alerts">De esas, cuantas solo tienen una busqueda guardada (RN-37: nunca se dice quienes).</param>
/// <param name="Requests">Las solicitudes publicas que coinciden, de la mas parecida a la de mayor presupuesto.</param>
public sealed record ProductDemandResponse(int People, int Alerts, IReadOnlyList<DemandRequestResponse> Requests);

/// <summary>Una solicitud "Se busca" que coincide con mi publicacion.</summary>
/// <param name="Id">La solicitud ("Yo tengo este producto").</param>
/// <param name="Title">Lo que busca.</param>
/// <param name="BudgetCents">Su presupuesto.</param>
/// <param name="RequesterName">Quien busca ("Ana R."), como en el tablero.</param>
/// <param name="ExpiresAt">Vencimiento (UTC).</param>
public sealed record DemandRequestResponse(string Id, string Title, long BudgetCents, string RequesterName, DateTime ExpiresAt);

/// <summary>
/// "Ideas con IA" (HU-37, criterio del 13-Nov-2026): un nombre mas claro y una mejor descripcion. Solo una propuesta:
/// quien vende decide si la usa al editar (RN-38).
/// </summary>
/// <param name="Title">Nombre propuesto (3 a 80 caracteres).</param>
/// <param name="Description">Descripcion propuesta (hasta 500).</param>
public sealed record ListingIdeasResponse(string Title, string Description);

/// <summary>Tipos de sugerencia (HU-37).</summary>
public static class SalesTipKinds
{
    /// <summary>Revisar el precio.</summary>
    public const string Price = "price";

    /// <summary>Mejorar la descripcion.</summary>
    public const string Description = "description";

    /// <summary>Actualizar las fotos.</summary>
    public const string Photos = "photos";
}
