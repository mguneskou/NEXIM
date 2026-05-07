// NEXIM — Single-bounce spectral ray tracer coupling surface BRDF with
//         atmospheric radiance contributions.
//
// Rendering equation (single bounce, direct illumination):
//
//   L_sensor(λ) = τ(λ) × f_r(cosI,cosO,...) × E_down(λ) × cosI × vis + L_path(λ)
//
// where:
//   τ         = upward atmospheric transmittance (from AtmosphericRT)
//   f_r       = surface BRDF from assigned material
//   E_down    = total downwelling irradiance at surface (direct + diffuse sky)
//   cosI      = cos(solar incidence angle) = dot(L_sun, N)
//   vis       = 1 if sun is visible (no occlusion), 0 if shadowed
//   L_path    = atmospheric path radiance (from AtmosphericRT)
//
// When the view ray misses all geometry, L_sensor = L_path (sky background).

using System.Numerics;
using TinyEmbree;
using NEXIM.Core.Models;

namespace NEXIM.Core.Rendering;

/// <summary>
/// Single-bounce spectral ray tracer that couples surface BRDF reflectance with
/// per-wavelength atmospheric contributions supplied by a pre-computed
/// <see cref="RadianceResult"/>.
/// </summary>
public sealed class RayTracer
{
    readonly SceneManager _scene;

    /// <param name="scene">
    /// Built (post-<c>Build()</c>) scene to trace against. The caller retains
    /// ownership; the <see cref="RayTracer"/> does not dispose it.
    /// </param>
    public RayTracer(SceneManager scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    /// <summary>
    /// Computes the solar direction unit vector in scene space.
    /// Convention: +Z is the zenith (surface normal up). Azimuth is measured
    /// from +X clockwise (North = 0°, East = 90° in standard remote-sensing
    /// convention mapped here to +X = 0°).
    /// </summary>
    /// <param name="sza_deg">Solar zenith angle in degrees (0 = overhead).</param>
    /// <param name="saa_deg">Solar azimuth angle in degrees.</param>
    /// <returns>Unit vector pointing from the surface toward the sun.</returns>
    public static Vector3 ComputeSolarDirection(double sza_deg, double saa_deg)
    {
        double sza = sza_deg * (Math.PI / 180.0);
        double saa = saa_deg * (Math.PI / 180.0);
        float  sinSza = (float)Math.Sin(sza);
        return new Vector3(
            sinSza * (float)Math.Cos(saa),
            sinSza * (float)Math.Sin(saa),
            (float)Math.Cos(sza));
    }

    /// <summary>
    /// Computes the per-wavelength spectral radiance reaching the sensor for a
    /// single view ray using the single-bounce rendering equation.
    /// </summary>
    /// <param name="viewOrigin">
    /// Sensor / camera position in scene space (same units as mesh vertices).
    /// </param>
    /// <param name="viewDirection">
    /// Unit vector from the sensor toward the scene (downward for a nadir sensor).
    /// </param>
    /// <param name="solarDirection">
    /// Unit vector pointing from the surface toward the sun (from
    /// <see cref="ComputeSolarDirection"/>).
    /// </param>
    /// <param name="atmResult">
    /// Pre-computed atmospheric quantities (transmittance, path radiance,
    /// downwelling irradiance) for the current scene geometry.
    /// </param>
    /// <param name="wavelengths">
    /// Wavelength grid in µm that matches <paramref name="atmResult"/>.
    /// </param>
    /// <returns>
    /// Per-wavelength upwelling spectral radiance at the sensor
    /// [W/(m²·sr·µm)], length == <c>wavelengths.Length</c>.
    /// </returns>
    public double[] ComputeSpectralRadiance(
        Vector3         viewOrigin,
        Vector3         viewDirection,
        Vector3         solarDirection,
        RadianceResult  atmResult,
        double[]        wavelengths)
    {
        int      nBands   = wavelengths.Length;
        double[] radiance = new double[nBands];

        Ray ray = new()
        {
            Origin      = viewOrigin,
            Direction   = viewDirection,
            MinDistance = 0.0f,
        };

        var (hit, sceneObj) = _scene.Trace(ray);

        // ─── No geometry hit — pure atmospheric background ────────────────
        if (!hit || sceneObj is null)
        {
            for (int k = 0; k < nBands; k++)
                radiance[k] = atmResult.PathRadiance[k];
            return radiance;
        }

        // ─── Geometry hit — compute BRDF contribution ─────────────────────
        Vector3 n = hit.Normal;                              // outward face normal
        Vector3 v = -Vector3.Normalize(viewDirection);      // toward sensor
        Vector3 l = Vector3.Normalize(solarDirection);      // toward sun

        double cosI       = Math.Max(0.0, Vector3.Dot(l, n));   // cos incidence
        double cosO       = Math.Max(0.0, Vector3.Dot(v, n));   // cos emission
        double deltaPhi   = ComputeCosDeltaPhi(l, v, n);

        // Shadow test: is the hit point directly illuminated?
        bool inShadow = _scene.IsOccluded(hit, l);
        double visibility = inShadow ? 0.0 : 1.0;

        for (int k = 0; k < nBands; k++)
        {
            IBrdf  brdf    = BrdfFactory.Create(sceneObj.Material, k);
            double fr      = brdf.Evaluate(cosI, cosO, deltaPhi, wavelengths[k]);
            double eDown   = atmResult.DownwellingIrradiance[k];

            // Single-bounce rendering equation
            double lSurface = fr * eDown * cosI * visibility;
            radiance[k]     = atmResult.Transmittance[k] * lSurface
                            + atmResult.PathRadiance[k];
        }

        return radiance;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes cos(Δφ) — the cosine of the azimuthal angle between the
    /// projections of L and V onto the surface tangent plane.
    /// </summary>
    static double ComputeCosDeltaPhi(Vector3 l, Vector3 v, Vector3 n)
    {
        Vector3 lTan = l - Vector3.Dot(l, n) * n;
        Vector3 vTan = v - Vector3.Dot(v, n) * n;
        float   lLen = lTan.Length();
        float   vLen = vTan.Length();
        if (lLen < 1e-7f || vLen < 1e-7f) return 1.0;
        return Vector3.Dot(lTan / lLen, vTan / vLen);
    }
}
