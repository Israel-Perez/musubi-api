using Microsoft.AspNetCore.OutputCaching;

namespace UnitTest.PurchaseRequests;

/// <summary>Cache de salida falsa: anota las etiquetas que se borran (HU-15).</summary>
internal sealed class RecordingOutputCacheStore : IOutputCacheStore
{
    public List<string> EvictedTags { get; } = [];

    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        EvictedTags.Add(tag);
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);

    public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
