using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Microsoft.ML.Transforms.Text;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Recommendations.Data;

namespace Musubi.Api.Modules.Recommendations;

/// <summary>Vector disperso normalizado (TF-IDF de una publicacion o el perfil de una persona).</summary>
internal sealed class SparseVector
{
    private readonly int[] indices;
    private readonly float[] values;

    private SparseVector(int[] indices, float[] values)
    {
        this.indices = indices;
        this.values = values;
    }

    /// <summary>Sin terminos.</summary>
    public static SparseVector Empty { get; } = new([], []);

    /// <summary>Si no tiene terminos.</summary>
    public bool IsEmpty => indices.Length == 0;

    /// <summary>Desde la columna de ML.NET (densa o dispersa).</summary>
    /// <param name="buffer">El vector.</param>
    /// <returns>El vector disperso.</returns>
    public static SparseVector From(VBuffer<float> buffer)
    {
        var raw = buffer.GetValues();
        if (buffer.IsDense)
        {
            var pairs = new SortedDictionary<int, double>();
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] != 0)
                {
                    pairs[i] = raw[i];
                }
            }

            return FromPairs(pairs);
        }

        var at = buffer.GetIndices();
        var sorted = new SortedDictionary<int, double>();
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != 0)
            {
                sorted[at[i]] = raw[i];
            }
        }

        return FromPairs(sorted);
    }

    /// <summary>Normaliza (L2) una suma de terminos.</summary>
    /// <param name="pairs">Indice → valor.</param>
    /// <returns>El vector.</returns>
    public static SparseVector FromPairs(IDictionary<int, double> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var norm = Math.Sqrt(pairs.Values.Sum(v => v * v));
        if (norm <= 0)
        {
            return Empty;
        }

        var ordered = pairs.OrderBy(p => p.Key).ToList();
        return new SparseVector([.. ordered.Select(p => p.Key)], [.. ordered.Select(p => (float)(p.Value / norm))]);
    }

    /// <summary>Similitud coseno (los dos ya estan normalizados).</summary>
    /// <param name="a">Uno.</param>
    /// <param name="b">Otro.</param>
    /// <returns>De 0 a 1.</returns>
    public static double Cosine(SparseVector a, SparseVector b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        double sum = 0;
        int i = 0, j = 0;
        while (i < a.indices.Length && j < b.indices.Length)
        {
            if (a.indices[i] == b.indices[j])
            {
                sum += a.values[i] * b.values[j];
                i++;
                j++;
            }
            else if (a.indices[i] < b.indices[j])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return sum;
    }

    /// <summary>Suma este vector, multiplicado por un peso, a un acumulador.</summary>
    /// <param name="into">Acumulador.</param>
    /// <param name="weight">Peso.</param>
    public void AddTo(IDictionary<int, double> into, double weight)
    {
        ArgumentNullException.ThrowIfNull(into);
        for (var i = 0; i < indices.Length; i++)
        {
            into[indices[i]] = into.TryGetValue(indices[i], out var current) ? current + (values[i] * weight) : values[i] * weight;
        }
    }
}

/// <summary>Resultado de la evaluacion fuera de linea (leave-one-out): evidencia de que el modelo aprende (L-16i).</summary>
/// <param name="Users">Personas evaluadas.</param>
/// <param name="K">Tamano de la lista.</param>
/// <param name="HitRate">Parte de las personas cuya ultima interaccion quedo entre las K recomendadas.</param>
/// <param name="RandomBaseline">Lo que lograria recomendar al azar.</param>
/// <param name="Collaborative">Si gano la mezcla con filtrado colaborativo (si no, solo el contenido).</param>
internal sealed record RecommendationEvaluation(int Users, int K, double HitRate, double RandomBaseline, bool Collaborative);

