using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;

namespace Musubi.Api.Modules.Recommendations;

/// <summary>Un precio para entrenar (HU-58).</summary>
/// <param name="Key">Id estable (la publicacion, o el pedido): decide si cae en la prueba fuera de linea.</param>
/// <param name="Title">Nombre.</param>
/// <param name="CategorySlug">Categoria.</param>
/// <param name="Condition">Estado (<c>new</c>, <c>used:likeNew</c>, <c>used:good</c> o <c>used:fair</c>).</param>
/// <param name="PriceCents">Precio por pieza.</param>
/// <param name="Sold">Lo que de verdad se pago en un pedido completado (pesa mas que un precio publicado).</param>
internal sealed record PriceSample(string Key, string Title, string CategorySlug, string Condition, long PriceCents, bool Sold);

/// <summary>"Precio sugerido: $X – $Y".</summary>
/// <param name="MinCents">Desde.</param>
/// <param name="MaxCents">Hasta.</param>
/// <param name="Samples">Cuantos precios de esa categoria hay (para decir "con N precios del campus").</param>
internal sealed record PriceRange(long MinCents, long MaxCents, int Samples);

/// <summary>La medicion fuera de linea de un entrenamiento (evidencia de ML, L-16i).</summary>
/// <param name="Train">Precios para entrenar.</param>
/// <param name="Test">Precios apartados para medir.</param>
/// <param name="ModelMae">Error absoluto medio del modelo, en pesos.</param>
/// <param name="BaselineMae">El de la mediana de la categoria, en pesos (lo que se haria sin ML).</param>
internal sealed record PriceEvaluation(int Train, int Test, double ModelMae, double BaselineMae);

/// <summary>
/// HU-58 (ML.NET, dentro de la API y sin servicios externos): una regresion lineal (SDCA) sobre el logaritmo del precio,
/// con las palabras del nombre (TF-IDF de palabras y trigramas de letras, como HU-35), la categoria y el estado. Lo
/// vendido pesa <see cref="SoldWeight"/> veces mas que lo publicado. El rango es la estimacion mas y menos el error
/// tipico medido; con pocos precios de la categoria no se estima nada (no se inventa).
/// </summary>
internal sealed class PriceModel
{
    /// <summary>Precios del campus para entrenar algo.</summary>
    public const int MinSamples = 20;

    /// <summary>Precios de una categoria para sugerir en ella (decision para revisar, 23-Nov-2026).</summary>
    public const int MinPerCategory = 5;

    /// <summary>Cuanto pesa un precio pagado frente a uno publicado.</summary>
    public const float SoldWeight = 3f;

    /// <summary>Precios apartados para medir: 1 de cada 5.</summary>
    public const int TestEvery = 5;

    /// <summary>El rango nunca es mas angosto que +-10 % ni mas ancho que +-60 % (en logaritmo).</summary>
    private const double MinSpread = 0.10;

    private const double MaxSpread = 0.60;

    private readonly MLContext? ml;
    private readonly ITransformer? model;
    private readonly IReadOnlyDictionary<string, int> perCategory;
    private readonly double spread;

    private PriceModel(DateTime trainedAt, int samples, IReadOnlyDictionary<string, int> perCategory, MLContext? ml, ITransformer? model, double spread, PriceEvaluation? evaluation)
    {
        TrainedAt = trainedAt;
        SampleCount = samples;
        this.perCategory = perCategory;
        this.ml = ml;
        this.model = model;
        this.spread = spread;
        Evaluation = evaluation;
    }

    /// <summary>Cuando se entreno (UTC).</summary>
    public DateTime TrainedAt { get; }

    /// <summary>Precios con los que se entreno.</summary>
    public int SampleCount { get; }

    /// <summary>La medicion fuera de linea, o <c>null</c> si no hubo precios suficientes para apartar.</summary>
    public PriceEvaluation? Evaluation { get; }

    /// <summary>El error tipico (en logaritmo) que da el ancho del rango.</summary>
    public double Spread => spread;

    /// <summary>Entrena con los precios del campus.</summary>
    /// <param name="samples">Los precios.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="evaluate">Medir el error con 1 de cada 5 precios apartados.</param>
    /// <returns>El modelo (sin estimaciones si hay pocos precios).</returns>
    public static PriceModel Train(IReadOnlyList<PriceSample> samples, DateTime now, bool evaluate)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var usable = samples.Where(s => s.PriceCents > 0 && !string.IsNullOrWhiteSpace(s.CategorySlug)).ToList();
        var perCategory = usable.GroupBy(s => s.CategorySlug, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        if (usable.Count < MinSamples)
        {
            return new PriceModel(now, usable.Count, perCategory, null, null, MaxSpread, null);
        }

        var ml = new MLContext(seed: 7);
        PriceEvaluation? evaluation = null;
        List<double>? residuals = null;
        var test = usable.Where(IsTest).ToList();
        var train = usable.Where(s => !IsTest(s)).ToList();
        if (evaluate && test.Count >= MinPerCategory && train.Count >= MinSamples)
        {
            var partial = Fit(ml, train);
            var scores = Predict(ml, partial, test);
            var predicted = test.Select((s, i) => (Sample: s, Log: (double)scores[i])).ToList();
            residuals = [.. predicted.Select(p => Math.Abs(p.Log - LogPesos(p.Sample.PriceCents)))];
            evaluation = new PriceEvaluation(
                train.Count,
                test.Count,
                predicted.Average(p => Math.Abs(Math.Exp(p.Log) - (p.Sample.PriceCents / 100d))),
                BaselineMae(train, test));
        }

        var full = Fit(ml, usable);
        if (residuals is null)
        {
            var scores = Predict(ml, full, usable);
            residuals = [.. usable.Select((s, i) => Math.Abs(scores[i] - LogPesos(s.PriceCents)))];
        }

        var spread = Math.Clamp(Quantile(residuals, 0.6), MinSpread, MaxSpread);
        return new PriceModel(now, usable.Count, perCategory, ml, full, spread, evaluation);
    }

