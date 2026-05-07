// NEXIM — DISORT layer input/output data structures.
// These structs define the per-layer optical properties consumed by DisortSolver
// and the per-level flux/intensity output it produces.

namespace NEXIM.Core.Atmospheric.DISORT;

/// <summary>
/// Optical properties for a single homogeneous atmospheric layer
/// at a single wavelength / g-point.
///
/// DISORT treats the atmosphere as a stack of N plane-parallel layers,
/// each characterised by (τ, ω, phase function moments).
/// Layers are ordered top-to-bottom (layer 0 = top-of-atmosphere).
///
/// Reference: Stamnes et al. (1988) Appl. Opt. 27(12):2502.
/// doi:10.1364/AO.27.002502
/// </summary>
public sealed class DisortLayer
{
    /// <summary>Layer index, 0 = top of atmosphere.</summary>
    public int Index { get; init; }

    /// <summary>Total (extinction) optical depth of this layer (dimensionless, ≥ 0).</summary>
    public double OpticalDepth { get; init; }

    /// <summary>
    /// Single-scattering albedo ω ∈ [0,1].
    /// ω = 0: purely absorbing; ω = 1: purely scattering.
    /// </summary>
    public double SingleScatteringAlbedo { get; init; }

    /// <summary>
    /// Legendre polynomial expansion coefficients χ_l of the phase function.
    /// Length = nStreams (or more for delta-M scaling).
    /// χ_0 = 1 always (normalisation); χ_1 = asymmetry parameter g.
    /// </summary>
    public double[] PhaseFunctionMoments { get; init; } = [];

    /// <summary>
    /// Layer Planck emission in W/(m²·sr·µm) at the layer mid-point temperature.
    /// Non-zero only in the thermal IR (λ > ~3.5 µm).
    /// </summary>
    public double PlanckEmission { get; init; } = 0.0;
}

/// <summary>
/// Complete input to the DISORT solver for a single monochromatic calculation.
/// </summary>
public sealed class DisortInput
{
    /// <summary>
    /// Atmospheric layers, ordered top-of-atmosphere (index 0) to surface (index N−1).
    /// </summary>
    public required DisortLayer[] Layers { get; init; }

    /// <summary>Number of streams (must be 4, 8, or 16). Default: 8.</summary>
    public int NStreams { get; init; } = GaussLegendreQuadrature.DefaultStreams;

    /// <summary>
    /// Solar irradiance at TOA in W/(m²·µm) integrated over the band.
    /// Zero for thermal-only calculations.
    /// </summary>
    public double SolarIrradiance { get; init; }

    /// <summary>Cosine of the solar zenith angle μ₀ = cos(SZA).</summary>
    public double CosSolarZenith { get; init; }

    /// <summary>
    /// Surface Lambertian albedo (dimensionless, 0–1).
    /// Used as the lower boundary condition.
    /// </summary>
    public double SurfaceAlbedo { get; init; }

    /// <summary>
    /// Surface Planck emission in W/(m²·sr·µm).
    /// Non-zero only in thermal IR.
    /// </summary>
    public double SurfacePlanckEmission { get; init; } = 0.0;
}

/// <summary>
/// Output of a single monochromatic DISORT computation.
/// </summary>
public sealed class DisortOutput
{
    /// <summary>
    /// Upwelling (towards sensor) hemispherical flux at each level boundary
    /// in W/(m²·µm). Length = nLayers + 1 (TOA to surface).
    /// Index 0 = TOA upwelling (what a satellite sensor sees as irradiance).
    /// </summary>
    public required double[] UpwellingFlux { get; init; }

    /// <summary>
    /// Downwelling hemispherical flux at each level boundary in W/(m²·µm).
    /// Length = nLayers + 1. Index [nLayers] = surface downwelling irradiance.
    /// </summary>
    public required double[] DownwellingFlux { get; init; }

    /// <summary>
    /// Upwelling diffuse radiance in the nadir direction (μ = 1) at TOA,
    /// in W/(m²·sr·µm). This is the primary output for satellite simulations.
    /// </summary>
    public double NadirRadiance { get; init; }

    /// <summary>
    /// Direct (un-scattered) beam transmittance from TOA to surface.
    /// exp(−τ_total / μ₀).
    /// </summary>
    public double DirectBeamTransmittance { get; init; }
}
