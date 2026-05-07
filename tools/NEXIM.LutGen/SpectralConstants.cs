// NEXIM.LutGen — Spectroscopic constants for H₂O in the UV–SWIR range.
//
// H₂O band parameters derived from:
//   Gordon et al. (2022) HITRAN2020. JQSRT 277:107949.
//   doi:10.1016/j.jqsrt.2021.107949
//
// Aggregate band model: each near-IR rotation-vibration band cluster is
// represented by (λ₀, FWHM, σ_peak, E_lower) where σ_peak is the peak
// absorption cross-section [cm²/molecule] at reference conditions
// (T₀ = 296 K, P₀ = 1013.25 hPa).  Line profiles are Lorentzian (valid for
// tropospheric P > 100 hPa) with pressure-broadened half-width γ.
//
// The aggregate line model (20 super-lines per band cluster) reproduces the
// correct spectral integral, pressure/temperature scaling, and within-interval
// k-distribution variance without requiring the full HITRAN line list.
//
// Reference for air-broadened half-widths:
//   Clough et al. (1989) JQSRT 41(3):157–184.
//   doi:10.1016/0022-4073(89)90028-6

namespace NEXIM.LutGen;

/// <summary>
/// Parameters for one H₂O vibration-rotation band cluster.
/// </summary>
internal readonly struct H2OBandCluster
{
    /// <summary>Band centre wavelength [µm].</summary>
    public double Centre_um { get; init; }

    /// <summary>Gaussian envelope full-width at half-maximum [µm].</summary>
    public double Fwhm_um { get; init; }

    /// <summary>
    /// Peak absorption cross-section [cm²/molecule] at T₀=296 K, P₀=1013.25 hPa.
    /// </summary>
    public double SigmaPeak_cm2 { get; init; }

    /// <summary>Effective lower-state energy [cm⁻¹] for temperature scaling.</summary>
    public double E_lower_cm1 { get; init; }
}

/// <summary>
/// Embedded spectroscopic constants used by <see cref="KDistributionComputer"/>.
/// </summary>
internal static class SpectralConstants
{
    // ── Physical constants ─────────────────────────────────────────────────────
    /// <summary>Reference temperature [K] for line-strength normalisation.</summary>
    public const double T0_K = 296.0;

    /// <summary>Reference pressure [hPa].</summary>
    public const double P0_hPa = 1013.25;

    /// <summary>hc/k_B [cm·K] — for Boltzmann energy exponent.</summary>
    public const double HcOverKb = 1.4387769;   // cm·K

    /// <summary>Air-broadened half-width [cm⁻¹/atm] for H₂O (mean value, Clough 1989).</summary>
    public const double H2OGamma0 = 0.090;

    /// <summary>Temperature exponent for pressure-broadened half-width: γ ∝ (T₀/T)^n.</summary>
    public const double GammaTempExp = 0.75;

    /// <summary>MT_CKD far-wing continuum baseline [cm²/molecule].</summary>
    public const double H2OContinuumBase = 5.0e-27;

