using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Profiles.Data;

internal interface IPreferenceRepository
{
    Task<PreferenceDocument?> FindAsync(string userId, CancellationToken cancellationToken);

    Task<PreferenceDocument> SetThemeAsync(string userId, string theme, CancellationToken cancellationToken);

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
            return Task.FromResult(preferences.GetValueOrDefault(userId));
        }
    }

    public Task<PreferenceDocument> SetThemeAsync(string userId, string theme, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var document = new PreferenceDocument { UserId = userId, Theme = theme };
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
