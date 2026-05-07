// NEXIM — K-means spectral segmenter (ML.NET + native fallback).
// Uses Microsoft.ML KMeansTrainer when k ≥ 2.  A pure C# implementation
// is also provided as NativeKMeans for unit-testing without ML.NET overhead.
//
// Reference: MacQueen, J. (1967). "Some methods for classification and
//   analysis of multivariate observations." Proc. 5th Berkeley Symp.

using Microsoft.ML;
using Microsoft.ML.Data;

namespace NEXIM.Core.Segmentation;

// ── ML.NET schema row ─────────────────────────────────────────────────────────

file sealed class FeatureRow
{
    [VectorType]
    public float[] Features { get; set; } = Array.Empty<float>();
}

// ── public segmenter ─────────────────────────────────────────────────────────

/// <summary>
/// Spectral K-means segmenter backed by Microsoft.ML.
/// Falls back to <see cref="NativeKMeans"/> when k = 1.
/// </summary>
public sealed class KMeansSegmenter : ISegmenter
{
    readonly int    _k;
    readonly int    _maxIterations;
    readonly int    _seed;

    /// <param name="k">Number of clusters (≥ 1).</param>
    /// <param name="maxIterations">Maximum Lloyd iterations passed to ML.NET.</param>
    /// <param name="seed">Random seed for reproducibility.</param>
    public KMeansSegmenter(int k = 8, int maxIterations = 100, int seed = 42)
    {
        if (k < 1) throw new ArgumentOutOfRangeException(nameof(k), "k must be ≥ 1.");
        _k             = k;
        _maxIterations = maxIterations;
        _seed          = seed;
    }

    /// <inheritdoc/>
    public SegmentationResult Segment(IReadOnlyList<PixelFeature> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Count == 0)
            return new SegmentationResult { Labels = Array.Empty<int>(), ClassCount = 0 };

        // k=1: trivially all class 0
        if (_k == 1 || pixels.Count <= _k)
        {
            int effectiveK = Math.Min(_k, pixels.Count);
            // Assign pixel i → class i for pixels.Count < k; else all → 0
            int[] trivial = pixels.Count <= _k
                ? Enumerable.Range(0, pixels.Count).ToArray()
                : new int[pixels.Count];
            return new SegmentationResult
            {
                Labels     = trivial,
                ClassCount = effectiveK,
            };
        }

        int dims = pixels[0].Features.Length;

        var ctx = new MLContext(seed: _seed);

        // Build IDataView with explicit fixed-size vector schema
        var rows = pixels.Select(p => new FeatureRow
        {
            Features = Array.ConvertAll(p.Features, v => (float)v),
        }).ToList();

        // ML.NET requires a known-size vector; supply schema with explicit dim
        var schemaDef = SchemaDefinition.Create(typeof(FeatureRow));
        schemaDef["Features"].ColumnType =
            new VectorDataViewType(NumberDataViewType.Single, dims);
        var data  = ctx.Data.LoadFromEnumerable(rows, schemaDef);
        var pipeline = ctx.Transforms
            .NormalizeMinMax("Features")
            .Append(ctx.Clustering.Trainers.KMeans(
                featureColumnName: "Features",
                numberOfClusters:  _k));

        var model = pipeline.Fit(data);
        var transformed = model.Transform(data);

        // Read predicted cluster IDs (1-based in ML.NET) and distance scores
        var predictedIds = transformed.GetColumn<uint>("PredictedLabel").ToArray();
        var scores       = transformed.GetColumn<float[]>("Score").ToArray();

        int[] labels    = new int[pixels.Count];
        double[][] probs = new double[pixels.Count][];

        for (int i = 0; i < pixels.Count; i++)
        {
            labels[i] = (int)predictedIds[i] - 1;  // 1-based → 0-based
            probs[i]  = Array.ConvertAll(scores[i], s => (double)s);
        }

        // Compute centroids from assigned labels
        double[][] centroids = ComputeCentroids(pixels, labels, _k, dims);

        return new SegmentationResult
        {
            Labels        = labels,
            ClassCount    = _k,
            Centroids     = centroids,
            Probabilities = probs,
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────

    internal static double[][] ComputeCentroids(
        IReadOnlyList<PixelFeature> pixels, int[] labels, int k, int dims)
    {
        var sums   = new double[k][];
        var counts = new int[k];
        for (int c = 0; c < k; c++) sums[c] = new double[dims];

        for (int i = 0; i < pixels.Count; i++)
        {
            int lbl = labels[i];
            if (lbl < 0 || lbl >= k) continue;
            counts[lbl]++;
            for (int d = 0; d < dims; d++)
                sums[lbl][d] += pixels[i].Features[d];
        }

        for (int c = 0; c < k; c++)
            if (counts[c] > 0)
                for (int d = 0; d < dims; d++)
                    sums[c][d] /= counts[c];

        return sums;
    }
}

// ── pure-C# fallback ─────────────────────────────────────────────────────────

/// <summary>
/// Lightweight pure-C# K-means implementation (Lloyd's algorithm).
/// Used for unit testing and environments without ML.NET overhead.
/// Labels are 0-based.
/// </summary>
public static class NativeKMeans
{
    /// <summary>
    /// Run K-means on <paramref name="pixels"/>.
    /// </summary>
    public static SegmentationResult Run(
        IReadOnlyList<PixelFeature> pixels,
        int k,
        int maxIterations = 200,
        int seed          = 0)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (k < 1) throw new ArgumentOutOfRangeException(nameof(k));
        if (pixels.Count == 0)
            return new SegmentationResult { Labels = Array.Empty<int>(), ClassCount = 0 };

        int n    = pixels.Count;
        int dims = pixels[0].Features.Length;
        int ek   = Math.Min(k, n);

        // Initialise centroids by picking ek evenly-spaced pixels
        double[][] centroids = new double[ek][];
        for (int c = 0; c < ek; c++)
        {
            int idx = c * n / ek;
            centroids[c] = (double[])pixels[idx].Features.Clone();
        }

        int[] labels = new int[n];
        for (int iter = 0; iter < maxIterations; iter++)
        {
            bool changed = false;

            // Assignment step
            for (int i = 0; i < n; i++)
            {
                int    best  = 0;
                double bestD = double.MaxValue;
                for (int c = 0; c < ek; c++)
                {
                    double d = SquaredEuclidean(pixels[i].Features, centroids[c], dims);
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (labels[i] != best) { labels[i] = best; changed = true; }
            }

            if (!changed) break;

            // Update step
            var sums   = new double[ek][];
            var counts = new int[ek];
            for (int c = 0; c < ek; c++) sums[c] = new double[dims];

            for (int i = 0; i < n; i++)
            {
                counts[labels[i]]++;
                for (int d = 0; d < dims; d++)
                    sums[labels[i]][d] += pixels[i].Features[d];
            }

            for (int c = 0; c < ek; c++)
                if (counts[c] > 0)
                    for (int d = 0; d < dims; d++)
                        centroids[c][d] = sums[c][d] / counts[c];
        }

        return new SegmentationResult
        {
            Labels     = labels,
            ClassCount = ek,
            Centroids  = centroids,
        };
    }

    static double SquaredEuclidean(double[] a, double[] b, int dims)
    {
        double s = 0.0;
        for (int i = 0; i < dims; i++) { double d = a[i] - b[i]; s += d * d; }
        return s;
    }
}