    // ── H₂O band clusters (0.4–2.5 µm) ───────────────────────────────────────
    // Aggregate parameters from HITRAN2020 integrated band strengths.
    // Bands outside the LUT range (< 0.4 µm, > 2.5 µm) contribute negligible
    // tails within the range; only the 2.7 µm band has a significant high-ν
    // wing that overlaps the 2.3–2.5 µm region.
    public static readonly H2OBandCluster[] H2OBands =
    [
        // λ₀ (µm)   FWHM (µm)   σ_peak (cm²)   E_lower (cm⁻¹)
        // ──────────────────────────────────────────────────────
        // ν₁+ν₂+ν₃ combination band
        new() { Centre_um = 0.718, Fwhm_um = 0.022, SigmaPeak_cm2 = 5.0e-25, E_lower_cm1 = 250 },
        // 2ν₁+ν₂ combination band
        new() { Centre_um = 0.820, Fwhm_um = 0.022, SigmaPeak_cm2 = 1.8e-24, E_lower_cm1 = 280 },
        // ν₁+ν₃ combination band (strong)
        new() { Centre_um = 0.940, Fwhm_um = 0.032, SigmaPeak_cm2 = 2.2e-23, E_lower_cm1 = 200 },
        // ν₂+ν₃ combination band
        new() { Centre_um = 1.135, Fwhm_um = 0.038, SigmaPeak_cm2 = 1.5e-23, E_lower_cm1 = 180 },
        // 2ν₃ band (strong near-IR water vapour band)
        new() { Centre_um = 1.380, Fwhm_um = 0.055, SigmaPeak_cm2 = 8.0e-22, E_lower_cm1 = 210 },
        // ν₁+ν₃/ν₁ band (strong, dominant in 1.9 µm window boundary)
        new() { Centre_um = 1.872, Fwhm_um = 0.085, SigmaPeak_cm2 = 2.0e-22, E_lower_cm1 = 160 },
        // ν₃ fundamental (2.7 µm) — high-ν wing overlaps 2.3–2.5 µm
        new() { Centre_um = 2.700, Fwhm_um = 0.180, SigmaPeak_cm2 = 5.0e-20, E_lower_cm1 = 130 },
    ];

    // ── Pressure levels for k-tables [hPa] ────────────────────────────────────
    public static readonly double[] PressureLevels_hPa =
        [1013.25, 850.0, 700.0, 500.0, 300.0, 100.0, 30.0, 10.0];

    // ── Temperature offsets for k-tables [K] ──────────────────────────────────
    // Centred on the standard-atmosphere temperature at each pressure level.
    public static readonly double[] TemperatureOffsets_K =
        [-30.0, -15.0, 0.0, 15.0, 30.0];

    // ── 16-point Gauss-Legendre quadrature on [0, 1] ─────────────────────────
    // Points/weights from Abramowitz & Stegun Table 25.4, transformed to [0,1].
    // ∑ GWeights = 1.0 (required for CKD integration).
    public static readonly double[] GPoints =
    [
        0.0052995325041750, 0.0277124884633837, 0.0671843988060841, 0.1222977958224985,
        0.1910618777986781, 0.2709916111713863, 0.3591982246103705, 0.4524937450811813,
        0.5475062549188187, 0.6408017753896295, 0.7290083888286137, 0.8089381222013219,
        0.8777022041775015, 0.9328156011939159, 0.9722875115366163, 0.9947004674958250,
    ];
    public static readonly double[] GWeights =
    [
        0.0135762297058770, 0.0311267619693240, 0.0475792558412464, 0.0623144856277790,
        0.0747979944082883, 0.0845782596975012, 0.0913017075224617, 0.0947253052275342,
        0.0947253052275342, 0.0913017075224617, 0.0845782596975012, 0.0747979944082883,
        0.0623144856277790, 0.0475792558412464, 0.0311267619693240, 0.0135762297058770,
    ];

    /// <summary>
    /// Compute the H₂O mean absorption cross-section [cm²/molecule] at
    /// reference conditions for a given wavelength by summing band Gaussian envelopes.
    /// </summary>
    public static double H2OMeanCrossSection(double lambda_um)
    {
        double sigma = H2OContinuumBase;
        foreach (var band in H2OBands)
        {
            double dlambda = lambda_um - band.Centre_um;
            double sigma_gauss = band.Fwhm_um / (2.0 * Math.Sqrt(2.0 * Math.Log(2.0)));
            sigma += band.SigmaPeak_cm2 * Math.Exp(-dlambda * dlambda / (2.0 * sigma_gauss * sigma_gauss));
        }
        return sigma;
    }

    /// <summary>
    /// Temperature scaling factor for H₂O line strength:
    ///   f(T) = (T₀/T)^0.5 × exp(−E_lower × (hc/k_B) × (1/T − 1/T₀))
    /// </summary>
    public static double H2OTempScaling(double T_K, double E_lower_cm1)
    {
        return Math.Sqrt(T0_K / T_K)
             * Math.Exp(-E_lower_cm1 * HcOverKb * (1.0 / T_K - 1.0 / T0_K));
    }
}
