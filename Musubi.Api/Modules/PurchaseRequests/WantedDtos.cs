using System;
using System.Collections.Generic;

namespace Musubi.Api.Modules.PurchaseRequests;

// Contratos HTTP de M-05 (HU-13, HU-15). Filas canonicas en docs/endpoints.md.

/// <summary>Publicar una solicitud (maqueta <c>Necesidad-1…3</c>).</summary>
/// <param name="Title">"Producto que buscas" (3 a 80 caracteres).</param>
/// <param name="Category">Slug de una categoria del campus, u <c>otro</c>.</param>
/// <param name="Description">"Cuentanos mas" (opcional, hasta 300).</param>
/// <param name="Features">"Caracteristicas deseadas" (opcional, hasta 5 de hasta 40).</param>
/// <param name="BudgetCents">Presupuesto maximo en centavos ($1 a $999,999).</param>
/// <param name="Quantity">Piezas (1 a 10).</param>
/// <param name="NeededBy">"Para cuando la necesitas" (<c>yyyy-MM-dd</c>, de hoy a 60 dias), o <c>null</c>.</param>
public sealed record CreateWantedRequest(
    string Title,
    string Category,
    string? Description,
    IReadOnlyList<string>? Features,
    long? BudgetCents,
    int? Quantity,
    DateOnly? NeededBy);

/// <summary>Una solicitud (maqueta <c>Necesidad-publicada</c>, <c>Mis-solicitudes</c>).</summary>
/// <param name="Id">Id.</param>
/// <param name="Title">Producto que se busca.</param>
/// <param name="Category">Categoria.</param>
/// <param name="Description">Descripcion, o <c>null</c>.</param>
/// <param name="Features">Caracteristicas deseadas.</param>
/// <param name="BudgetCents">Presupuesto maximo en centavos.</param>
/// <param name="Quantity">Piezas.</param>
/// <param name="NeededBy">Para cuando, o <c>null</c>.</param>
/// <param name="ExpiresAt">Cuando deja de estar activa (UTC).</param>
/// <param name="Status"><c>active</c>, <c>closed</c>, <c>cancelled</c> o <c>expired</c> (calculado).</param>
/// <param name="OffersCount">Ofertas recibidas (HU-16).</param>
/// <param name="Requester">Quien busca (nombre corto).</param>
/// <param name="CreatedAt">Publicacion (UTC).</param>
public sealed record WantedResponse(
    string Id,
    string Title,
    WantedCategoryResponse Category,
    string? Description,
    IReadOnlyList<string> Features,
    long BudgetCents,
    int Quantity,
    DateOnly? NeededBy,
    DateTime ExpiresAt,
    string Status,
    int OffersCount,
    WantedRequesterResponse Requester,
    DateTime CreatedAt);

/// <summary>Categoria de una solicitud.</summary>
/// <param name="Slug">Slug, u <c>otro</c>.</param>
/// <param name="Name">Nombre ("Calculadoras", "Otro").</param>
public sealed record WantedCategoryResponse(string Slug, string Name);

/// <summary>Quien busca.</summary>
/// <param name="Id">Id del usuario; <c>null</c> para un visitante (RNF-05).</param>
/// <param name="Name">
/// Nombre completo en el tablero con sesion ("Fernanda Ortiz", como el perfil publico); nombre corto ("Fernanda O.")
/// para un visitante y en "Mis solicitudes".
/// </param>
/// <param name="Photo">Ruta de la foto (<c>/media/avatars/...</c>) en el tablero con sesion, o <c>null</c>.</param>
/// <param name="Career">Carrera corta ("Ing. Software") en el tablero con sesion si la muestra (HU-04), o <c>null</c>.</param>
public sealed record WantedRequesterResponse(string? Id, string Name, string? Photo = null, string? Career = null);

/// <summary>Filtros del tablero "Se busca" (HU-15): la hoja "Filtrar solicitudes" y la lupa de la maqueta.</summary>
/// <param name="Campus">Campus del visitante (sin sesion; por defecto el del piloto).</param>
/// <param name="Category">Slugs de las categorias (OR; se puede repetir el parametro).</param>
/// <param name="MinBudgetCents">Presupuesto minimo, inclusivo.</param>
/// <param name="MaxBudgetCents">Presupuesto maximo, inclusivo.</param>
/// <param name="NeededWithinDays">"Fecha limite": solo las que tienen fecha y la necesitan en este numero de dias.</param>
/// <param name="Q">Palabras del producto o de la descripcion.</param>
/// <param name="Cursor">Cursor de la pagina anterior.</param>
/// <param name="Limit">Tamano de pagina (1 a 50).</param>
public sealed record WantedBoardFilter(
    string? Campus = null,
    IReadOnlyList<string>? Category = null,
    long? MinBudgetCents = null,
    long? MaxBudgetCents = null,
    int? NeededWithinDays = null,
    string? Q = null,
    string? Cursor = null,
    int? Limit = null);

/// <summary>Una pagina del tablero "Se busca" (HU-15; maquetas <c>Se-busca</c> y <c>Se-busca-visitante</c>).</summary>
/// <param name="Items">Solicitudes activas, de la mas nueva a la mas vieja.</param>
/// <param name="NextCursor">Cursor de la pagina siguiente, o <c>null</c> si no hay mas.</param>
public sealed record WantedPage(IReadOnlyList<WantedResponse> Items, string? NextCursor);

/// <summary>"Mis solicitudes", de la mas nueva a la mas vieja.</summary>
/// <param name="Items">Las solicitudes.</param>
public sealed record WantedListResponse(IReadOnlyList<WantedResponse> Items);
