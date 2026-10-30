using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Auth.Data;

// Implementaciones en memoria para UseInMemoryDatabase=true (dotnet run sin MongoDB y suites WAF).
// Reproducen las reglas del servidor que importan al negocio (indices unicos, filtro de vencimiento,
// operaciones atomicas); TestShared/Auth verifica que se comporten igual que las de Mongo.
// Los documentos son inmutables (init): cada cambio reemplaza la entrada bajo el candado.

/// <summary>Comunidades en memoria, con la misma semilla que db/001-identidad.js y db/003-dominio-revision.js.</summary>
internal sealed class InMemoryCommunityRepository : ICommunityRepository
{
    /// <summary>Id fijo de la UTTT en memoria (en Mongo el id lo genera el script).</summary>
    public const string UtttId = "000000000000000000000001";

    private readonly CommunityDocument[] communities =
    [
        new()
        {
            Id = UtttId,
            Slug = "uttt",
            Name = "Universidad Tecnológica de Tula-Tepeji",
            ShortName = "UTTT",
            EmailDomains = ["uttt.edu.mx", "musubiapp.com.mx"],
            Status = CommunityStatuses.Active,
            CreatedAt = DateTime.UnixEpoch,
        },
    ];

    /// <inheritdoc />
    public Task<CommunityDocument?> FindByEmailDomainAsync(string emailDomain, CancellationToken cancellationToken) =>
        Task.FromResult(communities.FirstOrDefault(c => c.EmailDomains.Contains(emailDomain, StringComparer.Ordinal)));

    /// <inheritdoc />
    public Task<CommunityDocument?> FindByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(communities.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal)));

    /// <inheritdoc />
    public Task<CommunityDocument?> FindBySlugAsync(string slug, CancellationToken cancellationToken) =>
        Task.FromResult(communities.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.Ordinal)));
}

/// <summary>Usuarios en memoria (correo normalizado unico).</summary>
internal sealed class InMemoryUserRepository : IUserRepository
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, UserDocument> users = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> TryInsertAsync(UserDocument user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (gate)
        {
            if (users.Values.Any(u => string.Equals(u.EmailNormalized, user.EmailNormalized, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            users[user.Id] = user;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<UserDocument?> FindByIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(users.GetValueOrDefault(id));
        }
    }

    /// <inheritdoc />
    public Task<UserDocument?> FindByEmailAsync(string emailNormalized, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(users.Values.FirstOrDefault(u => string.Equals(u.EmailNormalized, emailNormalized, StringComparison.Ordinal)));
        }
    }

    /// <inheritdoc />
    public Task<bool> MarkEmailVerifiedAsync(string id, DateTime verifiedAt, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!users.TryGetValue(id, out var user) || user.Status != UserStatuses.PendingVerification)
            {
                return Task.FromResult(false);
            }

            users[id] = new UserDocument
            {
                Id = user.Id,
                CommunityId = user.CommunityId,
                Email = user.Email,
                EmailNormalized = user.EmailNormalized,
                PasswordHash = user.PasswordHash,
                FullName = user.FullName,
                Status = UserStatuses.Active,
                EmailVerifiedAt = verifiedAt,
                Consents = user.Consents,
                Roles = user.Roles,
                SchemaVersion = user.SchemaVersion,
                CreatedAt = user.CreatedAt,
                UpdatedAt = verifiedAt,
            };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> UpdatePasswordHashAsync(string id, string passwordHash, DateTime updatedAt, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!users.TryGetValue(id, out var user))
            {
                return Task.FromResult(false);
            }

            users[id] = new UserDocument
            {
                Id = user.Id,
                CommunityId = user.CommunityId,
                Email = user.Email,
                EmailNormalized = user.EmailNormalized,
                PasswordHash = passwordHash,
                FullName = user.FullName,
                Status = user.Status,
                EmailVerifiedAt = user.EmailVerifiedAt,
                Consents = user.Consents,
                Roles = user.Roles,
                SchemaVersion = user.SchemaVersion,
                CreatedAt = user.CreatedAt,
                UpdatedAt = updatedAt,
            };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(users.Remove(id));
        }
    }
}