/// <summary>
/// Modelo de recomendaciones de un campus (HU-35, M-11), entrenado con ML.NET dentro de la API:
/// <list type="bullet">
/// <item>TF-IDF de nombre, descripcion y categoria (palabras y trigramas de letras, sin palabras vacias del espanol),
/// aprendido del catalogo: "Ver parecidos" son los vecinos mas cercanos.</item>
/// <item>"Para ti": el perfil de cada persona (suma de lo que vio, guardo, escribio y compro, con pesos y olvido de 30
/// dias) comparado con cada publicacion; con datos suficientes se mezcla con factorizacion de matrices de una clase
/// (filtrado colaborativo: personas con gustos parecidos), solo si la evaluacion leave-one-out muestra que mejora.</item>
/// <item>Sin historial: lo mas activo del campus en 14 dias y luego lo mas nuevo (RN-46).</item>
/// </list>
/// </summary>
internal sealed class RecommendationModel
{
    /// <summary>Parte del puntaje que aporta el filtrado colaborativo cuando hay datos.</summary>
    public const double CollaborativeShare = 0.4;

    /// <summary>Dias en que una interaccion pierde ~63 % de su peso.</summary>
    public const double ForgetDays = 30;

    /// <summary>Ventana de "lo mas activo del campus".</summary>
    public static readonly TimeSpan PopularWindow = TimeSpan.FromDays(14);

    private readonly MLContext ml;
    private readonly ITransformer? text;
    private readonly Lock textGate = new();
    private readonly Dictionary<string, int> index;
    private readonly SparseVector[] vectors;
    private readonly CollaborativeScorer? collaborative;
    private readonly Dictionary<string, double> popularity;

