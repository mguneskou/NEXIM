// NEXIM — Spectral signature: a measured or modelled reflectance spectrum.

namespace NEXIM.Core.SpectralLibrary;

/// <summary>
/// A single spectral reflectance signature from the library.
/// Wavelength and reflectance arrays are co-indexed and share the same length.
/// </summary>
public sealed class SpectralSignature
{
    /// <summary>Unique name (e.g. "Healthy Grass - USGS splib07").</summary>
    public required string   Name            { get; init; }

    /// <summary>Broad material category (Vegetation, Soil, Water, Urban, Mineral, Snow).</summary>
    public required string   Category        { get; init; }

    /// <summary>Wavelength grid [µm], ascending.</summary>
    public required double[] Wavelengths_um  { get; init; }

    /// <summary>Hemispherical reflectance [0–1] at each wavelength.</summary>
    public required double[] Reflectance     { get; init; }

    /// <summary>Source / citation string (may be empty).</summary>
    public string Citation { get; init; } = string.Empty;

    /// <summary>
    /// Linearly interpolate this spectrum onto <paramref name="targetGrid_um"/>.
    /// Values outside the source range are clamped to the nearest endpoint.
    /// </summary>
    public double[] InterpolateTo(double[] targetGrid_um)
    {
        var result = new double[targetGrid_um.Length];
        double[] xs = Wavelengths_um;
        double[] ys = Reflectance;

        for (int i = 0; i < targetGrid_um.Length; i++)
        {
            double wl = targetGrid_um[i];
            if (wl <= xs[0]) { result[i] = ys[0]; continue; }
            if (wl >= xs[^1]) { result[i] = ys[^1]; continue; }

            int lo = Array.BinarySearch(xs, wl);
            if (lo < 0) lo = ~lo - 1;
            lo = Math.Clamp(lo, 0, xs.Length - 2);

            double t = (wl - xs[lo]) / (xs[lo + 1] - xs[lo]);
            result[i] = ys[lo] + t * (ys[lo + 1] - ys[lo]);
        }
        return result;
    }
}
