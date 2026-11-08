using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Profiles.Data;

/// <summary>Carreras en MongoDB (indice <c>ux_community_slug</c>).</summary>
internal sealed class MongoCareerRepository(IMongoDatabase database) : ICareerRepository
{
    private readonly IMongoCollection<CareerDocument> careers = database.GetCollection<CareerDocument>(MongoSchema.Careers);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CareerDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken) =>
        await careers.Find(c => c.CommunityId == communityId && c.Active)
            .SortBy(c => c.Order)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>
/// Perfiles en MongoDB (<c>_id</c> = id del usuario). Cada escritura es un solo upsert atomico: el primer
/// cambio crea el documento con los valores por defecto (<c>$setOnInsert</c>) sin leerlo antes.
/// </summary>
internal sealed class MongoProfileRepository(IMongoDatabase database) : IProfileRepository
{
    private static readonly UpdateDefinitionBuilder<ProfileDocument> Update = Builders<ProfileDocument>.Update;

    private readonly IMongoCollection<ProfileDocument> profiles = database.GetCollection<ProfileDocument>(MongoSchema.Profiles);

    /// <inheritdoc />
    public async Task<ProfileDocument?> FindAsync(string userId, CancellationToken cancellationToken) =>
        await profiles.Find(p => p.Id == userId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProfileDocument>> FindManyAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        // Solo ids validos: un id mal formado en Filter.In haria fallar la consulta completa.
        var ids = userIds.Where(id => ObjectId.TryParse(id, out _)).Distinct(StringComparer.Ordinal).ToList();
        return ids.Count == 0
            ? []
            : await profiles.Find(Builders<ProfileDocument>.Filter.In(p => p.Id, ids)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ProfileDocument> SetDetailsAsync(ProfileOwner owner, string? careerSlug, int? term, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        // Un campo vacio se quita ($unset): el $jsonSchema no acepta null.
        var update = Update.Combine(
            careerSlug is null ? Update.Unset(p => p.CareerSlug) : Update.Set(p => p.CareerSlug, careerSlug),
            term is null ? Update.Unset(p => p.Term) : Update.Set(p => p.Term, term),
            Update.SetOnInsert(p => p.Visibility, new ProfileVisibility()),
            Defaults(owner, now));
        return UpsertAsync(owner.UserId, update, ReturnDocument.After, cancellationToken)!;
    }

    /// <inheritdoc />
    public Task<ProfileDocument> SetVisibilityAsync(ProfileOwner owner, ProfileVisibility visibility, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var update = Update.Combine(Update.Set(p => p.Visibility, visibility), Defaults(owner, now));
        return UpsertAsync(owner.UserId, update, ReturnDocument.After, cancellationToken)!;
    }

    /// <inheritdoc />
    public async Task<string?> SetAvatarAsync(ProfileOwner owner, string? avatarKey, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var update = Update.Combine(
            avatarKey is null ? Update.Unset(p => p.AvatarKey) : Update.Set(p => p.AvatarKey, avatarKey),
            Update.SetOnInsert(p => p.Visibility, new ProfileVisibility()),
            Defaults(owner, now));
        var before = await UpsertAsync(owner.UserId, update, ReturnDocument.Before, cancellationToken).ConfigureAwait(false);
        return before?.AvatarKey;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string userId, CancellationToken cancellationToken) =>
        profiles.DeleteOneAsync(p => p.Id == userId, cancellationToken);

    // Lo que un perfil nuevo necesita y ninguna escritura cambia (visibilidad aparte: una la cambia).
    private static UpdateDefinition<ProfileDocument> Defaults(ProfileOwner owner, DateTime now) => Update.Combine(
        Update.Set(p => p.UpdatedAt, now),
        Update.SetOnInsert(p => p.CommunityId, owner.CommunityId),
        Update.SetOnInsert(p => p.FullName, owner.FullName),
        Update.SetOnInsert(p => p.Stats, new ProfileStats()),
        Update.SetOnInsert(p => p.SchemaVersion, 1));

    private Task<ProfileDocument?> UpsertAsync(string userId, UpdateDefinition<ProfileDocument> update, ReturnDocument returnDocument, CancellationToken cancellationToken) =>
        profiles.FindOneAndUpdateAsync<ProfileDocument?>(
            Builders<ProfileDocument>.Filter.Eq(p => p.Id, userId),
            update,
            new FindOneAndUpdateOptions<ProfileDocument, ProfileDocument?> { IsUpsert = true, ReturnDocument = returnDocument },
            cancellationToken);
}
