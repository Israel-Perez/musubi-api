using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Musubi.Api.Modules.Admin.Data;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Admin;

/// <summary>
/// Gestionar las categorias del campus (HU-41, RF-56): agregar, renombrar, desactivar y volver a activar. Las reglas
/// viven en M-03 (<see cref="ICategoryCatalog"/>); aqui se revisa al administrador (RNF-03) y cada cambio va a la
/// bitacora (RNF-04).
/// </summary>
internal sealed partial class CategoryAdminService(
    AdminGate gate,
    ICategoryCatalog catalog,
    IModerationLogRepository log,
    TimeProvider time,
    ILogger<CategoryAdminService> logger)
{
    /// <summary>Las categorias del campus del administrador, tambien las desactivadas.</summary>
    /// <param name="adminId">Administrador (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las categorias o el error.</returns>
    public async Task<Result<AdminCategoriesResponse>> ListAsync(string adminId, CancellationToken cancellationToken)
    {
        if (await gate.ActiveAdminAsync(adminId, cancellationToken).ConfigureAwait(false) is not { } admin)
        {
            return ApiErrors.Forbidden;
        }

        var all = await catalog.ListAsync(admin.CommunityId, cancellationToken).ConfigureAwait(false);
        return Result<AdminCategoriesResponse>.Ok(new AdminCategoriesResponse(all.Select(View).ToList()));
    }

    /// <summary>"Agregar categoria".</summary>
    /// <param name="adminId">Administrador (del token).</param>
    /// <param name="request">El nombre.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La categoria o el error.</returns>
    public async Task<Result<AdminCategoryResponse>> CreateAsync(string adminId, CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await gate.ActiveAdminAsync(adminId, cancellationToken).ConfigureAwait(false) is not { } admin)
        {
            return ApiErrors.Forbidden;
        }

        var change = await catalog.CreateAsync(admin.CommunityId, request.Name, cancellationToken).ConfigureAwait(false);
        if (ErrorOf(change.Error) is { } error)
        {
            return error;
        }

        var created = change.After!;
        await LogAsync(admin, ModerationActions.CategoryCreated, created, created.Name, cancellationToken).ConfigureAwait(false);
        LogChanged(logger, created.Slug, admin.Id, ModerationActions.CategoryCreated);
        return Result<AdminCategoryResponse>.Ok(View(created));
    }

    /// <summary>Renombrar, desactivar o volver a activar.</summary>
    /// <param name="adminId">Administrador (del token).</param>
    /// <param name="slug">Categoria.</param>
    /// <param name="request">Lo que cambia.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La categoria o el error.</returns>
    public async Task<Result<AdminCategoryResponse>> UpdateAsync(
        string adminId, string slug, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await gate.ActiveAdminAsync(adminId, cancellationToken).ConfigureAwait(false) is not { } admin)
        {
            return ApiErrors.Forbidden;
        }

        if (request.Name is null && request.Active is null)
        {
            return AdminErrors.NoCategoryChanges;
        }

        var change = await catalog.UpdateAsync(admin.CommunityId, slug, request.Name, request.Active, cancellationToken).ConfigureAwait(false);
        if (ErrorOf(change.Error) is { } error)
        {
            return error;
        }

        var (before, after) = (change.Before!, change.After!);
        if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
        {
            await LogAsync(admin, ModerationActions.CategoryRenamed, after, before.Name, cancellationToken).ConfigureAwait(false);
            LogChanged(logger, after.Slug, admin.Id, ModerationActions.CategoryRenamed);
        }

        if (before.Active != after.Active)
        {
            var action = after.Active ? ModerationActions.CategoryReactivated : ModerationActions.CategoryDeactivated;
            await LogAsync(admin, action, after, null, cancellationToken).ConfigureAwait(false);
            LogChanged(logger, after.Slug, admin.Id, action);
        }

        return Result<AdminCategoryResponse>.Ok(View(after));
    }

    private static ApiError? ErrorOf(CategoryChangeError error) => error switch
    {
        CategoryChangeError.None => null,
        CategoryChangeError.InvalidName => AdminErrors.InvalidCategoryName,
        CategoryChangeError.Exists => AdminErrors.CategoryExists,
        CategoryChangeError.NotFound => AdminErrors.CategoryNotFound,
        CategoryChangeError.LastActive => AdminErrors.LastCategory,
        CategoryChangeError.Limit => AdminErrors.CategoryLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Error de categoria sin codigo."),
    };

    private static AdminCategoryResponse View(ManagedCategory c) => new(c.Slug, c.Name, c.Active, c.Order);

    private Task LogAsync(ModeratedAccount admin, string action, ManagedCategory category, string? reason, CancellationToken cancellationToken) =>
        log.InsertAsync(
            new ModerationLogDocument
            {
                Id = MongoConventions.NewId(),
                CommunityId = admin.CommunityId,
                ActorId = admin.Id,
                Action = action,
                TargetType = ModerationTargets.Category,
                TargetId = category.Id,
                Reason = reason,
                At = time.GetUtcNow().UtcDateTime,
            },
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Categoria {Slug} cambiada por {AdminId}: {Action}")]
    private static partial void LogChanged(ILogger logger, string slug, string adminId, string action);
}
