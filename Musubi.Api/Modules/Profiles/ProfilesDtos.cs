using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Musubi.Api.Modules.Auth;

namespace Musubi.Api.Modules.Profiles;

// Contrato de /api/v1/profiles y /api/v1/careers (filas en docs/endpoints.md; formas en ContractTest/Fixtures/).
// El perfil publico nunca lleva el correo ni lo que la persona oculto en "Quien te ve" (OWASP API3).

/// <summary>"Datos personales" (maqueta <c>Ajustes</c>): carrera y periodo; <c>null</c> los borra.</summary>
/// <param name="Career">Slug de la carrera del campus.</param>
/// <param name="Term">Periodo actual.</param>
public sealed record UpdateProfileRequest([MaxLength(40)] string? Career, [Range(1, 20)] int? Term);

/// <summary>"Quien te ve" (maqueta <c>Privacidad</c>).</summary>
/// <param name="Career">Mostrar mi carrera.</param>
/// <param name="Sales">Mostrar mis ventas.</param>
/// <param name="Online">Mostrar si estoy en linea.</param>
public sealed record VisibilityRequest([Required] bool? Career, [Required] bool? Sales, [Required] bool? Online);

/// <summary>Periodo academico del campus.</summary>
/// <param name="Unit"><c>cuatrimestre</c>, <c>semestre</c> o <c>trimestre</c>.</param>
/// <param name="Count">Numero de periodos.</param>
public sealed record AcademicTermResponse(string Unit, int Count);

/// <summary>Carrera elegida.</summary>
/// <param name="Slug">Identificador.</param>
/// <param name="Name">Nombre oficial.</param>
/// <param name="ShortName">Lo que muestra el perfil.</param>
/// <param name="FirstTerm">Primer periodo.</param>
/// <param name="LastTerm">Ultimo periodo.</param>
public sealed record CareerResponse(string Slug, string Name, string ShortName, int FirstTerm, int LastTerm);

/// <summary>"Quien te ve".</summary>
/// <param name="Career">Mostrar mi carrera.</param>
/// <param name="Sales">Mostrar mis ventas.</param>
/// <param name="Online">Mostrar si estoy en linea.</param>
public sealed record VisibilityResponse(bool Career, bool Sales, bool Online);

/// <summary>Estadisticas de mi perfil.</summary>
/// <param name="RatingAvg">Promedio (1 a 5) o <c>null</c> sin calificaciones.</param>
/// <param name="RatingCount">Calificaciones.</param>
/// <param name="Sales">Ventas.</param>
/// <param name="Purchases">Compras.</param>
public sealed record MyStatsResponse(double? RatingAvg, int RatingCount, int Sales, int Purchases);

/// <summary>Mi perfil (maquetas <c>Perfil</c>, <c>Ajustes</c> y <c>Privacidad</c>).</summary>
/// <param name="Id">Id del usuario.</param>
/// <param name="FullName">Nombre (no se edita).</param>
/// <param name="Email">Correo institucional (no se edita).</param>
/// <param name="EmailVerifiedAt">Cuando se verifico.</param>
/// <param name="Campus">Campus.</param>
/// <param name="AcademicTerm">Periodo del campus.</param>
/// <param name="Career">Carrera o <c>null</c>.</param>
/// <param name="Term">Periodo o <c>null</c>.</param>
/// <param name="Photo">Ruta de la foto (<c>/media/...</c>) o <c>null</c>.</param>
/// <param name="Visibility">"Quien te ve".</param>
/// <param name="Stats">Estadisticas.</param>
public sealed record MyProfileResponse(
    string Id,
    string FullName,
    string Email,
    DateTime? EmailVerifiedAt,
    CampusResponse Campus,
    AcademicTermResponse AcademicTerm,
    CareerResponse? Career,
    int? Term,
    string? Photo,
    VisibilityResponse Visibility,
    MyStatsResponse Stats);

/// <summary>Estadisticas visibles para el campus.</summary>
/// <param name="RatingAvg">Promedio o <c>null</c>.</param>
/// <param name="RatingCount">Calificaciones.</param>
/// <param name="Sales">Ventas, o <c>null</c> si las oculta.</param>
/// <param name="OnTime">"A tiempo" de <c>Perfil-vendedor</c>: porcentaje (0 a 100) de calificaciones con "Puntual", o <c>null</c> sin calificaciones (HU-27).</param>
public sealed record PublicStatsResponse(double? RatingAvg, int RatingCount, int? Sales, int? OnTime);

/// <summary>Perfil de otra persona del campus (NOTE (HU-27): reputacion y opiniones).</summary>
/// <param name="Id">Id del usuario.</param>
/// <param name="FullName">Nombre.</param>
/// <param name="Campus">Campus.</param>
/// <param name="Photo">Ruta de la foto o <c>null</c>.</param>
/// <param name="MemberSince">Desde cuando esta en Musubi.</param>
/// <param name="Career">Carrera, o <c>null</c> si no la tiene o la oculta.</param>
/// <param name="Term">Periodo, o <c>null</c> si no lo tiene o oculta la carrera.</param>
/// <param name="Stats">Estadisticas.</param>
public sealed record PublicProfileResponse(
    string Id,
    string FullName,
    CampusResponse Campus,
    string? Photo,
    DateTime MemberSince,
    CareerResponse? Career,
    int? Term,
    PublicStatsResponse Stats);

/// <summary>Carrera que se puede elegir.</summary>
/// <param name="Slug">Identificador.</param>
/// <param name="Name">Nombre oficial.</param>
/// <param name="ShortName">Lo que muestra el perfil.</param>
/// <param name="Level"><c>tsu</c>, <c>engineering</c> o <c>bachelor</c>.</param>
/// <param name="FirstTerm">Primer periodo.</param>
/// <param name="LastTerm">Ultimo periodo.</param>
public sealed record CareerOption(string Slug, string Name, string ShortName, string Level, int FirstTerm, int LastTerm);

/// <summary>Carreras del campus y su periodo.</summary>
/// <param name="AcademicTerm">Periodo del campus.</param>
/// <param name="Careers">Carreras activas, en su orden.</param>
public sealed record CareersResponse(AcademicTermResponse AcademicTerm, IReadOnlyList<CareerOption> Careers);
