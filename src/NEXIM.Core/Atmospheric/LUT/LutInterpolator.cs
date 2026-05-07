using System;

namespace NEXIM.Core.Atmospheric.LUT;

/// <summary>
/// 5-dimensional multilinear (hypercubic) interpolation into the atmospheric LUT.
///
/// Axes: SZA × VZA × AOD × WVC × Wavelength (outer to inner).
/// The innermost axis is fields (Transmittance / PathRadiance / DownwellingIrradiance).
///
/// For each query point, locates the surrounding hypercube of 2⁵ = 32 vertices
/// and performs trilinear interpolation, giving a smooth function without
/// discontinuities at grid boundaries.
///
/// Performance: O(32) multiplications per query point per field. For a typical
/// 250-band scene pixel this is ~8000 multiplications — well under 10 ms.
///
/// Reference: Numerical Recipes §3.6 — multilinear interpolation.
/// </summary>
public sealed class LutInterpolator
{
    private readonly LutFormat _fmt;
    private readonly float[]   _data;

    /// <summary>LUT field index for total-column atmospheric transmittance.</summary>
    public const int FieldTransmittance   = 0;
    /// <summary>LUT field index for path radiance [W m⁻² sr⁻¹ µm⁻¹] normalised to unit solar.</summary>
    public const int FieldPathRadiance    = 1;
    /// <summary>LUT field index for downwelling irradiance at surface normalised to unit solar.</summary>
    public const int FieldDownwelling     = 2;

    public LutInterpolator(LutFormat format, float[] data)
    {
        _fmt  = format ?? throw new ArgumentNullException(nameof(format));
        _data = data   ?? throw new ArgumentNullException(nameof(data));
        if (_data.Length != format.TotalFloats)
            throw new ArgumentException(
                $"Data length {_data.Length} does not match expected {format.TotalFloats}.");
    }

    /// <summary>
    /// Interpolate all <see cref="LutFormat.NFields"/> output fields at a single query point.
    /// </summary>
    /// <returns>Array of length NFields with interpolated values.</returns>
    public double[] Interpolate(double sza_deg, double vza_deg, double aod, double wvc_g_cm2, double wl_um)
    {
        // Find bracket and fractional position for each axis
        BracketAxis(_fmt.SzaNodes, sza_deg, out int iSza, out double tSza);
        BracketAxis(_fmt.VzaNodes, vza_deg, out int iVza, out double tVza);
        BracketAxis(_fmt.AodNodes, aod,     out int iAod, out double tAod);
        BracketAxis(_fmt.WvcNodes, wvc_g_cm2, out int iWvc, out double tWvc);
        BracketAxis(_fmt.WavelengthNodes, wl_um, out int iWl, out double tWl);

        var result = new double[_fmt.NFields];

        // 5D multilinear: sum over 2^5 = 32 corner vertices
        for (int dSza = 0; dSza <= 1; dSza++)
        for (int dVza = 0; dVza <= 1; dVza++)
        for (int dAod = 0; dAod <= 1; dAod++)
        for (int dWvc = 0; dWvc <= 1; dWvc++)
        for (int dWl  = 0; dWl  <= 1; dWl++)
        {
            double w = (dSza == 0 ? 1 - tSza : tSza)
                     * (dVza == 0 ? 1 - tVza : tVza)
                     * (dAod == 0 ? 1 - tAod : tAod)
                     * (dWvc == 0 ? 1 - tWvc : tWvc)
                     * (dWl  == 0 ? 1 - tWl  : tWl);

            for (int iF = 0; iF < _fmt.NFields; iF++)
            {
                long idx = _fmt.FlatIndex(
                    iSza + dSza, iVza + dVza, iAod + dAod, iWvc + dWvc, iWl + dWl, iF);
                result[iF] += w * _data[idx];
            }
        }

        return result;
    }

    /// <summary>
    /// Find the lower bracket index and fractional position for value <paramref name="v"/>
    /// on a sorted ascending axis array.
    /// </summary>
    private static void BracketAxis(double[] axis, double v, out int idx, out double frac)
    {
        if (axis.Length == 0) { idx = 0; frac = 0; return; }
        if (axis.Length == 1) { idx = 0; frac = 0; return; }

        // Clamp to valid range
        if (v <= axis[0])        { idx = 0; frac = 0; return; }
        if (v >= axis[^1])       { idx = axis.Length - 2; frac = 1; return; }

        // Binary search for the lower bracket
        int lo = 0, hi = axis.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (axis[mid] <= v) lo = mid; else hi = mid - 1;
        }
        idx  = lo;
        double span = axis[lo + 1] - axis[lo];
        frac = span > 0 ? (v - axis[lo]) / span : 0.0;
    }
}