    private RecommendationModel(
        string communityId,
        IReadOnlyList<CatalogItem> items,
        MLContext ml,
        ITransformer? text,
        SparseVector[] vectors,
        CollaborativeScorer? collaborative,
        Dictionary<string, double> popularity,
        DateTime trainedAt)
    {
        CommunityId = communityId;
        Items = items;
        this.ml = ml;
        this.text = text;
        this.vectors = vectors;
        this.collaborative = collaborative;
        this.popularity = popularity;
        TrainedAt = trainedAt;
        index = items.Select((item, i) => (item.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
    }

    /// <summary>Campus.</summary>
    public string CommunityId { get; }

    /// <summary>El catalogo con el que se entreno.</summary>
    public IReadOnlyList<CatalogItem> Items { get; }

    /// <summary>Cuando se entreno (UTC).</summary>
    public DateTime TrainedAt { get; }

    /// <summary>Si uso filtrado colaborativo (hay datos suficientes).</summary>
    public bool UsesCollaborative => collaborative is not null;

    /// <summary>La evaluacion fuera de linea, si se pidio y hubo a quien evaluar.</summary>
    public RecommendationEvaluation? Evaluation { get; private init; }

    /// <summary>Entrena el modelo de un campus.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="items">Publicaciones disponibles.</param>
    /// <param name="interactions">Interacciones del campus (90 dias).</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="evaluate">
    /// Medir hit@5 con leave-one-out (entrena de nuevo con y sin filtrado colaborativo) y quedarse con el colaborativo
    /// solo si mejora: con pocos datos la factorizacion puede empeorar las recomendaciones.
    /// </param>
    /// <returns>El modelo.</returns>
    public static RecommendationModel Train(
        string communityId,
        IReadOnlyList<CatalogItem> items,
        IReadOnlyList<InteractionDocument> interactions,
        DateTime now,
        bool evaluate)
    {
        var evaluation = evaluate ? Evaluate(communityId, items, interactions, now, 5) : null;
        return Fit(communityId, items, interactions, now, collaborative: evaluation?.Collaborative ?? true, evaluation);
    }

    private static RecommendationModel Fit(
        string communityId,
        IReadOnlyList<CatalogItem> items,
        IReadOnlyList<InteractionDocument> interactions,
        DateTime now,
        bool collaborative,
        RecommendationEvaluation? evaluation)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(interactions);
        var ml = new MLContext(seed: 7);
        ITransformer? text = null;
        SparseVector[] vectors = [];
        if (items.Count > 0)
        {
            // TF-IDF aprendido del catalogo del campus (el vocabulario y el peso de cada termino salen de los datos).
            var data = ml.Data.LoadFromEnumerable(items.Select(ToRow));
            text = ml.Transforms.Text.FeaturizeText(nameof(FeatureRow.Features), TextOptions(), nameof(TextRow.Text)).Fit(data);
            vectors = [.. text.Transform(data).GetColumn<VBuffer<float>>(nameof(FeatureRow.Features)).Select(SparseVector.From)];
        }

        var since = now - PopularWindow;
        var popularity = interactions
            .Where(i => i.At >= since)
            .GroupBy(i => i.ProductId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(i => InteractionKinds.Weight(i.Kind)), StringComparer.Ordinal);

        return new RecommendationModel(communityId, items, ml, text, vectors, collaborative ? CollaborativeScorer.TryTrain(interactions) : null, popularity, now)
        {
            Evaluation = evaluation,
        };
    }

    /// <summary>"Para ti": publicaciones ordenadas para una persona segun su historial; vacio si no hay historial util.</summary>
    /// <param name="userId">Quien.</param>
    /// <param name="history">Sus interacciones.</param>
    /// <param name="allowed">Que publicaciones se pueden recomendar (no propias ni de personas bloqueadas).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <returns>Ids, del mas al menos recomendado.</returns>
    public IReadOnlyList<string> ForUser(string userId, IReadOnlyList<InteractionDocument> history, Func<CatalogItem, bool> allowed, int limit, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(allowed);
        var seen = history.Select(h => h.ProductId).ToHashSet(StringComparer.Ordinal);
        var sum = new Dictionary<int, double>();
        foreach (var interaction in history)
        {
            if (index.TryGetValue(interaction.ProductId, out var i))
            {
                var age = Math.Max(0, (now - interaction.At).TotalDays);
                vectors[i].AddTo(sum, InteractionKinds.Weight(interaction.Kind) * Math.Exp(-age / ForgetDays));
            }
        }

        var profile = SparseVector.FromPairs(sum);
        if (profile.IsEmpty)
        {
            return [];
        }

        var candidates = Enumerable.Range(0, Items.Count).Where(i => !seen.Contains(Items[i].Id) && allowed(Items[i])).ToList();
        var collaborativeScores = collaborative?.Knows(userId) == true
            ? collaborative.Score(userId, [.. candidates.Select(i => Items[i].Id)])
            : null;
        // El coseno se lleva a la misma escala (0 a 1) que el puntaje colaborativo antes de mezclarlos.
        var cosines = candidates.ToDictionary(i => i, i => SparseVector.Cosine(profile, vectors[i]));
        var top = cosines.Count == 0 ? 0 : cosines.Values.Max();
        return [.. candidates
            .Select(i =>
            {
                var content = top > 0 ? cosines[i] / top : 0;
                return (Item: Items[i], Score: Blend(content, collaborativeScores, Items[i].Id));
            })
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Item.PublishedAt)
            .Take(limit)
            .Select(s => s.Item.Id)];
    }

    // Sin parecido de texto no se recomienda; con filtrado colaborativo, se mezcla.
    private static double Blend(double content, IReadOnlyDictionary<string, double>? collaborativeScores, string productId)
    {
        if (content <= 0)
        {
            return 0;
        }

        return collaborativeScores is not null && collaborativeScores.TryGetValue(productId, out var shared)
            ? ((1 - CollaborativeShare) * content) + (CollaborativeShare * shared)
            : content;
    }

