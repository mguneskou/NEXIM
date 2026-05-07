// NEXIM — Atmospheric RT input/output value objects.
// These are the data-transfer objects that cross the IAtmosphericRT boundary.

namespace NEXIM.Core.Models;

/// <summary>
/// Geometry for the sun–sensor–scene configuration.
/// Angles are defined in the standard remote sensing convention.
/// </summary>
public sealed class ViewGeometry
{
    /// <summary>Solar zenith angle in degrees (0 = overhead sun).</summary>
    public double SolarZenith_deg { get; init; }

    /// <summary>Solar azimuth angle in degrees, measured clockwise from North.</summary>
    public double SolarAzimuth_deg { get; init; }

    /// <summary>Sensor/view zenith angle in degrees (0 = nadir).</summary>
    public double ViewZenith_deg { get; init; }

    /// <summary>Sensor/view azimuth angle in degrees, measured clockwise from North.</summary>
    public double ViewAzimuth_deg { get; init; }
}

/// <summary>
/// Aerosol parameters used in all three RT modes.
/// </summary>
public sealed class AerosolParameters
{
    /// <summary>Aerosol optical depth at 550 nm (dimensionless).</summary>
    public double Aod550 { get; init; } = 0.1;

    /// <summary>Ångström exponent relating AOD across wavelengths.</summary>
    public double AngstromExponent { get; init; } = 1.3;

    /// <summary>Single-scattering albedo at 550 nm (dimensionless, 0–1).</summary>
    public double Ssa550 { get; init; } = 0.95;

    /// <summary>Henyey-Greenstein asymmetry parameter at 550 nm (dimensionless, −1 to 1).</summary>
    public double AsymmetryParameter { get; init; } = 0.7;

    /// <summary>Scale AOD to an arbitrary wavelength using the Ångström relation.</summary>
    public double ScaleAod(double wavelength_um)
    {
        // AOD(λ) = AOD(0.55) × (λ / 0.55)^(−α)
        return Aod550 * Math.Pow(wavelength_um / 0.55, -AngstromExponent);
    }
}

/// <summary>
/// Complete input to the atmospheric radiative transfer pipeline.
/// Consumed by all three RT modes (Fast LUT, Accurate CKD+DISORT, Full-Physics Monte Carlo).
/// </summary>
public sealed class AtmosphericInput
{
    /// <summary>Spectral grid over which to compute radiance.</summary>
    public required WavelengthGrid Grid { get; init; }

    /// <summary>Atmospheric composition and thermodynamic profile.</summary>
    public required AtmosphericProfile Profile { get; init; }

    /// <summary>Sun–sensor–scene geometry.</summary>
    public required ViewGeometry Geometry { get; init; }

    /// <summary>Aerosol parameterisation.</summary>
    public AerosolParameters Aerosol { get; init; } = new();

    /// <summary>
    /// Surface (Lambertian) reflectance as a function of wavelength.
    /// Length must equal <see cref="Grid"/>.Count, or be a single-element
    /// array for a spectrally flat surface.
    /// </summary>
    public required double[] SurfaceReflectance { get; init; }

    /// <summary>
    /// Enable scene-coupled adjacency correction in Mode 2 (CKD+DISORT).
    /// Applies the PSF-convolution method of Tanré et al. (1981)
    /// Appl. Opt. 20(20):3676, doi:10.1364/AO.20.003676.
    /// Iterates 2–3 times to convergence.
    /// Default: false (adjacency correction disabled).
    /// </summary>
    public bool EnableAdjacency { get; init; } = false;

    /// <summary>
    /// Optional 3D cloud field for Mode 3 (Monte Carlo).
    /// When null, a clear-sky atmosphere is assumed.
    /// </summary>
    public CloudField? CloudField { get; init; } = null;
}

/// <summary>
/// Result of an atmospheric RT computation.
/// </summary>
public sealed class RadianceResult
{
    /// <summary>
    /// Upwelling spectral radiance at sensor altitude in W/(m²·sr·µm),
    /// one value per wavelength in the input grid.
    /// </summary>
    public required double[] UpwellingRadiance { get; init; }

