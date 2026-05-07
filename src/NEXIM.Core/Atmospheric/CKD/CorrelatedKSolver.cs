using System;
using System.Collections.Generic;
using System.Linq;
using NEXIM.Core.Atmospheric.DISORT;
using NEXIM.Core.Models;

namespace NEXIM.Core.Atmospheric.CKD;

/// <summary>
/// Correlated-k Distribution (CKD) solver for gaseous absorption.
/// For each spectral band, loops over g-points from the k-distribution table,
/// calls the DISORT plane-parallel RT solver for each g-point, and integrates
/// the results back to spectral radiance using quadrature weights.
///
/// Implementation follows the methodology of:
///   Lacis &amp; Oinas (1991) doi:10.1029/90JD01945 — CKD framework
///   Fu &amp; Liou (1992) doi:10.1175/1520-0469(1992)049&lt;1072:OTCMFI&gt;2.0.CO;2 — band model
///   Mlawer et al. (1997) doi:10.1029/97JD00237 — RRTM gas parameterisation
///   Gordon et al. (2022) doi:10.1016/j.jqsrt.2021.107949 — HITRAN2020 line data
/// </summary>
public sealed class CorrelatedKSolver
{
    private readonly KTableLibrary _library;
    private readonly DisortSolver  _disort;

    public CorrelatedKSolver(KTableLibrary library, int nStreams = 8)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _disort  = new DisortSolver(nStreams);
    }

    /// <summary>
    /// Compute monochromatic radiance for a single spectral band centre
    /// (in µm) using the CKD integration:
    ///   L(λ) ≈ Σ_g w_g × L_DISORT(k_g)
    /// where the sum is over all g-points in the k-table for that band.
    /// </summary>
    /// <param name="bandIndex">Index into the KTableLibrary (0-based).</param>
    /// <param name="species">Gas species with pre-computed k-tables.</param>
    /// <param name="profile">Atmospheric profile (pressure/temperature/composition).</param>
    /// <param name="disortBase">Base DisortInput with geometry already set.</param>
    /// <param name="referenceTemperature_K">Reference T for k-table interpolation (typically 250 K).</param>
    /// <returns>
    /// Integrated radiance and transmittance for this band, weighted over g-points.
    /// </returns>
    public BandRadiance SolveBand(
        int                            bandIndex,
        GasSpecies                     species,
        IReadOnlyList<ProfileLayer>    profile,
        DisortInput                    disortBase,
        double                         referenceTemperature_K = 250.0)
    {
        var table = _library.GetTable(species, bandIndex);
        if (table is null)
            throw new InvalidOperationException(
                $"No k-table for {species} band {bandIndex}.");

        int nG = table.GPoints.Length;
        double totalRadiance       = 0.0;
        double totalTransmittance  = 0.0;

        for (int ig = 0; ig < nG; ig++)
        {
            // Build DISORT layer optical depths for this g-point from CKD k-values
            var layers = BuildDisortLayers(
                disortBase, profile, table, ig, referenceTemperature_K);

        var input = new DisortInput
        {
            Layers              = layers,
            NStreams            = disortBase.NStreams,
            SolarIrradiance     = disortBase.SolarIrradiance,
            CosSolarZenith      = disortBase.CosSolarZenith,
            SurfaceAlbedo       = disortBase.SurfaceAlbedo,
            SurfacePlanckEmission = disortBase.SurfacePlanckEmission,
        };

            var output = _disort.Solve(input);

            // Accumulate weighted by quadrature weight for this g-point
            double wg = table.GWeights[ig];
            totalRadiance      += wg * output.NadirRadiance;
            totalTransmittance += wg * output.DirectBeamTransmittance;
        }

        return new BandRadiance(totalRadiance, totalTransmittance);
    }

    private static DisortLayer[] BuildDisortLayers(
        DisortInput                 baseInput,
        IReadOnlyList<ProfileLayer> profile,
        KDistributionTable          table,
        int                         gIndex,
        double                      refTemp)
    {
        int nLayers = profile.Count;
        var layers  = new DisortLayer[nLayers];

        for (int i = 0; i < nLayers; i++)
        {
            var orig  = baseInput.Layers[i < baseInput.Layers.Length ? i : baseInput.Layers.Length - 1];
            var pLayer = profile[i];

            // Get k-value for this layer's (pressure, temperature, g-point) via bilinear interp
            double kAbs = table.InterpolateK(pLayer.Pressure_hPa, pLayer.Temperature_K, refTemp, gIndex);

            // Convert k (cm²/g) × amount (g/cm²) to optical depth
            double amount = pLayer.AbsorberAmount_g_cm2;
            double tauGas = kAbs * amount;

            // Total optical depth = Rayleigh + aerosol + gas
            double tauTotal = orig.OpticalDepth + tauGas;
            if (tauTotal < 0) tauTotal = 0;

            // Preserve SSA from Rayleigh+aerosol; gas is purely absorbing (SSA=0)
            double ssaTotal = tauTotal > 1e-30
                ? orig.SingleScatteringAlbedo * orig.OpticalDepth / tauTotal
                : orig.SingleScatteringAlbedo;

            layers[i] = new DisortLayer
            {
                Index                  = i,
                OpticalDepth           = tauTotal,
                SingleScatteringAlbedo = ssaTotal,
                PhaseFunctionMoments   = orig.PhaseFunctionMoments,
                PlanckEmission         = orig.PlanckEmission,
            };
        }

        return layers;
    }
}

/// <summary>Radiance and transmittance result for a single spectral band from the CKD integration.</summary>
/// <param name="NadirRadiance">Upwelling nadir radiance [W m⁻² sr⁻¹ µm⁻¹].</param>
/// <param name="DirectBeamTransmittance">Transmittance of direct solar beam [0–1].</param>
public readonly record struct BandRadiance(double NadirRadiance, double DirectBeamTransmittance);

/// <summary>
/// Atmospheric profile layer carrying the CKD-relevant state for each model level.
/// This is used internally by CorrelatedKSolver to translate CKD k-values to optical depths.
/// </summary>
public sealed class ProfileLayer
{
    /// <summary>Mid-layer pressure [hPa].</summary>
    public double Pressure_hPa      { get; init; }
    /// <summary>Mid-layer temperature [K].</summary>
    public double Temperature_K     { get; init; }
    /// <summary>Absorber amount (column-integrated gas density) for this layer [g cm⁻²].</summary>
    public double AbsorberAmount_g_cm2 { get; init; }
}