    /// <summary>Sin historial (RN-46): lo mas activo del campus y despues lo mas nuevo.</summary>
    /// <param name="allowed">Que publicaciones se pueden recomendar.</param>
    /// <param name="limit">Maximo.</param>
    /// <returns>Ids.</returns>
    public IReadOnlyList<string> General(Func<CatalogItem, bool> allowed, int limit)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        return [.. Items
            .Where(allowed)
            .OrderByDescending(item => popularity.GetValueOrDefault(item.Id))
            .ThenByDescending(item => item.PublishedAt)
            .Take(limit)
            .Select(item => item.Id)];
    }

    /// <summary>"Ver parecidos": los vecinos mas cercanos por texto, con un poco de categoria y precio.</summary>
    /// <param name="target">La publicacion.</param>
    /// <param name="allowed">Que publicaciones se pueden mostrar.</param>
    /// <param name="limit">Maximo.</param>
    /// <returns>Ids, del mas al menos parecido.</returns>
    public IReadOnlyList<string> Similar(CatalogItem target, Func<CatalogItem, bool> allowed, int limit)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(allowed);
        var vector = VectorOf(target);
        return [.. Items
            .Where(item => !string.Equals(item.Id, target.Id, StringComparison.Ordinal) && allowed(item))
            .Select(item =>
            {
                var cosine = SparseVector.Cosine(vector, vectors[index[item.Id]]);
                var sameCategory = string.Equals(item.CategorySlug, target.CategorySlug, StringComparison.Ordinal) ? 1.0 : 0.0;
                return (Item: item, Cosine: cosine, Score: (0.8 * cosine) + (0.1 * sameCategory) + (0.1 * PriceSimilarity(item.PriceCents, target.PriceCents)));
            })
            .Where(s => s.Cosine >= MinSimilarText)
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Item.PublishedAt)
            .Take(limit)
            .Select(s => s.Item.Id)];
    }

    /// <summary>Similitud de texto minima para ser "parecido".</summary>
    public const double MinSimilarText = 0.1;

    /// <summary>
    /// Leave-one-out: a cada persona con 2 o mas publicaciones se le quita la ultima, se entrena con lo demas y se mira si
    /// esa publicacion queda entre las K recomendadas. Se compara con recomendar al azar. Se mide con y sin filtrado
    /// colaborativo y se regresa el mejor (en un empate, el de solo contenido, que es mas simple).
    /// </summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="items">Publicaciones disponibles.</param>
    /// <param name="interactions">Interacciones.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="k">Tamano de la lista.</param>
    /// <returns>La evaluacion, o <c>null</c> si no hay a quien evaluar.</returns>
    public static RecommendationEvaluation? Evaluate(
        string communityId,
        IReadOnlyList<CatalogItem> items,
        IReadOnlyList<InteractionDocument> interactions,
        DateTime now,
        int k)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(interactions);
        var available = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var heldOut = interactions
            .GroupBy(i => i.UserId, StringComparer.Ordinal)
            .Select(g => (User: g.Key, Products: g.GroupBy(i => i.ProductId, StringComparer.Ordinal).Select(p => (Id: p.Key, Last: p.Max(i => i.At))).OrderByDescending(p => p.Last).ToList()))
            .Where(u => u.Products.Count >= 2 && available.Contains(u.Products[0].Id))
            .ToDictionary(u => u.User, u => u.Products[0].Id, StringComparer.Ordinal);
        if (heldOut.Count == 0)
        {
            return null;
        }

        var training = interactions.Where(i => !(heldOut.TryGetValue(i.UserId, out var held) && i.ProductId == held)).ToList();
        var content = Score(Fit(communityId, items, training, now, collaborative: false, evaluation: null), items, training, heldOut, now, k);
        var blended = Fit(communityId, items, training, now, collaborative: true, evaluation: null);
        if (!blended.UsesCollaborative)
        {
            return content;
        }

        var mixed = Score(blended, items, training, heldOut, now, k) with { Collaborative = true };
        return mixed.HitRate > content.HitRate ? mixed : content;
    }

    private static RecommendationEvaluation Score(
        RecommendationModel model,
        IReadOnlyList<CatalogItem> items,
        List<InteractionDocument> training,
        Dictionary<string, string> heldOut,
        DateTime now,
        int k)
    {
        var hits = 0;
        double baseline = 0;
        foreach (var (user, held) in heldOut)
        {
            var history = training.Where(i => i.UserId == user).ToList();
            var seen = history.Select(h => h.ProductId).ToHashSet(StringComparer.Ordinal);
            bool Allowed(CatalogItem item) => !string.Equals(item.SellerId, user, StringComparison.Ordinal);
            var candidates = items.Count(item => Allowed(item) && !seen.Contains(item.Id));
            baseline += candidates == 0 ? 0 : Math.Min(1.0, (double)k / candidates);
            if (model.ForUser(user, history, Allowed, k, now).Contains(held, StringComparer.Ordinal))
            {
                hits++;
            }
        }

        return new RecommendationEvaluation(heldOut.Count, k, (double)hits / heldOut.Count, baseline / heldOut.Count, Collaborative: false);
    }

    /// <summary>El vector de una publicacion (la calcula si se publico despues del entrenamiento).</summary>
    /// <param name="item">La publicacion.</param>
    /// <returns>El vector.</returns>
    public SparseVector VectorOf(CatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (index.TryGetValue(item.Id, out var i))
        {
            return vectors[i];
        }

        if (text is null)
        {
            return SparseVector.Empty;
        }

        lock (textGate)
        {
            var one = ml.Data.LoadFromEnumerable([ToRow(item)]);
            return SparseVector.From(text.Transform(one).GetColumn<VBuffer<float>>(nameof(FeatureRow.Features)).First());
        }
    }

    // 1 si cuestan lo mismo, 0 si uno cuesta 3 veces el otro o mas.
    private static double PriceSimilarity(long a, long b) =>
        a <= 0 || b <= 0 ? 0 : Math.Max(0, 1 - (Math.Abs(Math.Log((double)a / b)) / Math.Log(3)));

    // El nombre pesa el doble que la descripcion; la categoria entra como palabras.
    private static TextRow ToRow(CatalogItem item) =>
        new() { Text = string.Join(' ', item.Title, item.Title, item.Description, item.CategorySlug.Replace('-', ' ')) };

    /// <summary>El texto como lo ve el modelo (tambien el de precios, HU-58).</summary>
    /// <returns>Las opciones.</returns>
    internal static TextFeaturizingEstimator.Options TextOptions() => new()
    {
        CaseMode = TextNormalizingEstimator.CaseMode.Lower,
        KeepDiacritics = false,
        KeepPunctuations = false,
        KeepNumbers = true,
        StopWordsRemoverOptions = new StopWordsRemovingEstimator.Options { Language = TextFeaturizingEstimator.Language.Spanish },
        WordFeatureExtractor = new WordBagEstimator.Options { NgramLength = 1, UseAllLengths = true, Weighting = NgramExtractingEstimator.WeightingCriteria.TfIdf },
        // Los trigramas de letras juntan "calculadora" y "calculadoras" o "fx-991" y "fx991".
        CharFeatureExtractor = new WordBagEstimator.Options { NgramLength = 3, UseAllLengths = false, Weighting = NgramExtractingEstimator.WeightingCriteria.TfIdf },
        Norm = TextFeaturizingEstimator.NormFunction.L2,
    };

    private sealed class TextRow
    {
        public string Text { get; set; } = string.Empty;
    }

    private sealed class FeatureRow
    {
        [VectorType]
        public float[] Features { get; set; } = [];
    }
}

