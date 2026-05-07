// NEXIM — Material mapper.
//
// Matches an RGB cluster centroid to the nearest spectral library entry
// using CIE L*a*b* colour distance (ΔE 1976).
//
// The mapping pipeline:
//   sRGB → sRGB-linear → CIE XYZ (D65) → CIE L*a*b*
//
// For each library spectrum the apparent colour is computed by integrating
// its reflectance against the CIE 1931 2° colour-matching functions (CMFs)
// under the D65 illuminant, using trapezoidal quadrature on the embedded
// 43-point wavelength grid (0.40–2.50 µm at 0.05 µm spacing).
//
// The closest spectrum is the one minimising ΔE76 = Euclidean distance in Lab.

namespace NEXIM.Core.SpectralLibrary;

/// <summary>Simple RGB colour value. Components are [0–255].</summary>
public readonly record struct RgbColor(byte R, byte G, byte B);


public static class MaterialMapper
{
    // ── D65 white point (CIE 1931 2° observer) ────────────────────────────
    private const double Xn = 95.047;
    private const double Yn = 100.000;
    private const double Zn = 108.883;

    // ── CIE 1931 CMFs sampled at the 43-point standard grid ───────────────
    // Source: CIE 1931 2-degree observer tabulated values, interpolated to
    // 0.40–2.50 µm at 0.05 µm.  Beyond the CIE tabulation range (0.78 µm)
    // the CMFs are effectively zero; values here are rounded to 4 decimals.

    // x̄(λ) colour-matching function
    private static readonly double[] CMF_X =
    [
        0.1768, 0.2800, 0.3483, 0.3362, 0.2908, 0.1954, 0.0956, 0.0320, 0.0049, 0.0093,
        0.0633, 0.1655, 0.2904, 0.4334, 0.5945, 0.7621, 0.9163, 1.0263, 1.0622, 1.0026,
        0.8544, 0.6424, 0.4479, 0.2835, 0.1649, 0.0874, 0.0468, 0.0227, 0.0114, 0.0058,
        0.0029, 0.0014, 0.0007, 0.0003, 0.0002, 0.0001, 0.0000, 0.0000, 0.0000, 0.0000,
        0.0000, 0.0000, 0.0000
    ];

    // ȳ(λ) colour-matching function  (luminance-related)
    private static readonly double[] CMF_Y =
    [
        0.0199, 0.0402, 0.0840, 0.1655, 0.2908, 0.4349, 0.6070, 0.7570, 0.8670, 0.9551,
        0.9950, 0.9950, 0.9500, 0.8700, 0.7570, 0.6310, 0.5030, 0.3810, 0.2650, 0.1750,
        0.1070, 0.0610, 0.0320, 0.0170, 0.0082, 0.0041, 0.0021, 0.0010, 0.0005, 0.0002,
        0.0001, 0.0001, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000,
        0.0000, 0.0000, 0.0000
    ];

    // z̄(λ) colour-matching function
    private static readonly double[] CMF_Z =
    [
        0.8360, 1.3856, 1.7471, 1.7721, 1.6692, 1.2876, 0.8130, 0.4652, 0.2720, 0.1582,
        0.0782, 0.0422, 0.0203, 0.0087, 0.0039, 0.0021, 0.0017, 0.0011, 0.0008, 0.0003,
        0.0002, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000,
        0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000,
        0.0000, 0.0000, 0.0000
    ];

    // CIE standard illuminant D65 spectral power at the 43-point grid, normalised
    // so that ∫ D65 * ȳ dλ = 100.
    private static readonly double[] D65 =
    [
        82.75, 91.49, 93.43, 86.68, 104.86, 117.01, 117.81, 114.86, 115.92, 108.81,
        109.35, 107.80, 104.79, 107.69, 104.41, 104.05, 100.00, 96.33, 95.78, 88.69,
        90.01, 89.60, 87.70, 83.29, 83.70, 80.03, 80.21, 82.28, 78.28, 69.72,
        71.61, 74.35, 61.60, 69.89, 75.09, 63.59, 46.42, 66.81, 63.38, 54.43,
        50.71, 58.91, 61.92
    ];

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Find the library spectrum with the smallest ΔE76 to <paramref name="rgb"/>.
    /// </summary>
    public static SpectralSignature FindNearest(
        RgbColor rgb,
        IReadOnlyList<SpectralSignature> library)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (library.Count == 0)
            throw new ArgumentException("Library must not be empty.", nameof(library));

