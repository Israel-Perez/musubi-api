using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Profiles.Data;

internal interface IPreferenceRepository
{
    Task<PreferenceDocument?> FindAsync(string userId, CancellationToken cancellationToken);

    Task<PreferenceDocument> SetThemeAsync(string userId, string theme, CancellationToken cancellationToken);

    Task<PreferenceDocument> SetInterestsAsync(string userId, IReadOnlyList<string> interests, CancellationToken cancellationToken);

    Task DeleteAsync(string userId, CancellationToken cancellationToken);
}

internal sealed class MongoPreferenceRepository(IMongoDatabase database) : IPreferenceRepository
{
    private readonly IMongoCollection<PreferenceDocument> preferences = database.GetCollection<PreferenceDocument>(MongoSchema.Preferences);

    public async Task<PreferenceDocument?> FindAsync(string userId, CancellationToken cancellationToken) =>
        await preferences.Find(p => p.UserId == userId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task<PreferenceDocument> SetThemeAsync(string userId, string theme, CancellationToken cancellationToken) =>
        await preferences.FindOneAndUpdateAsync(
            p => p.UserId == userId,
            Builders<PreferenceDocument>.Update.SetOnInsert(p => p.UserId, userId)
                .SetOnInsert(p => p.SchemaVersion, 1)
                .Set(p => p.Theme, theme),
            new FindOneAndUpdateOptions<PreferenceDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);

    // El tema por defecto entra solo al crear el documento: el validador de db/018 lo exige y no pisa el elegido.
    public async Task<PreferenceDocument> SetInterestsAsync(string userId, IReadOnlyList<string> interests, CancellationToken cancellationToken) =>
        await preferences.FindOneAndUpdateAsync(
            p => p.UserId == userId,
            Builders<PreferenceDocument>.Update.SetOnInsert(p => p.UserId, userId)
                .SetOnInsert(p => p.SchemaVersion, 1)
                .SetOnInsert(p => p.Theme, "system")
                .Set(p => p.Interests, interests),
            new FindOneAndUpdateOptions<PreferenceDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);

    public Task DeleteAsync(string userId, CancellationToken cancellationToken) =>
        preferences.DeleteOneAsync(p => p.UserId == userId, cancellationToken);
}

internal sealed class InMemoryPreferenceRepository : IPreferenceRepository
{
    private readonly Lock gate = new();
    private readonly System.Collections.Generic.Dictionary<string, PreferenceDocument> preferences = new(System.StringComparer.Ordinal);

    public Task<PreferenceDocument?> FindAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(preferences.TryGetValue(userId, out var preference) ? preference : null);
        }
    }

    public Task<PreferenceDocument> SetThemeAsync(string userId, string theme, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var interests = preferences.TryGetValue(userId, out var current) ? current.Interests : [];
            var document = new PreferenceDocument { UserId = userId, Theme = theme, Interests = interests };
            preferences[userId] = document;
            return Task.FromResult(document);
        }
    }

    public Task<PreferenceDocument> SetInterestsAsync(string userId, IReadOnlyList<string> interests, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var theme = preferences.TryGetValue(userId, out var current) ? current.Theme : "system";
            var document = new PreferenceDocument { UserId = userId, Theme = theme, Interests = [.. interests] };
            preferences[userId] = document;
            return Task.FromResult(document);
        }
    }

    public Task DeleteAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            preferences.Remove(userId);
        }

        return Task.CompletedTask;
    }
}
