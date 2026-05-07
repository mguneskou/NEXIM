// NEXIM — SpectralAnalyser.cs
// Spectral analysis algorithms operating on HypercubeData:
//   • Named spectral indices (NDVI, NDWI, NDRE, NDBI, SAVI, EVI, CAI)
//   • PCA via covariance matrix eigen-decomposition (MathNet.Numerics)
//   • RX anomaly detector (Reed-Xiaoli — Mahalanobis distance)

using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using NEXIM.Core.Models;

namespace NEXIM.Core.Analysis;

// ── Spectral index identifiers ─────────────────────────────────────────────
public enum SpectralIndex
{
    NDVI,   // Normalised Difference Vegetation Index   (NIR-Red)/(NIR+Red)
    NDWI,   // Normalised Difference Water Index        (Green-NIR)/(Green+NIR)
    NDRE,   // Normalised Difference Red-Edge Index     (NIR-RedEdge)/(NIR+RedEdge)
    NDBI,   // Normalised Difference Built-up Index     (SWIR-NIR)/(SWIR+NIR)
    SAVI,   // Soil-Adjusted Vegetation Index           ((NIR-Red)/(NIR+Red+0.5))×1.5
    EVI,    // Enhanced Vegetation Index                2.5(NIR-Red)/(NIR+6Red-7.5Blue+1)
    CAI,    // Cellulose Absorption Index               0.5(R2000+R2200)-R2100
}

// ── PCA result ─────────────────────────────────────────────────────────────
public sealed class PcaResult
{
    /// <summary>
    /// BIL cube with <see cref="Components"/> bands.
    /// PcBands[r * Components + k][c] = score of pixel (r,c) on PC k.
    /// </summary>
    public required float[][]  PcBands     { get; init; }

    /// <summary>Eigenvalues sorted descending (one per PC).</summary>
    public required double[]   Eigenvalues { get; init; }

    /// <summary>Fraction of total variance explained by each PC (0–1).</summary>
    public required double[]   Explained   { get; init; }

    /// <summary>Number of principal components computed.</summary>
    public required int        Components  { get; init; }
}

// ── SpectralAnalyser ───────────────────────────────────────────────────────
public static class SpectralAnalyser
{
    // ── Spectral indices ──────────────────────────────────────────────────

    /// <summary>
    /// Compute a spectral index over every pixel of <paramref name="cube"/>.
    /// Returns a flat array of length Rows × Columns indexed as [r * Columns + c].
    /// </summary>
    public static float[] ComputeIndex(HypercubeData cube, SpectralIndex type)
    {
        int N = cube.Rows * cube.Columns;
        var result = new float[N];

        switch (type)
        {
            case SpectralIndex.NDVI:
            {
                var red = cube.GetBand(cube.FindBand(0.670));
                var nir = cube.GetBand(cube.FindBand(0.860));
                for (int i = 0; i < N; i++) result[i] = NdRatio(nir[i], red[i]);
                break;
            }
            case SpectralIndex.NDWI:
            {
                var grn = cube.GetBand(cube.FindBand(0.560));
                var nir = cube.GetBand(cube.FindBand(0.860));
                for (int i = 0; i < N; i++) result[i] = NdRatio(grn[i], nir[i]);
                break;
            }
            case SpectralIndex.NDRE:
            {
                var re  = cube.GetBand(cube.FindBand(0.720));
                var nir = cube.GetBand(cube.FindBand(0.800));
                for (int i = 0; i < N; i++) result[i] = NdRatio(nir[i], re[i]);
                break;
            }
            case SpectralIndex.NDBI:
            {
                var nir  = cube.GetBand(cube.FindBand(0.860));
                var swir = cube.GetBand(cube.FindBand(1.610));
                for (int i = 0; i < N; i++) result[i] = NdRatio(swir[i], nir[i]);
                break;
            }
            case SpectralIndex.SAVI:
            {
                const float L = 0.5f;
                var red = cube.GetBand(cube.FindBand(0.670));
                var nir = cube.GetBand(cube.FindBand(0.860));
                for (int i = 0; i < N; i++)
                {
                    float n = nir[i], r = red[i], d = n + r + L;
                    result[i] = MathF.Abs(d) > 1e-6f ? (n - r) / d * (1f + L) : 0f;
                }
                break;
            }
            case SpectralIndex.EVI:
            {
                var blue = cube.GetBand(cube.FindBand(0.470));
                var red  = cube.GetBand(cube.FindBand(0.670));
                var nir  = cube.GetBand(cube.FindBand(0.860));
                for (int i = 0; i < N; i++)
                {
                    float n = nir[i], r = red[i], b = blue[i];
                    float d = n + 6f * r - 7.5f * b + 1f;
                    result[i] = MathF.Abs(d) > 1e-6f ? 2.5f * (n - r) / d : 0f;
                }
                break;
            }
            case SpectralIndex.CAI:
            {
                var r2000 = cube.GetBand(cube.FindBand(2.000));
                var r2100 = cube.GetBand(cube.FindBand(2.100));
                var r2200 = cube.GetBand(cube.FindBand(2.200));
                for (int i = 0; i < N; i++)
                    result[i] = 0.5f * (r2000[i] + r2200[i]) - r2100[i];
                break;
            }
        }

        return result;
    }

    static float NdRatio(float a, float b)
    {
        float d = a + b;
        return MathF.Abs(d) > 1e-6f ? (a - b) / d : 0f;
    }

