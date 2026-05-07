// NEXIM — Bidirectional Reflectance Distribution Function (BRDF) models.
//
// References:
//   Lambertian: Trivial.
//   Oren-Nayar: M. Oren and S.K. Nayar, "Generalization of Lambert's
//     Reflectance Model", SIGGRAPH 1994, doi:10.1145/192161.192213.
//   GGX: B. Walter et al., "Microfacet Models for Refraction through Rough
//     Surfaces", EGSR 2007, doi:10.2312/EGSR/EGSR07/195-206.
//   Hapke: B. Hapke, "Bidirectional Reflectance Spectroscopy: 1. Theory",
//     J. Geophys. Res. 86(B4):3039–3054, 1981, doi:10.1029/JB086iB04p03039.

using NEXIM.Core.Models;

namespace NEXIM.Core.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Bidirectional Reflectance Distribution Function [sr⁻¹].
/// All angles are defined relative to the surface normal (outward-pointing up).
/// </summary>
public interface IBrdf
{
    /// <summary>
    /// Evaluate the BRDF at the given geometry.
    /// </summary>
    /// <param name="cosThetaI">
    /// cos(incidence angle) = dot(L, N), L = direction from surface toward light,
    /// N = outward surface normal. Must be ≥ 0.
    /// </param>
    /// <param name="cosThetaO">
    /// cos(emission angle) = dot(V, N), V = direction from surface toward sensor.
    /// Must be ≥ 0.
    /// </param>
    /// <param name="cosDeltaPhi">
    /// Cosine of the azimuthal difference between the projected L and V vectors
    /// in the surface tangent plane. Range [−1, 1].
    /// </param>
    /// <param name="wavelength_um">Wavelength in µm (for spectrally varying BRDFs).</param>
    /// <returns>BRDF value in sr⁻¹ ≥ 0.</returns>
    double Evaluate(double cosThetaI, double cosThetaO, double cosDeltaPhi, double wavelength_um);
}

// ─────────────────────────────────────────────────────────────────────────────
// 1. Lambertian (perfectly diffuse)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Perfectly diffuse (Lambertian) BRDF: f_r = ρ / π.
/// Hemispherical reflectance equals the albedo ρ.
/// </summary>
public sealed class LambertianBrdf : IBrdf
{
    readonly double _albedo;

    public LambertianBrdf(double albedo)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(albedo, 0.0, nameof(albedo));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(albedo, 1.0, nameof(albedo));
        _albedo = albedo;
    }

    public double Evaluate(double cosThetaI, double cosThetaO, double cosDeltaPhi, double wavelength_um)
        => _albedo / Math.PI;
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. Oren-Nayar (Oren & Nayar 1994, SIGGRAPH)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Rough diffuse BRDF with a Gaussian distribution of Lambertian micro-facets
/// (Oren &amp; Nayar 1994). Reduces to Lambertian when σ = 0.
/// <para>
/// f_r = (ρ/π) × [A + B × max(0, cos(Δφ)) × sin(α) × tan(β)]
/// where α = max(θᵢ, θₒ), β = min(θᵢ, θₒ).
/// </para>
/// </summary>
public sealed class OrenNayarBrdf : IBrdf
{
    readonly double _albedo;
    readonly double _A;
    readonly double _B;