/// <summary>
/// Filtrado colaborativo con factorizacion de matrices de una clase (ML.NET, LIBMF): aprende factores de personas y
/// publicaciones solo con lo que si hicieron (sin calificaciones negativas). Se entrena si hay datos suficientes.
/// </summary>
internal sealed class CollaborativeScorer
{
    /// <summary>Pares persona-publicacion minimos para entrenar.</summary>
    public const int MinPairs = 20;

    /// <summary>Personas minimas para entrenar.</summary>
    public const int MinUsers = 3;

    private readonly MLContext ml;
    private readonly ITransformer model;
    private readonly HashSet<string> users;
    private readonly HashSet<string> products;
    private readonly Lock gate = new();

    private CollaborativeScorer(MLContext ml, ITransformer model, HashSet<string> users, HashSet<string> products)
    {
        this.ml = ml;
        this.model = model;
        this.users = users;
        this.products = products;
    }

    /// <summary>Entrena si hay datos suficientes; si no, <c>null</c>.</summary>
    /// <param name="interactions">Interacciones del campus.</param>
    /// <returns>El modelo o <c>null</c>.</returns>
    public static CollaborativeScorer? TryTrain(IReadOnlyList<InteractionDocument> interactions)
    {
        var pairs = interactions
            .Select(i => (i.UserId, i.ProductId))
            .Distinct()
            .Select(p => new Pair { UserId = p.UserId, ProductId = p.ProductId, Label = 1f })
            .ToList();
        var users = pairs.Select(p => p.UserId).ToHashSet(StringComparer.Ordinal);
        var products = pairs.Select(p => p.ProductId).ToHashSet(StringComparer.Ordinal);
        if (pairs.Count < MinPairs || users.Count < MinUsers || products.Count < 2)
        {
            return null;
        }

        var ml = new MLContext(seed: 7);
        var options = new MatrixFactorizationTrainer.Options
        {
            MatrixColumnIndexColumnName = "UserKey",
            MatrixRowIndexColumnName = "ProductKey",
            LabelColumnName = nameof(Pair.Label),
            LossFunction = MatrixFactorizationTrainer.LossFunctionType.SquareLossOneClass,
            Alpha = 0.01,
            Lambda = 0.025,
            C = 0.00001,
            ApproximationRank = 8,
            NumberOfIterations = 40,
            NumberOfThreads = 1,
            Quiet = true,
        };
        var pipeline = ml.Transforms.Conversion.MapValueToKey("UserKey", nameof(Pair.UserId))
            .Append(ml.Transforms.Conversion.MapValueToKey("ProductKey", nameof(Pair.ProductId)))
            .Append(ml.Recommendation().Trainers.MatrixFactorization(options));
        var model = pipeline.Fit(ml.Data.LoadFromEnumerable(pairs));
        return new CollaborativeScorer(ml, model, users, products);
    }