    /// <summary>El rango para algo con ese nombre, categoria y estado, o <c>null</c> si la categoria tiene pocos precios.</summary>
    /// <param name="title">Nombre.</param>
    /// <param name="categorySlug">Categoria.</param>
    /// <param name="condition">Estado (<c>new</c> o <c>used:detalle</c>).</param>
    /// <returns>El rango o <c>null</c>.</returns>
    public PriceRange? Estimate(string title, string categorySlug, string condition)
    {
        if (ml is null || model is null || !perCategory.TryGetValue(categorySlug, out var count) || count < MinPerCategory)
        {
            return null;
        }

        // Transform es seguro entre hilos (PredictionEngine no).
        double log = Predict(ml, model, [new PriceSample(string.Empty, title, categorySlug, condition, 100, Sold: false)])[0];

        if (double.IsNaN(log) || double.IsInfinity(log))
        {
            return null;
        }

        var min = Nice(Math.Exp(log - spread), up: false);
        var max = Math.Max(Nice(Math.Exp(log + spread), up: true), min + Step(min));
        return new PriceRange(min * 100, max * 100, count);
    }

    /// <summary>El error absoluto medio de la mediana de la categoria (lo que se haria sin ML), en pesos.</summary>
    /// <param name="train">Precios conocidos.</param>
    /// <param name="test">Precios a estimar.</param>
    /// <returns>El error.</returns>
    internal static double BaselineMae(IReadOnlyList<PriceSample> train, IReadOnlyList<PriceSample> test)
    {
        var overall = Median(train.Select(s => s.PriceCents / 100d));
        var medians = train.GroupBy(s => s.CategorySlug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Median(g.Select(s => s.PriceCents / 100d)), StringComparer.Ordinal);
        return test.Average(s => Math.Abs((medians.TryGetValue(s.CategorySlug, out var m) ? m : overall) - (s.PriceCents / 100d)));
    }

    // 1 de cada 5 por un hash estable del id (no depende del orden ni de string.GetHashCode, que cambia por proceso).
    private static bool IsTest(PriceSample sample)
    {
        uint hash = 2166136261;
        foreach (var c in sample.Key)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash % TestEvery == 0;
    }

    private static float[] Predict(MLContext ml, ITransformer model, IReadOnlyList<PriceSample> samples) =>
        [.. model.Transform(ml.Data.LoadFromEnumerable(samples.Select(Row).ToList())).GetColumn<float>("Score")];

    private static TransformerChain<RegressionPredictionTransformer<LinearRegressionModelParameters>> Fit(MLContext ml, IReadOnlyList<PriceSample> samples)
    {
        var data = ml.Data.LoadFromEnumerable(samples.Select(Row).ToList());
        var pipeline = ml.Transforms.Text.FeaturizeText("TitleFeatures", RecommendationModel.TextOptions(), nameof(PriceRow.Title))
            .Append(ml.Transforms.Categorical.OneHotEncoding("CategoryFeatures", nameof(PriceRow.Category)))
            .Append(ml.Transforms.Categorical.OneHotEncoding("ConditionFeatures", nameof(PriceRow.Condition)))
            .Append(ml.Transforms.Concatenate("Features", "TitleFeatures", "CategoryFeatures", "ConditionFeatures"))
            .Append(ml.Regression.Trainers.Sdca(
                labelColumnName: nameof(PriceRow.Label),
                featureColumnName: "Features",
                exampleWeightColumnName: nameof(PriceRow.Weight),
                maximumNumberOfIterations: 200));
        return pipeline.Fit(data);
    }

    private static PriceRow Row(PriceSample s) => new()
    {
        Title = s.Title,
        Category = s.CategorySlug,
        Condition = s.Condition,
        Label = (float)LogPesos(s.PriceCents),
        Weight = s.Sold ? SoldWeight : 1f,
    };

    private static double LogPesos(long cents) => Math.Log(cents / 100d);

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static double Quantile(IReadOnlyList<double> values, double q)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? MaxSpread : sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(q * sorted.Length))];
    }

    // Pesos "redondos" como los escribe la gente: de 5 en 5 hasta $100, de 10 en 10 hasta $1,000 y luego de 50 en 50.
    private static long Step(double pesos)
    {
        if (pesos < 100)
        {
            return 5;
        }

        return pesos < 1000 ? 10 : 50;
    }

    private static long Nice(double pesos, bool up)
    {
        var step = Step(pesos);
        var rounded = (long)(up ? Math.Ceiling(pesos / step) : Math.Floor(pesos / step)) * step;
        return Math.Max(step, rounded);
    }

    private sealed class PriceRow
    {
        public string Title { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Condition { get; set; } = string.Empty;

        public float Label { get; set; }

        public float Weight { get; set; }
    }
}
