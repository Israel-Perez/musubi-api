using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Musubi.Api.Modules.Listings.Data;

namespace UnitTest.Data;

/// <summary>HU-09: el documento guarda las palabras normalizadas que consulta el indice de prefijo.</summary>
public sealed class ProductSearchWordsTests
{
    [Fact]
    public void SearchWords_AreSerializedNormalized_AndTheDocumentRoundTrips()
    {
        var product = new ProductDocument
        {
            Id = "6716a00000000000000000aa",
            CommunityId = "000000000000000000000001",
            SellerId = "6716a00000000000000000bb",
            Seller = new ProductSeller { Name = "Ana R." },
            Title = "Calculadora Científica",
            Description = "La usé un semestre para Cálculo.",
            CategorySlug = "calculadoras",
            PriceCents = 45_000,
            Condition = ProductConditions.Used,
            ConditionDetail = "likeNew",
            Stock = 1,
            Status = ProductStatuses.Active,
            Photos = ["media/products/6716a00000000000000000bb/0123456789abcdef0123456789abcdef.webp"],
            CreatedAt = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc),
        };

        var document = product.ToBsonDocument();
        var words = document["searchWords"].AsBsonArray.Select(word => word.AsString).ToList();

        // Sin mayusculas, sin acentos y sin el plural final: lo que consulta el prefijo.
        Assert.Contains("calculadora", words);
        Assert.Contains("cientifica", words);
        Assert.Contains("use", words);
        Assert.Contains("calculo", words);
        Assert.DoesNotContain(words, word => word.Any(char.IsUpper));

        var back = BsonSerializer.Deserialize<ProductDocument>(document);
        Assert.Equal(product.Title, back.Title);
        Assert.Equal(product.SearchWords, back.SearchWords);
    }
}
