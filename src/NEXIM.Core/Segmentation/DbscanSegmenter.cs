// NEXIM — DBSCAN spectral segmenter (pure C#).
// Density-Based Spatial Clustering of Applications with Noise.
// Labels noise pixels as class −1.
//
// Reference: Ester, Kriegel, Sander & Xu (1996) "A density-based algorithm
//   for discovering clusters in large spatial databases with noise."
//   KDD-96 Proc., pp. 226–231.

namespace NEXIM.Core.Segmentation;

/// <summary>
/// DBSCAN segmenter operating in spectral feature space.
/// Noise pixels receive label −1.
/// </summary>
public sealed class DbscanSegmenter : ISegmenter
{
    readonly double _epsilon;
    readonly int    _minPoints;

    /// <param name="epsilon">
    ///   Neighbourhood radius in feature space (Euclidean distance).
    /// </param>
    /// <param name="minPoints">
    ///   Minimum number of points within <paramref name="epsilon"/> (including
    ///   the point itself) to form a core point.
    /// </param>
    public DbscanSegmenter(double epsilon = 0.1, int minPoints = 5)
    {
        if (epsilon <= 0)    throw new ArgumentOutOfRangeException(nameof(epsilon));
        if (minPoints < 1)   throw new ArgumentOutOfRangeException(nameof(minPoints));
        _epsilon   = epsilon;
        _minPoints = minPoints;
    }

    /// <inheritdoc/>
    public SegmentationResult Segment(IReadOnlyList<PixelFeature> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Count == 0)
            return new SegmentationResult { Labels = Array.Empty<int>(), ClassCount = 0 };

        int n       = pixels.Count;
        int[] labels = Enumerable.Repeat(-1, n).ToArray();  // −1 = unvisited / noise
        int cluster  = 0;

        for (int i = 0; i < n; i++)
        {
            if (labels[i] != -1) continue;  // already assigned

            List<int> neighbours = RangeQuery(pixels, i, _epsilon);
            if (neighbours.Count < _minPoints)
            {
                labels[i] = -1;  // noise (will stay −1 unless later absorbed)
                continue;
            }

            labels[i] = cluster;
            var seeds  = new Queue<int>(neighbours);
            seeds.Dequeue();  // remove i itself

            while (seeds.Count > 0)
            {
                int q = seeds.Dequeue();
                if (labels[q] == -1) labels[q] = cluster;  // was noise → border
                if (labels[q] != -1 && labels[q] != cluster) continue;  // already in other cluster (shouldn't happen in standard DBSCAN but guard it)
                labels[q] = cluster;

                List<int> qNeighbours = RangeQuery(pixels, q, _epsilon);
                if (qNeighbours.Count >= _minPoints)
                    foreach (int qn in qNeighbours)
                        if (labels[qn] == -1) seeds.Enqueue(qn);
            }

            cluster++;
        }

        int classCount = cluster;  // number of real clusters (noise = −1 not counted)

        return new SegmentationResult
        {
            Labels     = labels,
            ClassCount = classCount,
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────

    List<int> RangeQuery(IReadOnlyList<PixelFeature> pixels, int idx, double eps)
    {
        double[] x     = pixels[idx].Features;
        int      dims  = x.Length;
        double   eps2  = eps * eps;
        var      result = new List<int>();

        for (int j = 0; j < pixels.Count; j++)
        {
            double[] y = pixels[j].Features;
            double   d = 0.0;
            for (int k = 0; k < dims; k++) { double diff = x[k] - y[k]; d += diff * diff; }
            if (d <= eps2) result.Add(j);
        }
        return result;
    }
}