        double[] labQuery = RgbToLab(rgb);
        double   bestDist = double.MaxValue;
        SpectralSignature? best = null;

        foreach (var sig in library)
        {
            double[] labSig = SpectrumToLab(sig);
            double dist = DeltaE(labQuery, labSig);
            if (dist < bestDist) { bestDist = dist; best = sig; }
        }

        return best!;
    }

    /// <summary>
    /// Assign a library spectrum to each cluster centroid colour.
    /// </summary>
    public static SpectralSignature[] AssignMaterials(
        RgbColor[] clusterCentroids,
        IReadOnlyList<SpectralSignature> library)
    {
        var result = new SpectralSignature[clusterCentroids.Length];
        for (int i = 0; i < clusterCentroids.Length; i++)
            result[i] = FindNearest(clusterCentroids[i], library);
        return result;
    }

    // ── Colour-space helpers ──────────────────────────────────────────────

    /// <summary>Convert an <see cref="RgbColor"/> to CIE L*a*b* (D65).</summary>
    public static double[] RgbToLab(RgbColor c)
    {
        // sRGB → linear (gamma-expand)
        double r = SrgbToLinear(c.R / 255.0);
        double g = SrgbToLinear(c.G / 255.0);
        double b = SrgbToLinear(c.B / 255.0);

        // sRGB-linear → CIE XYZ (D65, IEC 61966-2-1 matrix)
        double X = 0.4124564 * r + 0.3575761 * g + 0.1804375 * b;
        double Y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        double Z = 0.0193339 * r + 0.1191920 * g + 0.9503041 * b;

        return XyzToLab(X * 100, Y * 100, Z * 100);
    }

    /// <summary>
    /// Compute the D65-illuminated apparent colour of a spectrum and convert to Lab.
    /// The spectrum is interpolated to the 43-point standard grid before integration.
    /// </summary>
    public static double[] SpectrumToLab(SpectralSignature sig)
    {
        // Interpolate spectrum to standard 43-point grid
        double[] refl = sig.InterpolateTo(StandardGrid);

        // Trapezoidal integration: X = ∫ refl * CMF_X * D65 dλ / N
        double X = 0, Y = 0, Z = 0;
        for (int i = 0; i < 43; i++)
        {
            double r = refl[i];
            X += r * CMF_X[i] * D65[i];
            Y += r * CMF_Y[i] * D65[i];
            Z += r * CMF_Z[i] * D65[i];
        }

        // Normalise so a perfect white (refl=1) gives XYZ = (Xn, Yn, Zn)
        double kNorm = 100.0 / (CMF_Y.Zip(D65, (y, d) => y * d).Sum());
        return XyzToLab(X * kNorm, Y * kNorm, Z * kNorm);
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    private static readonly double[] StandardGrid =
        Enumerable.Range(0, 43).Select(i => Math.Round(0.40 + i * 0.05, 2)).ToArray();

    private static double[] XyzToLab(double X, double Y, double Z)
    {
        double fx = LabF(X / Xn);
        double fy = LabF(Y / Yn);
        double fz = LabF(Z / Zn);

        double L = 116.0 * fy - 16.0;
        double a = 500.0 * (fx - fy);
        double b = 200.0 * (fy - fz);
        return [L, a, b];
    }

    private static double LabF(double t)
        => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;

    private static double SrgbToLinear(double c)
        => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double DeltaE(double[] lab1, double[] lab2)
    {
        double dL = lab1[0] - lab2[0];
        double da = lab1[1] - lab2[1];
        double db = lab1[2] - lab2[2];
        return Math.Sqrt(dL * dL + da * da + db * db);
    }
}
