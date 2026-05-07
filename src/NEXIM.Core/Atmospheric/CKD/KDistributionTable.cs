// NEXIM — k-Distribution Table data model.
//
// The correlated-k distribution (CKD) method (Lacis & Oinas 1991) reorders
// monochromatic absorption coefficients into a smooth, monotonically increasing
// function of the cumulative probability g ∈ [0,1], enabling efficient
// numerical quadrature over spectral bands instead of line-by-line integration.
//
// k-tables are pre-computed from the HITRAN molecular spectroscopic database:
//   Gordon et al. (2022) HITRAN2020. J. Quant. Spectrosc. Radiat. Transfer 277:107949.
//   doi:10.1016/j.jqsrt.2021.107949
//
// The CKD approach is described in:
//   Lacis, A.A. & Oinas, V. (1991). J. Geophys. Res. Atmos. 96(D5):9027.
//   doi:10.1029/90JD01945
//
// Operational implementation reference (RRTM/RRTMG):
//   Mlawer et al. (1997). J. Geophys. Res. Atmos. 102(D14):16663.
//   doi:10.1029/97JD00237
//   Iacono et al. (2008). J. Geophys. Res. 113:D13103.
//   doi:10.1029/2008JD009944

namespace NEXIM.Core.Atmospheric.CKD;

/// <summary>
/// Identifies a gas species tracked in the NEXIM k-table library.
/// Species names follow HITRAN molecule numbering conventions.
/// </summary>
public enum GasSpecies
{
    H2O  = 1,
    CO2  = 2,
    O3   = 3,
    N2O  = 4,
    CO   = 5,
    CH4  = 6,
    O2   = 7,
}

/// <summary>
/// A k-distribution table for a single gas species over a single spectral band.
///
/// The table discretises the probability space g ∈ [0,1] into
/// <see cref="NGPoints"/> quadrature points.  For each combination of
/// (pressure level, temperature level, g-point), it stores an absorption
/// coefficient k [cm²/molecule] that can be directly passed to the
/// DISORT solver as an equivalent monochromatic problem.
///
/// Layout of <see cref="KValues"/> (row-major, C-order):
///   k[ip, it, ig] = KValues[ip * NTempLevels * NGPoints + it * NGPoints + ig]
/// where ip = pressure index, it = temperature index, ig = g-point index.
/// </summary>
public sealed class KDistributionTable
{
    /// <summary>Gas species this table applies to.</summary>
    public GasSpecies Species { get; init; }

    /// <summary>Spectral band centre wavelength in µm.</summary>
    public double BandCentre_um { get; init; }

    /// <summary>Spectral band full width in µm.</summary>
    public double BandWidth_um { get; init; }

    /// <summary>
    /// Pressure grid in hPa, strictly descending (surface to TOA).
    /// Typical range: 1013.25 hPa (surface) → ~0.01 hPa (stratopause).
    /// </summary>
    public double[] PressureLevels_hPa { get; init; } = [];

    /// <summary>
    /// Temperature perturbation grid in Kelvin, centred on a reference profile.
    /// Typical values: [−30, −15, 0, +15, +30] K.
    /// </summary>
    public double[] TemperatureOffsets_K { get; init; } = [];

    /// <summary>
    /// Quadrature abscissae in g-space [0,1].
    /// Gauss-Legendre points recommended (Lacis &amp; Oinas 1991).
    /// </summary>
    public double[] GPoints { get; init; } = [];

    /// <summary>
    /// Gauss-Legendre quadrature weights corresponding to <see cref="GPoints"/>.
    /// Must satisfy ∑ weights = 1.
    /// </summary>
    public double[] GWeights { get; init; } = [];

    /// <summary>
    /// Absorption coefficients k [cm²/molecule], stored in row-major order
    /// (pressure × temperature × g-point). See class documentation for indexing.
    /// </summary>
    public float[] KValues { get; init; } = [];

    /// <summary>Number of pressure levels.</summary>
    public int NPressureLevels => PressureLevels_hPa.Length;

    /// <summary>Number of temperature offset levels.</summary>
    public int NTempLevels => TemperatureOffsets_K.Length;

    /// <summary>Number of g-quadrature points.</summary>
    public int NGPoints => GPoints.Length;

    /// <summary>
    /// Retrieve the absorption coefficient k [cm²/molecule] at the given
    /// pressure and temperature by bilinear interpolation in (p, T) space,
    /// for a specific g-point index.
    /// </summary>
    /// <param name="pressure_hPa">Actual layer pressure in hPa.</param>
    /// <param name="temperature_K">Actual layer temperature in K.</param>
    /// <param name="referenceTemperature_K">Reference temperature at this pressure level (from standard atmosphere).</param>
    /// <param name="gIndex">Index into the g-point array.</param>
    public double InterpolateK(double pressure_hPa, double temperature_K,
        double referenceTemperature_K, int gIndex)
    {
        // Find bracketing pressure indices (descending array — search reversed)
        int ip = FindBracket(PressureLevels_hPa, pressure_hPa, descending: true);
        int ip1 = Math.Min(ip + 1, NPressureLevels - 1);
        double tp = ip1 == ip ? 0.0 :
            (pressure_hPa - PressureLevels_hPa[ip]) /
            (PressureLevels_hPa[ip1] - PressureLevels_hPa[ip]);

        // Temperature offset relative to reference profile
        double tOffset = temperature_K - referenceTemperature_K;
        int it = FindBracket(TemperatureOffsets_K, tOffset, descending: false);
        int it1 = Math.Min(it + 1, NTempLevels - 1);
        double tt = it1 == it ? 0.0 :
            (tOffset - TemperatureOffsets_K[it]) /
            (TemperatureOffsets_K[it1] - TemperatureOffsets_K[it]);

        // Bilinear interpolation
        double k00 = KValues[ip  * NTempLevels * NGPoints + it  * NGPoints + gIndex];
        double k10 = KValues[ip1 * NTempLevels * NGPoints + it  * NGPoints + gIndex];
        double k01 = KValues[ip  * NTempLevels * NGPoints + it1 * NGPoints + gIndex];
        double k11 = KValues[ip1 * NTempLevels * NGPoints + it1 * NGPoints + gIndex];

        return (1 - tp) * ((1 - tt) * k00 + tt * k01) +
               tp       * ((1 - tt) * k10 + tt * k11);
    }

    private static int FindBracket(double[] arr, double value, bool descending)
    {
        if (descending)
        {
            // Array is descending: find largest index where arr[i] >= value
            for (int i = 0; i < arr.Length - 1; i++)
                if (arr[i + 1] < value) return i;
            return arr.Length - 2;
        }
        else
        {
            // Array is ascending: find largest index where arr[i] <= value
            int lo = 0, hi = arr.Length - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (arr[mid] <= value) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
    }
}
