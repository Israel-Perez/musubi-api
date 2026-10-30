using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Auth.Data;

/// <summary>Comunidades en MongoDB (indice <c>ux_emailDomains</c>).</summary>
internal sealed class MongoCommunityRepository(IMongoDatabase database) : ICommunityRepository
{
    private readonly IMongoCollection<CommunityDocument> communities = database.GetCollection<CommunityDocument>(MongoSchema.Communities);

    /// <inheritdoc />
    public async Task<CommunityDocument?> FindByEmailDomainAsync(string emailDomain, CancellationToken cancellationToken) =>
        await communities.Find(Builders<CommunityDocument>.Filter.AnyEq(c => c.EmailDomains, emailDomain))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<CommunityDocument?> FindByIdAsync(string id, CancellationToken cancellationToken) =>
        await communities.Find(c => c.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<CommunityDocument?> FindBySlugAsync(string slug, CancellationToken cancellationToken) =>
        await communities.Find(c => c.Slug == slug).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Usuarios en MongoDB (indice unico <c>ux_emailNormalized</c>).</summary>
internal sealed class MongoUserRepository(IMongoDatabase database) : IUserRepository
{
    private readonly IMongoCollection<UserDocument> users = database.GetCollection<UserDocument>(MongoSchema.Users);

    /// <inheritdoc />
    public async Task<bool> TryInsertAsync(UserDocument user, CancellationToken cancellationToken)
    {
        try
        {
            await users.InsertOneAsync(user, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // El indice unico resuelve la carrera entre dos registros simultaneos del mismo correo.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<UserDocument?> FindByIdAsync(string id, CancellationToken cancellationToken) =>
        await users.Find(u => u.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<UserDocument?> FindByEmailAsync(string emailNormalized, CancellationToken cancellationToken) =>
        await users.Find(u => u.EmailNormalized == emailNormalized).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> MarkEmailVerifiedAsync(string id, DateTime verifiedAt, CancellationToken cancellationToken)
    {
        var result = await users.UpdateOneAsync(
            u => u.Id == id && u.Status == UserStatuses.PendingVerification,
            Builders<UserDocument>.Update
                .Set(u => u.Status, UserStatuses.Active)
                .Set(u => u.EmailVerifiedAt, verifiedAt)
                .Set(u => u.UpdatedAt, verifiedAt),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    /// <inheritdoc />
    public async Task<bool> UpdatePasswordHashAsync(string id, string passwordHash, DateTime updatedAt, CancellationToken cancellationToken)
    {
        var result = await users.UpdateOneAsync(
            u => u.Id == id,
            Builders<UserDocument>.Update.Set(u => u.PasswordHash, passwordHash).Set(u => u.UpdatedAt, updatedAt),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.MatchedCount == 1;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var result = await users.DeleteOneAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount == 1;
    }
}

/// <summary>Codigos de verificacion en MongoDB (indices <c>ux_user_purpose</c> y TTL).</summary>
internal sealed class MongoVerificationCodeRepository(IMongoDatabase database) : IVerificationCodeRepository
{
    private readonly IMongoCollection<VerificationCodeDocument> codes = database.GetCollection<VerificationCodeDocument>(MongoSchema.VerificationCodes);

    /// <inheritdoc />
    public async Task UpsertAsync(VerificationCodeDocument code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        // $set + $setOnInsert: reemplaza el contenido sin cambiar el _id de un documento existente.
        await codes.UpdateOneAsync(
            c => c.UserId == code.UserId && c.Purpose == code.Purpose,
            Builders<VerificationCodeDocument>.Update
                .SetOnInsert(c => c.Id, code.Id)
                .SetOnInsert(c => c.CreatedAt, code.CreatedAt)
                .Set(c => c.CodeHash, code.CodeHash)
                .Set(c => c.Attempts, 0)
                .Set(c => c.LastSentAt, code.LastSentAt)
                .Set(c => c.ExpiresAt, code.ExpiresAt)
                .Set(c => c.SchemaVersion, code.SchemaVersion),
            new UpdateOptions { IsUpsert = true },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<VerificationCodeDocument?> FindActiveAsync(string userId, string purpose, DateTime now, CancellationToken cancellationToken) =>
        // El monitor TTL borra con retraso (~60 s): el vencimiento tambien se filtra aqui (R6).
        await codes.Find(c => c.UserId == userId && c.Purpose == purpose && c.ExpiresAt > now)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<int?> IncrementAttemptsAsync(string id, CancellationToken cancellationToken)
    {
        var updated = await codes.FindOneAndUpdateAsync(
            c => c.Id == id,
            Builders<VerificationCodeDocument>.Update.Inc(c => c.Attempts, 1),
            new FindOneAndUpdateOptions<VerificationCodeDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);
        return updated?.Attempts;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken) =>
        codes.DeleteOneAsync(c => c.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task DeleteAllForUserAsync(string userId, CancellationToken cancellationToken) =>
        codes.DeleteManyAsync(c => c.UserId == userId, cancellationToken);
}

/// <summary>Refresh tokens en MongoDB (indices <c>ux_tokenHash</c>, <c>ix_family</c>, <c>ix_user</c> y TTL).</summary>
internal sealed class MongoRefreshTokenRepository(IMongoDatabase database) : IRefreshTokenRepository
{
    private readonly IMongoCollection<RefreshTokenDocument> tokens = database.GetCollection<RefreshTokenDocument>(MongoSchema.RefreshTokens);

    /// <inheritdoc />
    public Task InsertAsync(RefreshTokenDocument token, CancellationToken cancellationToken) =>
        tokens.InsertOneAsync(token, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<RefreshTokenDocument?> TryConsumeAsync(string tokenHash, DateTime now, CancellationToken cancellationToken) =>
        await tokens.FindOneAndUpdateAsync(
            t => t.TokenHash == tokenHash && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now,
            Builders<RefreshTokenDocument>.Update.Set(t => t.UsedAt, now),
            new FindOneAndUpdateOptions<RefreshTokenDocument> { ReturnDocument = ReturnDocument.Before },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<RefreshTokenDocument?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        await tokens.Find(t => t.TokenHash == tokenHash).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<long> RevokeFamilyAsync(string familyId, DateTime now, CancellationToken cancellationToken)
    {
        var result = await tokens.UpdateManyAsync(
            t => t.FamilyId == familyId && t.RevokedAt == null,
            Builders<RefreshTokenDocument>.Update.Set(t => t.RevokedAt, now),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount;
    }

    /// <inheritdoc />
    public async Task<long> RevokeAllForUserAsync(string userId, DateTime now, CancellationToken cancellationToken)
    {
        var result = await tokens.UpdateManyAsync(
            t => t.UserId == userId && t.RevokedAt == null,
            Builders<RefreshTokenDocument>.Update.Set(t => t.RevokedAt, now),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount;
    }

    /// <inheritdoc />
    public async Task<long> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken)
    {
        var result = await tokens.DeleteManyAsync(t => t.UserId == userId, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount;
    }
}
