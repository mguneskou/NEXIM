// NEXIM — HypercubeData.cs
// Unified in-memory representation of a loaded hyperspectral cube,
// whether read from .nxi or ENVI (.hdr/.img).
// BIL layout: Cube[r * Bands + b] is the float32 array of length Columns
// for spatial row r and spectral band b.

namespace NEXIM.Core.Models;

/// <summary>
/// In-memory hyperspectral cube produced by loading a .nxi or ENVI file.
/// </summary>
public sealed class HypercubeData
{
    /// <summary>BIL data: Cube[r * Bands + b] = float[Columns].</summary>
    public required float[][]  Cube           { get; init; }

    /// <summary>Centre wavelength of each band in µm.</summary>
    public required double[]   Wavelengths_um { get; init; }

    /// <summary>Number of spatial rows.</summary>
    public required int        Rows    { get; init; }

    /// <summary>Number of spectral bands.</summary>
    public required int        Bands   { get; init; }

    /// <summary>Number of spatial columns.</summary>
    public required int        Columns { get; init; }

    /// <summary>Source file name (no path), for display only.</summary>
    public          string     FileName { get; init; } = string.Empty;

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Extract the full spectrum for the pixel at (<paramref name="row"/>,
    /// <paramref name="col"/>) as a new float array of length Bands.
    /// </summary>
    public float[] GetSpectrum(int row, int col)
    {
        var spectrum = new float[Bands];
        for (int b = 0; b < Bands; b++)
            spectrum[b] = Cube[row * Bands + b][col];
        return spectrum;
    }

    /// <summary>
    /// Return a flat (Rows × Columns) array containing the values for
    /// spectral band <paramref name="bandIndex"/>.
    /// </summary>
    public float[] GetBand(int bandIndex)
    {
        var band = new float[Rows * Columns];
        for (int r = 0; r < Rows; r++)
            Array.Copy(Cube[r * Bands + bandIndex], 0, band, r * Columns, Columns);
        return band;
    }

    /// <summary>
    /// Find the band index whose centre wavelength is closest to
    /// <paramref name="wavelength_um"/>.
    /// </summary>
    public int FindBand(double wavelength_um)
    {
        int    best     = 0;
        double bestDist = double.MaxValue;
        for (int b = 0; b < Bands; b++)
        {
            double d = Math.Abs(Wavelengths_um[b] - wavelength_um);
            if (d < bestDist) { bestDist = d; best = b; }
        }
        return best;
    }
}