    // ── PCA ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Compute PCA via covariance-matrix eigen-decomposition.
    /// O(N·B) memory, O(N·B²) time.
    /// </summary>
    /// <param name="cube">Input hyperspectral cube.</param>
    /// <param name="components">Number of PCs to retain (clamped to Bands).</param>
    public static PcaResult ComputePCA(HypercubeData cube, int components = 5)
    {
        int K = Math.Clamp(components, 1, cube.Bands);
        int N = cube.Rows * cube.Columns;
        int B = cube.Bands;

        // 1. Global mean spectrum
        var mean = new double[B];
        for (int r = 0; r < cube.Rows; r++)
        for (int c = 0; c < cube.Columns; c++)
        for (int b = 0; b < B; b++)
            mean[b] += cube.Cube[r * B + b][c];
        for (int b = 0; b < B; b++) mean[b] /= N;

        // 2. B×B scatter matrix (upper triangle, then mirror)
        var cov = new double[B, B];
        for (int r = 0; r < cube.Rows; r++)
        for (int c = 0; c < cube.Columns; c++)
        {
            // Centred pixel vector (stack-allocated for small B)
            double[] x = new double[B];
            for (int b = 0; b < B; b++)
                x[b] = cube.Cube[r * B + b][c] - mean[b];

            for (int i = 0; i < B; i++)
            for (int j = i; j < B; j++)
            {
                double v = x[i] * x[j];
                cov[i, j] += v;
                if (i != j) cov[j, i] += v;
            }
        }
        double scale = N > 1 ? 1.0 / (N - 1) : 1.0;
        for (int i = 0; i < B; i++)
        for (int j = 0; j < B; j++)
            cov[i, j] *= scale;

        // 3. Eigen-decompose; MathNet sorts ascending → reverse for descending
        var covMat = DenseMatrix.OfArray(cov);
        var evd    = covMat.Evd(Symmetricity.Symmetric);

        int[] sortedIdx = Enumerable.Range(0, B)
            .OrderByDescending(i => evd.EigenValues[i].Real)
            .ToArray();

        double totalVar = Enumerable.Range(0, B)
            .Sum(i => Math.Max(evd.EigenValues[i].Real, 0.0));

        double[] eigenvalues = new double[K];
        double[] explained   = new double[K];
        float[][] loadings   = new float[K][];

        for (int k = 0; k < K; k++)
        {
            int idx    = sortedIdx[k];
            double ev  = evd.EigenValues[idx].Real;
            eigenvalues[k] = ev;
            explained[k]   = totalVar > 0 ? Math.Max(ev, 0) / totalVar : 0;

            loadings[k] = new float[B];
            for (int b = 0; b < B; b++)
                loadings[k][b] = (float)evd.EigenVectors[b, idx];
        }

        // 4. Project cube → PC scores (BIL, K bands)
        var pcBands = new float[cube.Rows * K][];
        for (int r = 0; r < cube.Rows; r++)
        for (int k = 0; k < K; k++)
        {
            var row = new float[cube.Columns];
            for (int c = 0; c < cube.Columns; c++)
            {
                float sum = 0f;
                for (int b = 0; b < B; b++)
                    sum += (cube.Cube[r * B + b][c] - (float)mean[b]) * loadings[k][b];
                row[c] = sum;
            }
            pcBands[r * K + k] = row;
        }

        return new PcaResult
        {
            PcBands    = pcBands,
            Eigenvalues = eigenvalues,
            Explained   = explained,
            Components  = K,
        };
    }

    // ── RX anomaly detector (Reed-Xiaoli) ─────────────────────────────────

    /// <summary>
    /// Compute the RX anomaly score (Mahalanobis distance from the global mean)
    /// for every pixel.  Returns a flat float array [r * Columns + c].
    /// A small ridge (1e-6 × I) is added to the covariance for numerical stability.
    /// </summary>
    public static float[] ComputeRxAnomaly(HypercubeData cube)
    {
        int N = cube.Rows * cube.Columns;
        int B = cube.Bands;

        // 1. Mean
        var mean = new double[B];
        for (int r = 0; r < cube.Rows; r++)
        for (int c = 0; c < cube.Columns; c++)
        for (int b = 0; b < B; b++)
            mean[b] += cube.Cube[r * B + b][c];
        for (int b = 0; b < B; b++) mean[b] /= N;

        // 2. Covariance + ridge
        var cov = new double[B, B];
        for (int r = 0; r < cube.Rows; r++)
        for (int c = 0; c < cube.Columns; c++)
        {
            double[] x = new double[B];
            for (int b = 0; b < B; b++)
                x[b] = cube.Cube[r * B + b][c] - mean[b];

            for (int i = 0; i < B; i++)
            for (int j = i; j < B; j++)
            {
                double v = x[i] * x[j];
                cov[i, j] += v;
                if (i != j) cov[j, i] += v;
            }
        }
        double scale = N > 1 ? 1.0 / (N - 1) : 1.0;
        for (int i = 0; i < B; i++)
        {
            for (int j = 0; j < B; j++) cov[i, j] *= scale;
            cov[i, i] += 1e-6;  // Tikhonov regularization
        }

        // 3. Invert covariance
        Matrix<double> invCov;
        try
        {
            invCov = DenseMatrix.OfArray(cov).Inverse();
        }
        catch
        {
            invCov = DenseMatrix.CreateIdentity(B);
        }

        // 4. Mahalanobis distance per pixel
        var scores = new float[N];
        for (int r = 0; r < cube.Rows; r++)
        for (int c = 0; c < cube.Columns; c++)
        {
            double[] x = new double[B];
            for (int b = 0; b < B; b++)
                x[b] = cube.Cube[r * B + b][c] - mean[b];

            double d = 0;
            for (int i = 0; i < B; i++)
            {
                double tmp = 0;
                for (int j = 0; j < B; j++)
                    tmp += invCov[i, j] * x[j];
                d += x[i] * tmp;
            }
            scores[r * cube.Columns + c] = (float)Math.Max(d, 0.0);
        }

        return scores;
    }
}
