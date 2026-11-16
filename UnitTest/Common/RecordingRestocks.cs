using Musubi.Api.Modules.Listings;

namespace UnitTest.Common;

/// <summary>HU-51: registra las publicaciones que pudieron volver a tener piezas, sin la cola real.</summary>
internal sealed class RecordingRestocks : IProductRestocks
{
    /// <summary>Las publicaciones encoladas, en orden.</summary>
    public List<string> Products { get; } = [];

    /// <inheritdoc />
    public bool Restocked(string productId)
    {
        Products.Add(productId);
        return true;
    }
}