/// <summary>Codigos de verificacion en memoria (uno por usuario y proposito).</summary>
internal sealed class InMemoryVerificationCodeRepository : IVerificationCodeRepository
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, VerificationCodeDocument> codes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task UpsertAsync(VerificationCodeDocument code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        lock (gate)
        {
            var existing = codes.Values.FirstOrDefault(c => SameOwner(c, code.UserId, code.Purpose));
            if (existing is not null)
            {
                codes.Remove(existing.Id);
            }

            codes[existing?.Id ?? code.Id] = new VerificationCodeDocument
            {
                Id = existing?.Id ?? code.Id,
                UserId = code.UserId,
                Purpose = code.Purpose,
                CodeHash = code.CodeHash,
                Attempts = 0,
                LastSentAt = code.LastSentAt,
                ExpiresAt = code.ExpiresAt,
                SchemaVersion = code.SchemaVersion,
                CreatedAt = existing?.CreatedAt ?? code.CreatedAt,
            };
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<VerificationCodeDocument?> FindActiveAsync(string userId, string purpose, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(codes.Values.FirstOrDefault(c => SameOwner(c, userId, purpose) && c.ExpiresAt > now));
        }
    }

    /// <inheritdoc />
    public Task<int?> IncrementAttemptsAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!codes.TryGetValue(id, out var code))
            {
                return Task.FromResult<int?>(null);
            }

            codes[id] = new VerificationCodeDocument
            {
                Id = code.Id,
                UserId = code.UserId,
                Purpose = code.Purpose,
                CodeHash = code.CodeHash,
                Attempts = code.Attempts + 1,
                LastSentAt = code.LastSentAt,
                ExpiresAt = code.ExpiresAt,
                SchemaVersion = code.SchemaVersion,
                CreatedAt = code.CreatedAt,
            };
            return Task.FromResult<int?>(code.Attempts + 1);
        }
    }

    /// <inheritdoc />
    public Task DeleteAllForUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            foreach (var code in codes.Values.Where(c => string.Equals(c.UserId, userId, StringComparison.Ordinal)).ToList())
            {
                codes.Remove(code.Id);
            }

            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            codes.Remove(id);
            return Task.CompletedTask;
        }
    }

    private static bool SameOwner(VerificationCodeDocument code, string userId, string purpose) =>
        string.Equals(code.UserId, userId, StringComparison.Ordinal) && string.Equals(code.Purpose, purpose, StringComparison.Ordinal);
}

/// <summary>Refresh tokens en memoria (hash unico; consumo atomico bajo candado).</summary>
internal sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, RefreshTokenDocument> tokens = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task InsertAsync(RefreshTokenDocument token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (gate)
        {
            if (!tokens.TryAdd(token.TokenHash, token))
            {
                throw new InvalidOperationException("Hash de refresh token duplicado.");
            }

            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<RefreshTokenDocument?> TryConsumeAsync(string tokenHash, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!tokens.TryGetValue(tokenHash, out var token) || token.UsedAt is not null || token.RevokedAt is not null || token.ExpiresAt <= now)
            {
                return Task.FromResult<RefreshTokenDocument?>(null);
            }

            tokens[tokenHash] = With(token, usedAt: now, revokedAt: token.RevokedAt);
            return Task.FromResult<RefreshTokenDocument?>(token);
        }
    }

    /// <inheritdoc />
    public Task<RefreshTokenDocument?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(tokens.GetValueOrDefault(tokenHash));
        }
    }

    /// <inheritdoc />
    public Task<long> RevokeFamilyAsync(string familyId, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Revoke(t => string.Equals(t.FamilyId, familyId, StringComparison.Ordinal), now));

    /// <inheritdoc />
    public Task<long> RevokeAllForUserAsync(string userId, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Revoke(t => string.Equals(t.UserId, userId, StringComparison.Ordinal), now));

    /// <inheritdoc />
    public Task<long> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var mine = tokens.Values.Where(t => string.Equals(t.UserId, userId, StringComparison.Ordinal)).ToList();
            foreach (var token in mine)
            {
                tokens.Remove(token.TokenHash);
            }

            return Task.FromResult<long>(mine.Count);
        }
    }

    private long Revoke(Func<RefreshTokenDocument, bool> match, DateTime now)
    {
        lock (gate)
        {
            var targets = tokens.Values.Where(t => match(t) && t.RevokedAt is null).ToList();
            foreach (var token in targets)
            {
                tokens[token.TokenHash] = With(token, usedAt: token.UsedAt, revokedAt: now);
            }

            return targets.Count;
        }
    }

    private static RefreshTokenDocument With(RefreshTokenDocument token, DateTime? usedAt, DateTime? revokedAt) => new()
    {
        Id = token.Id,
        UserId = token.UserId,
        FamilyId = token.FamilyId,
        TokenHash = token.TokenHash,
        ExpiresAt = token.ExpiresAt,
        UsedAt = usedAt,
        RevokedAt = revokedAt,
        Device = token.Device,
        SchemaVersion = token.SchemaVersion,
        CreatedAt = token.CreatedAt,
    };
}

/// <summary>Ids para documentos nuevos (mismo formato en memoria y en Mongo).</summary>
internal static class AuthIds
{
    /// <summary>Id nuevo (ObjectId en texto).</summary>
    /// <returns>El id.</returns>
    public static string New() => MongoConventions.NewId();
}
