using Musubi.Api.Modules.Listings.Data;

namespace UnitTest.Listings;

/// <summary>HU-10: el cursor compuesto es la frontera de confianza de la paginacion; valida orden, precio e id.</summary>
public sealed class ProductCursorCodecTests
{
    private const string Id = "6716a0000000000000000099";

    private const long MaxPriceCents = 99_999_900;

    [Theory]
    [InlineData(ProductSorts.Newest)]
    [InlineData(ProductSorts.Relevance)]
    [InlineData(ProductSorts.Oldest)]
    [InlineData(ProductSorts.PriceAsc)]
    [InlineData(ProductSorts.PriceDesc)]
    public void RoundTrip_PreservesSortPriceAndId(string sort)
    {
        var expected = new ProductCursor(sort, ProductSorts.IsPriceBased(sort) ? 45_000 : null, Id);

        var token = ProductCursorCodec.Encode(expected);

        Assert.True(ProductCursorCodec.TryDecode(token, sort, MaxPriceCents, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(ProductSorts.Newest)]
    [InlineData(ProductSorts.Relevance)]
    public void LegacyRawObjectId_IsAcceptedOnlyForNewestAndRelevance(string sort)
    {
        Assert.True(ProductCursorCodec.TryDecode(Id, sort, MaxPriceCents, out var cursor));
        Assert.Equal(new ProductCursor(sort, null, Id), cursor);
    }

    [Theory]
    [InlineData(ProductSorts.Oldest)]
    [InlineData(ProductSorts.PriceAsc)]
    [InlineData(ProductSorts.PriceDesc)]
    public void LegacyRawObjectId_IsRejectedForOtherSorts(string sort)
    {
        Assert.False(ProductCursorCodec.TryDecode(Id, sort, MaxPriceCents, out _));
    }

    [Fact]
    public void TokenFromAnotherSort_IsRejected()
    {
        var token = ProductCursorCodec.Encode(new ProductCursor(ProductSorts.PriceAsc, 45_000, Id));

        Assert.False(ProductCursorCodec.TryDecode(token, ProductSorts.PriceDesc, MaxPriceCents, out _));
        Assert.False(ProductCursorCodec.TryDecode(token, ProductSorts.Newest, MaxPriceCents, out _));
    }

    [Theory]
    [InlineData("priceAsc:abc:" + Id)]
    [InlineData("priceAsc:100000000:" + Id)]
    [InlineData("priceAsc:45000:no-es-un-id")]
    [InlineData("priceAsc:45000")]
    [InlineData("priceAsc:45000:" + Id + ":extra")]
    [InlineData("newest:no-es-un-id")]
    [InlineData("newest:" + Id + ":extra")]
    public void MalformedToken_IsRejected(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        Assert.False(ProductCursorCodec.TryDecode(token, token.Split(':')[0], MaxPriceCents, out _));
    }

    [Fact]
    public void UppercaseObjectId_IsCanonicalized()
    {
        Assert.True(ProductCursorCodec.TryDecode(Id.ToUpperInvariant(), ProductSorts.Newest, MaxPriceCents, out var cursor));
        Assert.Equal(Id, cursor.Id);
    }
}
