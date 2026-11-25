using System.Text.Json;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Matching;

namespace UnitTest.Matching;

/// <summary>
/// HU-60 (criterio "una prueba compara las dos formas con ejemplos reales del campus"): 16 solicitudes y 24
/// publicaciones del campus, con cuales le sirven a cada una, y sus vectores de Gemini (<c>gemini-embedding-001</c>,
/// 256 dimensiones, <c>RETRIEVAL_QUERY</c>/<c>RETRIEVAL_DOCUMENT</c>; <c>scripts/semantic_eval_vectors.ps1</c>). Como en
/// los avisos, se comparan dentro de la categoria. Medido el 25-Nov-2026: por palabras se encuentran 4 de 21 y por
/// significado 18 de 21 (con 6 de mas, contra 2), con mejor precision.
/// </summary>
public sealed class SemanticEvaluationTests
{
    private static readonly Lazy<(EvalSet Data, Dictionary<string, float[]> Vectors)> Set = new(Load);

    [Fact]
    public void BySense_FindsMuchMoreThanByWords_WithBetterPrecision()
    {
        var words = Evaluate(useVectors: false);
        var sense = Evaluate(useVectors: true);

        Assert.Equal((4, 2, 17), (words.Found, words.Wrong, words.Missed));
        Assert.Equal((18, 6, 3), (sense.Found, sense.Wrong, sense.Missed));
        Assert.True(sense.Precision >= words.Precision, $"precision por significado {sense.Precision:0.00} contra {words.Precision:0.00}");
        Assert.True(sense.Recall >= 0.85, $"cobertura por significado {sense.Recall:0.00}");
        Assert.True(words.Recall < 0.25, $"cobertura por palabras {words.Recall:0.00}");
    }

    [Fact]
    public void TheMockupExample_CalculadoraCientifica_FindsTheCasios()
    {
        // El ejemplo de la HU: "calculadora científica" encuentra la "Casio fx-991" sin compartir palabras.
        var (data, vectors) = Set.Value;
        var request = data.Requests.Single(r => r.Id == "r01");
        var casio = data.Products.Single(p => p.Id == "p01");

        Assert.Equal(0, MatchRules.Share(MatchRules.Terms(request.Title), SearchWord.Words(casio.Text)));
        Assert.True(Embeddings.Cosine(vectors["r01"], vectors["p01"]) >= MatchRules.SemanticThreshold);
        // Limite medido: una calculadora basica tambien pasa (es de los 6 de mas) y hasta queda arriba (0.77 contra 0.76),
        // porque dice "calculadora". Por eso los avisos siguen pidiendo la categoria y el presupuesto, y quien busca decide.
        Assert.True(Embeddings.Cosine(vectors["r01"], vectors["p04"]) >= MatchRules.SemanticThreshold);
    }

    private static Score Evaluate(bool useVectors)
    {
        var (data, vectors) = Set.Value;
        int found = 0, wrong = 0, missed = 0;
        foreach (var request in data.Requests)
        {
            var terms = MatchRules.Terms(request.Title);
            foreach (var product in data.Products.Where(p => p.Category == request.Category))
            {
                var share = MatchRules.Share(terms, SearchWord.Words(product.Text));
                double? similarity = useVectors ? Embeddings.Cosine(vectors[request.Id], vectors[product.Id]) : null;
                var matched = MatchRules.Compatible(share, similarity);
                var relevant = request.Relevant.Contains(product.Id);
                if (matched && relevant)
                {
                    found++;
                }
                else if (matched)
                {
                    wrong++;
                }
                else if (relevant)
                {
                    missed++;
                }
            }
        }

        return new Score(found, wrong, missed);
    }

    private static (EvalSet, Dictionary<string, float[]>) Load()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Matching", "Fixtures");
        using var set = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "semantic-eval.json")));
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "semantic-eval-vectors.json")));
        Assert.Equal(
            ("gemini-embedding-001", 256),
            (file.RootElement.GetProperty("model").GetString(), file.RootElement.GetProperty("dimensions").GetInt32()));
        var products = set.RootElement.GetProperty("products").EnumerateArray()
            .Select(p => new EvalProduct(Value(p, "id"), Value(p, "category"), Value(p, "text")))
            .ToList();
        var requests = set.RootElement.GetProperty("requests").EnumerateArray()
            .Select(r => new EvalRequest(Value(r, "id"), Value(r, "category"), Value(r, "title"), [.. r.GetProperty("relevant").EnumerateArray().Select(x => x.GetString()!)]))
            .ToList();
        var vectors = file.RootElement.GetProperty("vectors").EnumerateObject()
            .ToDictionary(v => v.Name, v => v.Value.EnumerateArray().Select(x => x.GetSingle()).ToArray(), StringComparer.Ordinal);
        return (new EvalSet(products, requests), vectors);
    }

    private static string Value(JsonElement element, string name) => element.GetProperty(name).GetString()!;

    private sealed record Score(int Found, int Wrong, int Missed)
    {
        public double Precision => Found + Wrong == 0 ? 0 : Found / (double)(Found + Wrong);

        public double Recall => Found / (double)(Found + Missed);
    }

    private sealed record EvalSet(IReadOnlyList<EvalProduct> Products, IReadOnlyList<EvalRequest> Requests);

    private sealed record EvalProduct(string Id, string Category, string Text);

    private sealed record EvalRequest(string Id, string Category, string Title, IReadOnlyList<string> Relevant);
}
