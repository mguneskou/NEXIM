// NEXIM — SAM (Spectral Angle Mapper) propagation dashboard.
// SAM computes the angle between two spectral vectors; smaller angle = higher
// similarity.  This class:
//   1. Classifies every pixel by comparing it to a library of reference spectra
//      ("endmembers"), assigning the closest endmember by spectral angle.
//   2. Exposes per-pixel spectral angle maps for the dashboard overlay.
//
// SAM is threshold-free and illumination-invariant (it is normalised by vector
// magnitude), making it well-suited to hyperspectral scene analysis.
//
// Reference: Kruse et al. (1993) "The spectral image processing system (SIPS)
//   — interactive visualisation and analysis of imaging spectrometer data."
//   Remote Sensing of Environment 44:145–163.

namespace NEXIM.Core.Segmentation;

/// <summary>
/// A reference endmember spectrum for SAM classification.
/// </summary>
public sealed class Endmember
{
    /// <summary>Human-readable material name.</summary>
    public required string Name { get; init; }

    /// <summary>Reference spectral vector (one value per band).</summary>
    public required double[] Spectrum { get; init; }
}

/// <summary>
/// Per-pixel result from a SAM classification run.
/// </summary>
public sealed class SamPixelResult
{
    /// <summary>Spatial row index.</summary>
    public int Row { get; init; }

    /// <summary>Spatial column index.</summary>
    public int Column { get; init; }

    /// <summary>Index of the assigned endmember (−1 if all angles exceed threshold).</summary>
    public int AssignedEndmember { get; init; }

    /// <summary>Spectral angle to the assigned endmember in radians.</summary>
    public double MinAngle_rad { get; init; }

    /// <summary>Spectral angle in radians to every endmember (parallel array).</summary>
    public required double[] Angles_rad { get; init; }
}

/// <summary>
/// SAM-based spectral classification and propagation.
/// </summary>
public sealed class SamPropagator
{
    readonly IReadOnlyList<Endmember> _endmembers;
    readonly double _threshold_rad;

    /// <param name="endmembers">Reference endmember library.</param>
    /// <param name="threshold_rad">
    ///   Maximum spectral angle (radians) for a positive match.
    ///   Pixels where all angles exceed the threshold are assigned −1.
    ///   Default ≈ 0.1 rad (~5.7°), a commonly used SAM threshold.
    /// </param>
    public SamPropagator(
        IReadOnlyList<Endmember> endmembers,
        double threshold_rad = 0.1)
    {
        ArgumentNullException.ThrowIfNull(endmembers);
        if (endmembers.Count == 0)
            throw new ArgumentException("At least one endmember is required.", nameof(endmembers));
        if (threshold_rad <= 0)
            throw new ArgumentOutOfRangeException(nameof(threshold_rad));
        _endmembers    = endmembers;
        _threshold_rad = threshold_rad;
    }

    /// <summary>
    /// Classify all <paramref name="pixels"/> against the endmember library.
    /// </summary>
    /// <returns>
    /// A <see cref="SamPixelResult"/> for each input pixel, in the same order.
    /// </returns>
    public IReadOnlyList<SamPixelResult> Classify(IReadOnlyList<PixelFeature> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        int m      = _endmembers.Count;
        var result = new SamPixelResult[pixels.Count];

        for (int i = 0; i < pixels.Count; i++)
        {
            double[] x      = pixels[i].Features;
            double[] angles = new double[m];
            int      best   = -1;
            double   minAng = double.MaxValue;

            for (int e = 0; e < m; e++)
            {
                angles[e] = SpectralAngle(x, _endmembers[e].Spectrum);
                if (angles[e] < minAng) { minAng = angles[e]; best = e; }
            }

            result[i] = new SamPixelResult
            {
                Row                = pixels[i].Row,
                Column             = pixels[i].Column,
                AssignedEndmember  = minAng <= _threshold_rad ? best : -1,
                MinAngle_rad       = minAng,
                Angles_rad         = angles,
            };
        }

        return result;
    }

    /// <summary>
    /// Convert SAM classification results to a <see cref="SegmentationResult"/>.
    /// Pixels with <c>AssignedEndmember == −1</c> receive label −1.
    /// </summary>
    public static SegmentationResult ToSegmentationResult(
        IReadOnlyList<SamPixelResult> samResults, int endmemberCount)
    {
        int[] labels = samResults.Select(r => r.AssignedEndmember).ToArray();
        return new SegmentationResult
        {
            Labels     = labels,
            ClassCount = endmemberCount,
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Spectral angle between two vectors in radians ∈ [0, π/2].
    /// Returns π/2 for zero-norm vectors.
    /// </summary>
    public static double SpectralAngle(double[] a, double[] b)
    {
        int    dims = Math.Min(a.Length, b.Length);
        double dot  = 0.0, normA = 0.0, normB = 0.0;
        for (int i = 0; i < dims; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        normA = Math.Sqrt(normA);
        normB = Math.Sqrt(normB);
        if (normA < 1e-12 || normB < 1e-12) return Math.PI / 2.0;
        double cosAngle = dot / (normA * normB);
        // Clamp to [-1, 1] to guard against floating-point drift
        cosAngle = Math.Clamp(cosAngle, -1.0, 1.0);
        return Math.Acos(cosAngle);
    }
}