    /// <param name="albedo">Hemispherical albedo ρ ∈ [0,1].</param>
    /// <param name="sigma_rad">Surface roughness σ in radians (σ = 0 → Lambertian).</param>
    public OrenNayarBrdf(double albedo, double sigma_rad)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(albedo, 0.0, nameof(albedo));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(albedo, 1.0, nameof(albedo));
        _albedo = albedo;
        double sig2 = sigma_rad * sigma_rad;
        _A = 1.0 - 0.5 * sig2 / (sig2 + 0.33);
        _B = 0.45 * sig2 / (sig2 + 0.09);
    }

    public double Evaluate(double cosThetaI, double cosThetaO, double cosDeltaPhi, double wavelength_um)
    {
        double sinI = Math.Sqrt(Math.Max(0.0, 1.0 - cosThetaI * cosThetaI));
        double sinO = Math.Sqrt(Math.Max(0.0, 1.0 - cosThetaO * cosThetaO));

        // α = larger angle (smaller cosine), β = smaller angle (larger cosine)
        double sinAlpha, tanBeta;
        if (cosThetaI < cosThetaO)
        {
            // θᵢ > θₒ → α = θᵢ, β = θₒ
            sinAlpha = sinI;
            tanBeta  = sinO / Math.Max(cosThetaO, 1e-9);
        }
        else
        {
            sinAlpha = sinO;
            tanBeta  = sinI / Math.Max(cosThetaI, 1e-9);
        }

        double maxCos = Math.Max(0.0, cosDeltaPhi);
        return (_albedo / Math.PI) * (_A + _B * maxCos * sinAlpha * tanBeta);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. GGX microfacet specular (Walter et al. 2007)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// GGX (Trowbridge-Reitz) microfacet specular BRDF (Walter et al. 2007).
/// <para>f_r = D(θ_h) × G(θᵢ, θₒ) × F(θ_d) / (4 cosθᵢ cosθₒ)</para>
/// <para>
/// D = GGX normal distribution; G = Smith uncorrelated height-correlated masking;
/// F = Schlick Fresnel approximation.
/// </para>
/// </summary>
public sealed class GgxBrdf : IBrdf
{
    readonly double _alpha2;  // roughness²
    readonly double _f0;      // Fresnel at normal incidence

    /// <param name="alpha">GGX roughness α ∈ [0,1].</param>
    /// <param name="f0">Fresnel reflectance at normal incidence F₀ ∈ [0,1].</param>
    public GgxBrdf(double alpha, double f0)
    {
        _alpha2 = alpha * alpha;
        _f0     = Math.Clamp(f0, 0.0, 1.0);
    }

    // GGX normal distribution (Eq. 1 in Walter 2007)
    static double Distribution(double cosH, double alpha2)
    {
        double cosH2 = cosH * cosH;
        double t     = cosH2 * (alpha2 - 1.0) + 1.0;
        return alpha2 / (Math.PI * t * t);
    }

    // Smith GGX G1 masking term (Eq. 34 in Walter 2007)
    static double G1(double cosV, double alpha2)
    {
        double cosV2 = cosV * cosV;
        double tan2V = (1.0 - cosV2) / Math.Max(cosV2, 1e-14);
        return 2.0 / (1.0 + Math.Sqrt(1.0 + alpha2 * tan2V));
    }

    // Schlick Fresnel approximation
    static double Fresnel(double cosD, double f0)
        => f0 + (1.0 - f0) * Math.Pow(1.0 - Math.Max(0.0, cosD), 5.0);

    public double Evaluate(double cosThetaI, double cosThetaO, double cosDeltaPhi, double wavelength_um)
    {
        if (cosThetaI <= 0.0 || cosThetaO <= 0.0) return 0.0;

        double sinI = Math.Sqrt(Math.Max(0.0, 1.0 - cosThetaI * cosThetaI));
        double sinO = Math.Sqrt(Math.Max(0.0, 1.0 - cosThetaO * cosThetaO));

        // Phase angle between L and V
        double cosG = cosThetaI * cosThetaO + sinI * sinO * cosDeltaPhi;

        // Half-vector angle with surface normal — cosθ_h = (cosI+cosO)/|L+V|
        // |L+V|² = 2(1+cosG)
        double len2 = 2.0 * (1.0 + cosG);
        if (len2 < 1e-14) return 0.0;
        double cosH = (cosThetaI + cosThetaO) / Math.Sqrt(len2);
        cosH = Math.Clamp(cosH, 0.0, 1.0);

        // Angle between incident ray and half-vector — for Fresnel
        // cos(θ_d) = dot(L, H) = (1+cosG)/|L+V| = sqrt((1+cosG)/2)
        double cosLH = Math.Sqrt(Math.Max(0.0, (1.0 + cosG) * 0.5));

        double D = Distribution(cosH, _alpha2);
        double G = G1(cosThetaI, _alpha2) * G1(cosThetaO, _alpha2);
        double F = Fresnel(cosLH, _f0);

        return D * G * F / (4.0 * cosThetaI * cosThetaO);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. Hapke (1981) planetary surface reflectance
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Hapke (1981) bidirectional reflectance model for planetary / soil surfaces.
/// Implements Eq. (36) of Hapke (1981):
/// <para>
/// f_r = (w/4) × [μ₀/(μ₀+μ)] × [(1+B(g))·p(g) + H(μ₀)·H(μ) − 1]
/// </para>
/// <para>
/// where μ₀ = cosθᵢ, μ = cosθₒ, g = phase angle, w = single-scattering albedo,
/// p = Henyey-Greenstein phase function (backward-scatter convention: b &gt; 0
/// → back-scattering), B = opposition surge, H = Chandrasekhar H-function.
/// </para>
/// </summary>
public sealed class HapkeBrdf : IBrdf
{
    readonly double _w;   // single-scattering albedo
    readonly double _b;   // HG asymmetry (b > 0 = backward)
    readonly double _B0;  // opposition surge amplitude
    readonly double _h;   // opposition surge half-width [rad]

    /// <param name="w">Single-scattering albedo ∈ [0,1].</param>
    /// <param name="b">HG asymmetry ∈ [−1,1] (b &gt; 0 = backward).</param>
    /// <param name="B0">Opposition surge amplitude ∈ [0,1].</param>
    /// <param name="h">Opposition surge angular half-width in radians.</param>
    public HapkeBrdf(double w, double b = 0.0, double B0 = 0.0, double h = 0.06)
    {
        _w  = Math.Clamp(w,  0.0, 1.0);
        _b  = Math.Clamp(b, -1.0, 1.0);
        _B0 = Math.Clamp(B0, 0.0, 1.0);
        _h  = Math.Max(h, 1e-9);
    }

    // Chandrasekhar H-function approximation (Hapke 1981, Eq. 8).
    // H(x, w) = (1 + 2x) / (1 + 2x·√(1−w))
    static double H(double x, double w)
    {
        double gamma = Math.Sqrt(Math.Max(0.0, 1.0 - w));
        return (1.0 + 2.0 * x) / (1.0 + 2.0 * gamma * x);
    }

    // Single-lobe Henyey-Greenstein phase function (backward-scatter convention).
    // p(g) = (1−b²) / (1 + 2b·cosG + b²)^(3/2)
    // For b > 0, peak at g = π (backward scatter).
    double P(double cosG)
    {
        double den = 1.0 + 2.0 * _b * cosG + _b * _b;
        if (den < 1e-14) return 0.0;
        return (1.0 - _b * _b) / (den * Math.Sqrt(den));
    }

    // Opposition surge B(g) (Hapke 1981, Eq. 9).
    double Bs(double cosG)
    {
        double g       = Math.Acos(Math.Clamp(cosG, -1.0, 1.0));
        double tanHalf = Math.Tan(g * 0.5);
        return _B0 / (1.0 + tanHalf / _h);
    }

    public double Evaluate(double cosThetaI, double cosThetaO, double cosDeltaPhi, double wavelength_um)
    {
        if (cosThetaI <= 0.0 || cosThetaO <= 0.0) return 0.0;

        double sinI = Math.Sqrt(Math.Max(0.0, 1.0 - cosThetaI * cosThetaI));
        double sinO = Math.Sqrt(Math.Max(0.0, 1.0 - cosThetaO * cosThetaO));
        double cosG = cosThetaI * cosThetaO + sinI * sinO * cosDeltaPhi;

        double mu0 = cosThetaI;
        double mu  = cosThetaO;
        double p   = P(cosG);
        double bs  = Bs(cosG);
        double hI  = H(mu0, _w);
        double hO  = H(mu, _w);

        // Hapke (1981) Eq. (36) — single-scatter + multiple-scatter correction
        return (_w / 4.0) * (mu0 / (mu0 + mu))
               * ((1.0 + bs) * p + hI * hO - 1.0);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Factory
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Creates the correct <see cref="IBrdf"/> for a given material and band.</summary>
public static class BrdfFactory
{
    /// <param name="material">Material definition.</param>
    /// <param name="bandIndex">Spectral band index used to look up the per-band albedo.</param>
    public static IBrdf Create(Material material, int bandIndex)
    {
        double albedo = material.GetAlbedo(bandIndex);
        return material.BrdfType switch
        {
            BrdfType.Lambertian => new LambertianBrdf(albedo),
            BrdfType.OrenNayar  => new OrenNayarBrdf(albedo, material.Roughness_rad),
            BrdfType.Ggx        => new GgxBrdf(material.GgxAlpha, material.GgxF0),
            BrdfType.Hapke      => new HapkeBrdf(material.HapkeW, material.HapkeB,
                                                  material.HapkeB0, material.HapkeHWidth),
            _ => throw new ArgumentOutOfRangeException(nameof(material), material.BrdfType, null),
        };
    }
}
