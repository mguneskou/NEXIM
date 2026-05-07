// NEXIM — Novel End-to-eXtended hyperspectral IMage simulator
// Wavelength grid definition used throughout the radiative transfer pipeline.

namespace NEXIM.Core.Models;

/// <summary>
/// Represents a spectral wavelength grid in micrometres (µm).
/// Supports uniform and non-uniform grids spanning the UV–Far-IR range
/// (nominally 0.2–100 µm for full-physics modes).
/// </summary>
public sealed class WavelengthGrid
{
    /// <summary>Wavelength values in micrometres (µm), strictly ascending.</summary>
    public double[] Wavelengths_um { get; }

    /// <summary>Number of spectral points.</summary>
    public int Count => Wavelengths_um.Length;

    /// <summary>Shortest wavelength in µm.</summary>
    public double MinWavelength_um => Wavelengths_um[0];

    /// <summary>Longest wavelength in µm.</summary>
    public double MaxWavelength_um => Wavelengths_um[^1];

    /// <summary>
    /// Create a grid from an explicit array of wavelengths in µm.
    /// </summary>
    /// <param name="wavelengths_um">Wavelength values in µm, must be strictly ascending.</param>
    /// <exception cref="ArgumentException">Thrown when the array is empty or not strictly ascending.</exception>
    public WavelengthGrid(double[] wavelengths_um)
    {
        ArgumentNullException.ThrowIfNull(wavelengths_um);
        if (wavelengths_um.Length == 0)
            throw new ArgumentException("Wavelength grid must contain at least one value.", nameof(wavelengths_um));

        for (int i = 1; i < wavelengths_um.Length; i++)
        {
            if (wavelengths_um[i] <= wavelengths_um[i - 1])
                throw new ArgumentException(
                    $"Wavelengths must be strictly ascending. Violation at index {i}: {wavelengths_um[i - 1]} >= {wavelengths_um[i]}.",
                    nameof(wavelengths_um));
        }

        Wavelengths_um = wavelengths_um;
    }

    /// <summary>
    /// Create a uniform wavelength grid between <paramref name="start_um"/> and
    /// <paramref name="end_um"/> (inclusive) with a fixed spacing.
    /// </summary>
    /// <param name="start_um">Start wavelength in µm.</param>
    /// <param name="end_um">End wavelength in µm (inclusive).</param>
    /// <param name="spacing_um">Uniform spacing in µm.</param>
    public static WavelengthGrid Uniform(double start_um, double end_um, double spacing_um)
    {
        if (end_um <= start_um) throw new ArgumentException("end_um must be greater than start_um.");
        if (spacing_um <= 0) throw new ArgumentException("spacing_um must be positive.");

        int n = (int)Math.Round((end_um - start_um) / spacing_um) + 1;
        var grid = new double[n];
        for (int i = 0; i < n; i++)
            grid[i] = start_um + i * spacing_um;
        // Clamp last point to exact end_um to avoid floating-point overshoot
        grid[^1] = end_um;
        return new WavelengthGrid(grid);
    }

    /// <summary>
    /// Convert a wavelength in µm to wavenumber in cm⁻¹.
    /// </summary>
    public static double ToWavenumber_cm1(double wavelength_um) => 1e4 / wavelength_um;

    /// <summary>
    /// Convert a wavenumber in cm⁻¹ to wavelength in µm.
    /// </summary>
    public static double FromWavenumber_cm1(double wavenumber_cm1) => 1e4 / wavenumber_cm1;
}