    /// <summary>Si la persona estuvo en el entrenamiento.</summary>
    /// <param name="userId">Quien.</param>
    /// <returns><c>true</c> si la conoce.</returns>
    public bool Knows(string userId) => users.Contains(userId);

    /// <summary>Puntajes de 0 a 1 (normalizados entre los candidatos que el modelo conoce).</summary>
    /// <param name="userId">Quien.</param>
    /// <param name="productIds">Candidatas.</param>
    /// <returns>Publicacion → puntaje.</returns>
    public IReadOnlyDictionary<string, double> Score(string userId, IReadOnlyList<string> productIds)
    {
        var known = productIds.Where(products.Contains).ToList();
        if (known.Count == 0)
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }

        float[] raw;
        lock (gate)
        {
            var rows = ml.Data.LoadFromEnumerable(known.Select(p => new Pair { UserId = userId, ProductId = p }));
            raw = [.. model.Transform(rows).GetColumn<float>("Score")];
        }

        var min = raw.Min();
        var max = raw.Max();
        return known
            .Select((p, i) => (p, Value: max > min ? (raw[i] - min) / (max - min) : 0.5))
            .ToDictionary(x => x.p, x => x.Value, StringComparer.Ordinal);
    }

    private sealed class Pair
    {
        public string UserId { get; set; } = string.Empty;

        public string ProductId { get; set; } = string.Empty;

        public float Label { get; set; }
    }
}
