// NEXIM — Gaussian Mixture Model (GMM) segmenter.
// Implements the EM algorithm for diagonal-covariance GMM in pure C#.
// Diagonal (axis-aligned) covariance is standard for high-dimensional
// hyperspectral data where full-covariance matrices are ill-conditioned.
//
// Reference: Dempster, Laird & Rubin (1977) "Maximum likelihood from
//   incomplete data via the EM algorithm." J. Royal Stat. Soc. B 39:1-38.

namespace NEXIM.Core.Segmentation;

/// <summary>
/// Diagonal-covariance Gaussian Mixture Model segmenter.
/// Implements the EM algorithm; returns hard (MAP) labels and soft
/// responsibilities as <see cref="SegmentationResult.Probabilities"/>.
/// </summary>
public sealed class GmmSegmenter : ISegmenter
{
    readonly int    _k;
    readonly int    _maxIterations;
    readonly double _tolerance;
    readonly double _minVariance;
    readonly int    _seed;

    /// <param name="k">Number of mixture components.</param>
    /// <param name="maxIterations">Maximum EM iterations.</param>
    /// <param name="tolerance">Log-likelihood convergence threshold.</param>
    /// <param name="minVariance">
    ///   Floor on per-dimension variance to prevent singularity.
    /// </param>
    /// <param name="seed">Random seed for centroid initialisation.</param>
    public GmmSegmenter(
        int    k             = 8,
        int    maxIterations = 100,
        double tolerance     = 1e-4,
        double minVariance   = 1e-6,
        int    seed          = 42)
    {
        if (k < 1) throw new ArgumentOutOfRangeException(nameof(k));
        _k             = k;
        _maxIterations = maxIterations;
        _tolerance     = tolerance;
        _minVariance   = minVariance;
        _seed          = seed;
    }

    /// <inheritdoc/>
    public SegmentationResult Segment(IReadOnlyList<PixelFeature> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Count == 0)
            return new SegmentationResult { Labels = Array.Empty<int>(), ClassCount = 0 };

        int n    = pixels.Count;
        int dims = pixels[0].Features.Length;
        int ek   = Math.Min(_k, n);

        // ── Initialise with K-means centroids ───────────────────────────
        var km   = NativeKMeans.Run(pixels, ek, maxIterations: 20, seed: _seed);
        double[][] means = km.Centroids!;

        // Initialise diagonal variances from data variance per dim
        double[] globalVar = GlobalVariance(pixels, dims);
        double[][] vars    = new double[ek][];
        for (int c = 0; c < ek; c++)
        {
            vars[c] = new double[dims];
            for (int d = 0; d < dims; d++)
                vars[c][d] = Math.Max(globalVar[d], _minVariance);
        }

        // Mixing weights: uniform
        double[] weights   = Enumerable.Repeat(1.0 / ek, ek).ToArray();

        // Responsibilities [n × ek]
        double[][] r       = new double[n][];
        for (int i = 0; i < n; i++) r[i] = new double[ek];

        double prevLogLik  = double.NegativeInfinity;

        // ── EM iterations ────────────────────────────────────────────────
        for (int iter = 0; iter < _maxIterations; iter++)
        {
            // E-step
            double logLik = EStep(pixels, means, vars, weights, r, n, dims, ek);

            // M-step
            MStep(pixels, r, means, vars, weights, n, dims, ek, _minVariance);

            if (Math.Abs(logLik - prevLogLik) < _tolerance) break;
            prevLogLik = logLik;
        }

        // Hard labels from MAP
        int[] labels = new int[n];
        for (int i = 0; i < n; i++)
        {
            int best = 0;
            for (int c = 1; c < ek; c++)
                if (r[i][c] > r[i][best]) best = c;
            labels[i] = best;
        }

        return new SegmentationResult
        {
            Labels        = labels,
            ClassCount    = ek,
            Centroids     = means,
            Probabilities = r,
        };
    }

    // ── EM steps ──────────────────────────────────────────────────────────

    static double EStep(
        IReadOnlyList<PixelFeature> pixels,
        double[][] means, double[][] vars, double[] weights,
        double[][] r, int n, int dims, int k)
    {
        double logLik = 0.0;
        for (int i = 0; i < n; i++)
        {
            double[] x     = pixels[i].Features;
            double   total = 0.0;
            for (int c = 0; c < k; c++)
            {
                double logp = LogDiagGaussian(x, means[c], vars[c], dims);
                double p    = weights[c] * Math.Exp(logp);
                r[i][c]     = p;
                total      += p;
            }
            if (total < double.Epsilon) total = double.Epsilon;
            for (int c = 0; c < k; c++) r[i][c] /= total;
            logLik += Math.Log(total);
        }
        return logLik;
    }

    static void MStep(
        IReadOnlyList<PixelFeature> pixels,
        double[][] r,
        double[][] means, double[][] vars, double[] weights,
        int n, int dims, int k, double minVar)
    {
        for (int c = 0; c < k; c++)
        {
            double nk = 0.0;
            for (int i = 0; i < n; i++) nk += r[i][c];
            if (nk < 1e-12) { weights[c] = 1e-12 / k; continue; }

            weights[c] = nk / n;

            // Update means
            double[] newMean = new double[dims];
            for (int i = 0; i < n; i++)
            {
                double ri = r[i][c];
                double[] x = pixels[i].Features;
                for (int d = 0; d < dims; d++)
                    newMean[d] += ri * x[d];
            }
            for (int d = 0; d < dims; d++) newMean[d] /= nk;
            means[c] = newMean;

            // Update diagonal variances
            double[] newVar = new double[dims];
            for (int i = 0; i < n; i++)
            {
                double ri = r[i][c];
                double[] x = pixels[i].Features;
                for (int d = 0; d < dims; d++)
                {
                    double diff = x[d] - newMean[d];
                    newVar[d] += ri * diff * diff;
                }
            }
            for (int d = 0; d < dims; d++)
                vars[c][d] = Math.Max(newVar[d] / nk, minVar);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────

    static double LogDiagGaussian(double[] x, double[] mean, double[] variance, int dims)
    {
        double logp = 0.0;
        for (int d = 0; d < dims; d++)
        {
            double diff = x[d] - mean[d];
            logp -= 0.5 * (Math.Log(2.0 * Math.PI * variance[d]) + diff * diff / variance[d]);
        }
        return logp;
    }

    static double[] GlobalVariance(IReadOnlyList<PixelFeature> pixels, int dims)
    {
        int n          = pixels.Count;
        double[] mean  = new double[dims];
        double[] var   = new double[dims];

        foreach (var px in pixels)
            for (int d = 0; d < dims; d++)
                mean[d] += px.Features[d];
        for (int d = 0; d < dims; d++) mean[d] /= n;

        foreach (var px in pixels)
            for (int d = 0; d < dims; d++)
            {
                double diff = px.Features[d] - mean[d];
                var[d] += diff * diff;
            }
        for (int d = 0; d < dims; d++) var[d] = Math.Max(var[d] / n, 1e-12);
        return var;
    }
}
