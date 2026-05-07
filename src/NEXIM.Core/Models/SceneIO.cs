// NEXIM — Scene description and rendering I/O value objects.
// These data-transfer objects cross the rendering pipeline boundaries.

namespace NEXIM.Core.Models;

/// <summary>BRDF model type for a scene material.</summary>
public enum BrdfType
{
    /// <summary>Lambertian (perfectly diffuse). Parameter: Albedo.</summary>
    Lambertian = 0,

    /// <summary>
    /// Oren-Nayar rough diffuse (Oren &amp; Nayar 1994 SIGGRAPH,
    /// doi:10.1145/192161.192213). Parameters: Albedo, Roughness_rad.
    /// </summary>
    OrenNayar = 1,

    /// <summary>
    /// GGX microfacet specular (Walter et al. 2007 EGSR,
    /// doi:10.2312/EGSR/EGSR07/195-206). Parameters: GgxAlpha, GgxF0.
    /// </summary>
    Ggx = 2,

    /// <summary>
    /// Hapke (1981) planetary surface reflectance
    /// (Hapke 1981 J. Geophys. Res. 86(B4):3039, doi:10.1029/JB086iB04p03039).
    /// Parameters: HapkeW, HapkeB, HapkeB0, HapkeHWidth.
    /// </summary>
    Hapke = 3,
}

/// <summary>
/// Surface material definition used to assign BRDF properties to a scene object.
/// All parameters are spectrally flat by default; per-band albedo is supported
/// via <see cref="Albedo"/>.
/// </summary>
public sealed class Material
{
    /// <summary>Unique material identifier (assigned by the caller).</summary>
    public int Id { get; init; }

    /// <summary>BRDF model to apply to this material.</summary>
    public BrdfType BrdfType { get; init; } = BrdfType.Lambertian;

    /// <summary>
    /// Spectral hemispherical albedo [0,1] per wavelength band, or a single
    /// value for a spectrally flat surface. Used by Lambertian and Oren-Nayar.
    /// </summary>
    public required double[] Albedo { get; init; }

    // ─── Oren-Nayar ──────────────────────────────────────────────────────────

    /// <summary>
    /// Surface roughness σ in radians (standard deviation of micro-facet slope
    /// angles). σ = 0 recovers the Lambertian BRDF.
    /// </summary>
    public double Roughness_rad { get; init; } = 0.0;

    // ─── GGX ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// GGX roughness α ∈ [0,1] (0 = perfect mirror, 1 = fully rough diffuse-like).
    /// Typical metals: 0.1–0.4. Plastic / ceramics: 0.2–0.5.
    /// </summary>
    public double GgxAlpha { get; init; } = 0.3;

    /// <summary>
    /// Fresnel reflectance at normal incidence F₀ ∈ [0,1].
    /// Typical dielectric: 0.04. Metals: 0.5–1.0.
    /// </summary>
    public double GgxF0 { get; init; } = 0.04;

    // ─── Hapke ───────────────────────────────────────────────────────────────

    /// <summary>Hapke single-scattering albedo w ∈ [0,1].</summary>
    public double HapkeW { get; init; } = 0.5;

    /// <summary>
    /// Hapke Henyey-Greenstein asymmetry parameter b ∈ [−1,1].
    /// b &gt; 0 → predominantly backward scattering (typical soils); b = 0 → isotropic.
    /// </summary>
    public double HapkeB { get; init; } = 0.0;

    /// <summary>Opposition surge amplitude B₀ ∈ [0,1] (0 = no surge).</summary>
    public double HapkeB0 { get; init; } = 0.0;

    /// <summary>Opposition surge angular half-width h in radians (default ≈ 0.1 rad).</summary>
    public double HapkeHWidth { get; init; } = 0.1;

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the albedo for the given spectral band index.
    /// If <see cref="Albedo"/> is a single-element array, that constant value is
    /// returned for every band.
    /// </summary>
    public double GetAlbedo(int bandIndex)
        => Albedo.Length == 1 ? Albedo[0] : Albedo[bandIndex];
}

/// <summary>Per-pixel spectral radiance result from the ray tracer.</summary>
public sealed class PixelRadiance
{
    /// <summary>
    /// Spectral upwelling radiance at the sensor [W/(m²·sr·µm)], one value
    /// per wavelength band in the input grid.
    /// </summary>
    public required double[] Radiance { get; init; }

    /// <summary>Wavelength grid used for this pixel.</summary>
    public required WavelengthGrid Grid { get; init; }

    /// <summary>
    /// True if the view ray intersected scene geometry; false for
    /// atmosphere-only (sky or background) pixels.
    /// </summary>
    public bool HitSurface { get; init; }
}