    /// <summary>
    /// Downwelling irradiance at the surface in W/(m²·µm),
    /// one value per wavelength. Useful for reflectance retrieval validation.
    /// </summary>
    public required double[] DownwellingIrradiance { get; init; }

    /// <summary>
    /// Atmospheric path radiance (intrinsic atmospheric emission + scattering)
    /// in W/(m²·sr·µm), before surface contribution.
    /// </summary>
    public required double[] PathRadiance { get; init; }

    /// <summary>
    /// Total atmospheric transmittance (dimensionless, 0–1) from surface to sensor,
    /// combining all gas absorption and scattering losses.
    /// </summary>
    public required double[] Transmittance { get; init; }

    /// <summary>Wavelength grid matching input, in µm.</summary>
    public required WavelengthGrid Grid { get; init; }

    /// <summary>Wall-clock time taken to compute this result in milliseconds.</summary>
    public double ComputeTime_ms { get; init; }

    /// <summary>Identifies which RT mode produced this result.</summary>
    public required string ModeName { get; init; }
}

/// <summary>
/// 3D cloud field passed to Mode 3 (Monte Carlo) atmospheric RT.
/// Defines a voxel grid of liquid water content and droplet radius.
/// </summary>
public sealed class CloudField
{
    /// <summary>
    /// 3D liquid water content grid [x, y, z] in g/m³.
    /// Zero or negative values indicate cloud-free voxels.
    /// </summary>
    public required float[,,] LwcGrid_gm3 { get; init; }

    /// <summary>
    /// Per-voxel effective droplet radius [x, y, z] in µm.
    /// When <c>null</c>, <see cref="UniformEffectiveRadius_um"/> is used for all voxels.
    /// </summary>
    public float[,,]? EffectiveRadiusGrid_um { get; init; }

    /// <summary>Uniform effective droplet radius in µm (used when <see cref="EffectiveRadiusGrid_um"/> is null).
    /// Typical stratocumulus value: 10 µm.</summary>
    public double UniformEffectiveRadius_um { get; init; } = 10.0;

    /// <summary>Uniform voxel spacing in km (same in X, Y and Z).</summary>
    public double GridSpacing_km { get; init; } = 0.1;

    /// <summary>X-origin of the cloud grid in km (horizontal, cross-track).</summary>
    public double OriginX_km { get; init; } = 0.0;

    /// <summary>Y-origin of the cloud grid in km (horizontal, along-track).</summary>
    public double OriginY_km { get; init; } = 0.0;

    /// <summary>Altitude of the lowest voxel base in km above MSL.</summary>
    public double BaseAltitude_km { get; init; } = 2.0;

    /// <summary>Number of voxels along the X axis.</summary>
    public int NX => LwcGrid_gm3.GetLength(0);

    /// <summary>Number of voxels along the Y axis.</summary>
    public int NY => LwcGrid_gm3.GetLength(1);

    /// <summary>Number of voxels along the vertical Z axis.</summary>
    public int NZ => LwcGrid_gm3.GetLength(2);

    /// <summary>
    /// Convenience factory: single homogeneous cloud slab.
    /// </summary>
    /// <param name="baseAlt_km">Altitude of cloud base in km.</param>
    /// <param name="thickness_km">Vertical thickness in km.</param>
    /// <param name="lwc_gm3">Liquid water content in g/m³ (typical 0.1–0.3).</param>
    /// <param name="rEff_um">Effective droplet radius in µm (typical 5–15).</param>
    /// <param name="dz_km">Voxel spacing in km.</param>
    public static CloudField HomogeneousSlab(double baseAlt_km, double thickness_km,
        double lwc_gm3 = 0.15, double rEff_um = 10.0, double dz_km = 0.1)
    {
        int nZ = Math.Max(1, (int)Math.Round(thickness_km / dz_km));
        var grid = new float[1, 1, nZ];
        for (int iz = 0; iz < nZ; iz++)
            grid[0, 0, iz] = (float)lwc_gm3;
        return new CloudField
        {
            LwcGrid_gm3             = grid,
            UniformEffectiveRadius_um = rEff_um,
            GridSpacing_km          = dz_km,
            BaseAltitude_km         = baseAlt_km,
        };
    }
}
