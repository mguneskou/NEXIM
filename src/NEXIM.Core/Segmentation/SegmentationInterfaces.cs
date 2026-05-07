// NEXIM — Segmentation interfaces and shared types.
// All segmentation algorithms operate on a flat array of spectral feature
// vectors extracted from a hyperspectral cube and return per-pixel class labels.

namespace NEXIM.Core.Segmentation;

/// <summary>
/// Per-pixel feature vector extracted from a hyperspectral cube.
/// </summary>
public sealed class PixelFeature
{
    /// <summary>Spatial row index.</summary>
    public int Row { get; init; }

    /// <summary>Spatial column index.</summary>
    public int Column { get; init; }

    /// <summary>
    /// Feature vector — typically the spectral radiance or reflectance values
    /// for each band, optionally normalised.
    /// </summary>
    public required double[] Features { get; init; }
}

/// <summary>
/// Result of a segmentation run.
/// </summary>
public sealed class SegmentationResult
{
    /// <summary>Per-pixel class labels (same order as input pixel list).</summary>
    public required int[] Labels { get; init; }

    /// <summary>Number of distinct classes found (including noise class −1 for DBSCAN).</summary>
    public int ClassCount { get; init; }

    /// <summary>Optional per-class centroid vectors in feature space.</summary>
    public double[][]? Centroids { get; init; }

    /// <summary>Optional per-pixel soft membership probabilities [pixel][class].</summary>
    public double[][]? Probabilities { get; init; }
}

/// <summary>
/// Contract for all spectral segmentation algorithms.
/// </summary>
public interface ISegmenter
{
    /// <summary>
    /// Segment a list of pixel feature vectors.
    /// </summary>
    /// <param name="pixels">Input pixel feature vectors.</param>
    /// <returns>Segmentation result with per-pixel class labels.</returns>
    SegmentationResult Segment(IReadOnlyList<PixelFeature> pixels);
}

/// <summary>
/// Utility helpers for building pixel feature lists from BIL cubes.
/// </summary>
public static class FeatureExtractor
{
    /// <summary>
    /// Extract all pixels from a BIL cube into a flat list of feature vectors.
    /// Features = raw spectral values (float cast to double).
    /// </summary>
    /// <param name="cube">BIL cube: cube[r*bands+b][c].</param>
    /// <param name="rows">Spatial rows.</param>
    /// <param name="bands">Spectral bands.</param>
    /// <param name="columns">Spatial columns.</param>
    public static List<PixelFeature> FromBilCube(
        float[][] cube, int rows, int bands, int columns)
    {
        ArgumentNullException.ThrowIfNull(cube);
        var result = new List<PixelFeature>(rows * columns);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                double[] feat = new double[bands];
                for (int b = 0; b < bands; b++)
                    feat[b] = cube[r * bands + b][c];
                result.Add(new PixelFeature { Row = r, Column = c, Features = feat });
            }
        return result;
    }

    /// <summary>
    /// L2-normalise each feature vector in-place (unit length).
    /// Zero-norm vectors are left unchanged.
    /// </summary>
    public static void NormaliseL2(List<PixelFeature> pixels)
    {
        foreach (var px in pixels)
        {
            double norm = 0.0;
            for (int i = 0; i < px.Features.Length; i++)
                norm += px.Features[i] * px.Features[i];
            norm = Math.Sqrt(norm);
            if (norm < 1e-12) continue;
            for (int i = 0; i < px.Features.Length; i++)
                px.Features[i] /= norm;
        }
    }

    /// <summary>
    /// Min-max normalise each feature dimension to [0, 1].
    /// Constant dimensions are left as 0.
    /// </summary>
    public static void NormaliseMinMax(List<PixelFeature> pixels)
    {
        if (pixels.Count == 0) return;
        int dims = pixels[0].Features.Length;
        double[] mins = new double[dims];
        double[] maxs = new double[dims];
        Array.Fill(mins, double.MaxValue);
        Array.Fill(maxs, double.MinValue);

        foreach (var px in pixels)
            for (int i = 0; i < dims; i++)
            {
                if (px.Features[i] < mins[i]) mins[i] = px.Features[i];
                if (px.Features[i] > maxs[i]) maxs[i] = px.Features[i];
            }

        foreach (var px in pixels)
            for (int i = 0; i < dims; i++)
            {
                double range = maxs[i] - mins[i];
                px.Features[i] = range < 1e-12 ? 0.0 : (px.Features[i] - mins[i]) / range;
            }
    }
}
