using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OutputCaching;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Caching;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Listings;

/// <summary>Una categoria como la administra M-14 (HU-41), tambien si esta desactivada.</summary>
/// <param name="Id">Id (para la bitacora).</param>
/// <param name="Slug">Identificador estable: lo usan los productos y las solicitudes, no cambia.</param>
/// <param name="Name">Nombre visible.</param>
/// <param name="Order">Orden en la app.</param>
/// <param name="Active">Se puede elegir al publicar y filtrar.</param>
internal sealed record ManagedCategory(string Id, string Slug, string Name, int Order, bool Active);

/// <summary>Por que no se pudo cambiar una categoria.</summary>
internal enum CategoryChangeError
{
    /// <summary>Sin error.</summary>
    None,

    /// <summary>Nombre vacio, corto o largo.</summary>
    InvalidName,

    /// <summary>Ya hay una categoria con ese nombre (tambien desactivada).</summary>
    Exists,

    /// <summary>No hay una categoria con ese slug en el campus.</summary>
    NotFound,

    /// <summary>Desactivar la ultima activa dejaria "Vender-3" sin opciones.</summary>
    LastActive,

    /// <summary>El campus ya tiene <see cref="CategoryCatalog.MaxCategories"/>.</summary>
    Limit,
}

/// <summary>Resultado de crear o cambiar una categoria.</summary>
/// <param name="Before">Como estaba (<c>null</c> al crear o con error).</param>
/// <param name="After">Como quedo (<c>null</c> con error).</param>
/// <param name="Error">El error, o <see cref="CategoryChangeError.None"/>.</param>
internal sealed record CategoryChange(ManagedCategory? Before, ManagedCategory? After, CategoryChangeError Error)
{
    /// <summary>Un cambio que no se pudo hacer.</summary>
    /// <param name="error">Por que.</param>
    /// <returns>El resultado.</returns>
    public static CategoryChange Failed(CategoryChangeError error) => new(null, null, error);
}

/// <summary>Contrato publico de M-03 para M-14: administrar las categorias del campus (HU-41).</summary>
internal interface ICategoryCatalog
{
    /// <summary>Todas las categorias del campus, en su orden.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las categorias.</returns>
    Task<IReadOnlyList<ManagedCategory>> ListAsync(string communityId, CancellationToken cancellationToken);

    /// <summary>"Agregar categoria": activa y al final.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="name">Nombre escrito por el administrador.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La categoria o el error.</returns>
    Task<CategoryChange> CreateAsync(string communityId, string? name, CancellationToken cancellationToken);

    /// <summary>Renombrar, desactivar o volver a activar.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="slug">Categoria.</param>
    /// <param name="name">Nombre nuevo, o <c>null</c> para dejarlo.</param>
    /// <param name="active">Estado nuevo, o <c>null</c> para dejarlo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El cambio o el error.</returns>
    Task<CategoryChange> UpdateAsync(string communityId, string slug, string? name, bool? active, CancellationToken cancellationToken);
}

/// <summary>
/// Reglas de las categorias (HU-41). El slug sale del nombre al crearla y no cambia: los productos, las solicitudes,
/// los intereses y las busquedas guardadas lo guardan. Al cambiar una, se borran las caches de "Vender-3", "Filtrar"
/// y la vitrina (en esta instancia; la otra la ve a mas tardar en su TTL).
/// </summary>
internal sealed class CategoryCatalog(ICategoryRepository categories, IOutputCacheStore outputCache) : ICategoryCatalog
{
    /// <summary>Largo minimo del nombre (validador de <c>db/002-publicaciones.js</c>).</summary>
    public const int MinNameLength = 2;

    /// <summary>Largo maximo del nombre y del slug (validador de <c>db/002-publicaciones.js</c>).</summary>
    public const int MaxNameLength = 40;

    /// <summary>
    /// Categorias por campus, tambien desactivadas: "Vender-3" y "Filtrar" las muestran todas como chips, sin
    /// paginar, y la maqueta tiene 6.
    /// </summary>
    public const int MaxCategories = 30;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ManagedCategory>> ListAsync(string communityId, CancellationToken cancellationToken) =>
        (await categories.ListAllAsync(communityId, cancellationToken).ConfigureAwait(false)).Select(Managed).ToList();

    /// <inheritdoc />
    public async Task<CategoryChange> CreateAsync(string communityId, string? name, CancellationToken cancellationToken)
    {
        if (CleanName(name) is not { } clean)
        {
            return CategoryChange.Failed(CategoryChangeError.InvalidName);
        }

        var all = await categories.ListAllAsync(communityId, cancellationToken).ConfigureAwait(false);
        var slug = SlugOf(clean);
        if (all.Any(c => SameName(c, slug)))
        {
            return CategoryChange.Failed(CategoryChangeError.Exists);
        }

        if (all.Count >= MaxCategories)
        {
            return CategoryChange.Failed(CategoryChangeError.Limit);
        }

        var category = new CategoryDocument
        {
            Id = MongoConventions.NewId(),
            CommunityId = communityId,
            Slug = slug,
            Name = clean,
            Order = all.Count == 0 ? 1 : all.Max(c => c.Order) + 1,
            Active = true,
        };
        if (!await categories.TryInsertAsync(category, cancellationToken).ConfigureAwait(false))
        {
            // Otro administrador la agrego al mismo tiempo (ux_community_slug).
            return CategoryChange.Failed(CategoryChangeError.Exists);
        }

        await EvictAsync(cancellationToken).ConfigureAwait(false);
        return new CategoryChange(null, Managed(category), CategoryChangeError.None);
    }

    /// <inheritdoc />
    public async Task<CategoryChange> UpdateAsync(string communityId, string slug, string? name, bool? active, CancellationToken cancellationToken)
    {
        var all = await categories.ListAllAsync(communityId, cancellationToken).ConfigureAwait(false);
        if (all.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.Ordinal)) is not { } current)
        {
            return CategoryChange.Failed(CategoryChangeError.NotFound);
        }

        var newName = current.Name;
        if (name is not null)
        {
            if (CleanName(name) is not { } clean)
            {
                return CategoryChange.Failed(CategoryChangeError.InvalidName);
            }

            var newSlug = SlugOf(clean);
            if (all.Any(c => !ReferenceEquals(c, current) && SameName(c, newSlug)))
            {
                return CategoryChange.Failed(CategoryChangeError.Exists);
            }

            newName = clean;
        }

        var newActive = active ?? current.Active;
        if (current.Active && !newActive && all.Count(c => c.Active) == 1)
        {
            return CategoryChange.Failed(CategoryChangeError.LastActive);
        }

        if (string.Equals(newName, current.Name, StringComparison.Ordinal) && newActive == current.Active)
        {
            return new CategoryChange(Managed(current), Managed(current), CategoryChangeError.None);
        }

        if (await categories.UpdateAsync(communityId, slug, newName, newActive, cancellationToken).ConfigureAwait(false) is not { } updated)
        {
            return CategoryChange.Failed(CategoryChangeError.NotFound);
        }

        await EvictAsync(cancellationToken).ConfigureAwait(false);
        return new CategoryChange(Managed(current), Managed(updated), CategoryChangeError.None);
    }

    /// <summary>
    /// Slug del nombre: minusculas, sin acentos y con guiones (<c>Ropa y batas</c> → <c>ropa-y-batas</c>), hasta
    /// <see cref="MaxNameLength"/> caracteres (puede quedar vacio: lo revisa <see cref="CleanName"/>).
    /// </summary>
    /// <param name="name">Nombre ya limpio.</param>
    /// <returns>El slug.</returns>
    public static string SlugOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var slug = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                slug.Append(c);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var text = slug.ToString().Trim('-');
        return text.Length > MaxNameLength ? text[..MaxNameLength].TrimEnd('-') : text;
    }

    /// <summary>Nombre sin espacios de mas, con 2 a 40 caracteres y al menos 2 letras o numeros; si no, <c>null</c>.</summary>
    /// <param name="name">Nombre escrito.</param>
    /// <returns>El nombre limpio o <c>null</c>.</returns>
    public static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var clean = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length is >= MinNameLength and <= MaxNameLength && SlugOf(clean).Length >= MinNameLength ? clean : null;
    }

    // "Electronica" y "Electrónica" son la misma: se compara contra el slug y el nombre de cada una.
    private static bool SameName(CategoryDocument category, string slug) =>
        string.Equals(category.Slug, slug, StringComparison.Ordinal)
        || string.Equals(SlugOf(category.Name), slug, StringComparison.Ordinal);

    private static ManagedCategory Managed(CategoryDocument c) => new(c.Id, c.Slug, c.Name, c.Order, c.Active);

    private async Task EvictAsync(CancellationToken cancellationToken)
    {
        await outputCache.EvictByTagAsync(CachingServiceCollectionExtensions.CatalogTag, cancellationToken).ConfigureAwait(false);
        // Las tarjetas de la vitrina traen el nombre de la categoria.
        await outputCache.EvictByTagAsync(CachingServiceCollectionExtensions.ShowcaseTag, cancellationToken).ConfigureAwait(false);
    }
}
