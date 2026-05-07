// NEXIM — 3D atmospheric volume for Monte Carlo photon transport.
// Represents the atmosphere as a 3D voxel grid of extinction coefficients,
// single-scatter albedos, and Henyey-Greenstein asymmetry parameters.
//
// For 1D clear-sky (no cloud): NX = NY = 1, NZ = atmospheric layer count.
// For 3D cloudy scenes: the grid spans the cloud field extent.
//
// References:
//   Marshak & Davis (2005) "3D Radiative Transfer in Cloudy Atmospheres",
//     Springer — standard voxel traversal DDA algorithm (Ch. 2).

using NEXIM.Core.Models;

namespace NEXIM.Core.Atmospheric.MonteCarlo;

/// <summary>
/// Blittable struct holding per-voxel optical properties.
/// Used for both CPU traversal and GPU upload.
/// </summary>
public struct GpuVoxel
{
    /// <summary>Extinction coefficient in km⁻¹.</summary>
    public float Extinction_km1;

    /// <summary>Single-scatter albedo [0, 1].</summary>
    public float Ssa;

    /// <summary>Henyey-Greenstein asymmetry parameter [−1, 1].</summary>
    public float G;
}

/// <summary>
/// 3D voxel grid representing the atmosphere at a single wavelength.
///
/// Coordinate system: origin at (0, 0, 0) km; Z points upward.
/// Voxel (ix, iy, iz) occupies the cuboid
///   [ix·dz, (ix+1)·dz] × [iy·dz, (iy+1)·dz] × [iz·dz, (iz+1)·dz]
///
/// Horizontal boundary conditions: periodic (infinite plane approximation).
/// </summary>
public sealed class AtmosphericVolume
{
    private readonly float[,,] _ext;   // [nX, nY, nZ] extinction km⁻¹
    private readonly float[,,] _ssa;   // [nX, nY, nZ]
    private readonly float[,,] _g;     // [nX, nY, nZ]

    /// <summary>Number of voxels along X (cross-track).</summary>
    public int NX { get; }

    /// <summary>Number of voxels along Y (along-track).</summary>
    public int NY { get; }

    /// <summary>Number of voxels along Z (vertical).</summary>
    public int NZ { get; }

    /// <summary>Uniform voxel edge length in km.</summary>
    public double GridSpacing_km { get; }

    /// <summary>Altitude of the top-of-atmosphere boundary in km.</summary>
    public double ToaAltitude_km { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    private AtmosphericVolume(
        float[,,] ext, float[,,] ssa, float[,,] g,
        double dz_km, double toa_km)
    {
        _ext = ext; _ssa = ssa; _g = g;
        NX = ext.GetLength(0);
        NY = ext.GetLength(1);
        NZ = ext.GetLength(2);
        GridSpacing_km = dz_km;
        ToaAltitude_km = toa_km;
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a voxel grid from a set of atmospheric layers, an optional cloud
    /// field, and aerosol parameters, all at the given wavelength.
    /// </summary>
    public static AtmosphericVolume Build(
        AtmosphericLayer[] layers,
        CloudField?         cloud,
        double              wavelength_um,
        AerosolParameters   aerosol)
    {
        // ── Grid dimensions ───────────────────────────────────────────────────
        double dz  = 0.5;  // 0.5 km vertical resolution (matches standard layers)
        double toa = 0.0;
        foreach (var l in layers)
            if (l.AltitudeTop_km > toa) toa = l.AltitudeTop_km;
        toa = Math.Max(toa, 70.0);      // ensure at least 70 km
        int nZ = (int)Math.Ceiling(toa / dz) + 1;

        int nX = (cloud != null) ? cloud.NX : 1;
        int nY = (cloud != null) ? cloud.NY : 1;

        var ext = new float[nX, nY, nZ];
        var ssa = new float[nX, nY, nZ];
        var g   = new float[nX, nY, nZ];

        // ── Fill clear-sky optical properties ────────────────────────────────
        for (int iz = 0; iz < nZ; iz++)
        {
            double z = iz * dz;
            var layer = FindLayer(layers, z);
            if (layer == null) continue;

            // Rayleigh extinction coefficient [km⁻¹]
            double extRay = RayleighExt_km1(wavelength_um, layer.Pressure_hPa);

            // Aerosol extinction coefficient [km⁻¹]
            // AOD is a column integral; distribute proportional to pressure
            double aodFrac = layer.Pressure_hPa / 1013.25 / 8.5;  // per km (scale-height ~8.5 km)
            double extAer  = aerosol.ScaleAod(wavelength_um) * aodFrac;

            double ssaAer  = aerosol.Ssa550;   // SSA approximated as wavelength-independent
            double gAer    = aerosol.AsymmetryParameter;

            double extTotal = extRay + extAer;
            double ssaVal;
            double gVal;
            if (extTotal < 1e-20)
            {
                ssaVal = 1.0;
                gVal   = 0.0;
            }
            else
            {
                // SSA: Rayleigh is conservative (SSA=1), aerosol may absorb
                ssaVal = (extRay + extAer * ssaAer) / extTotal;
                // g: Rayleigh g=0, aerosol g=gAer
                double sExtRay = extRay;          // Rayleigh scatter = extinction (SSA=1)
                double sExtAer = extAer * ssaAer; // aerosol scatter
                double sExt    = sExtRay + sExtAer;
                gVal = (sExt > 1e-20)
                    ? (sExtRay * 0.0 + sExtAer * gAer) / sExt
                    : 0.0;
            }

            for (int ix = 0; ix < nX; ix++)
            for (int iy = 0; iy < nY; iy++)
            {
                ext[ix, iy, iz] = (float)Math.Max(0, extTotal);
                ssa[ix, iy, iz] = (float)Math.Clamp(ssaVal, 0.0, 1.0);
                g[ix, iy, iz]   = (float)Math.Clamp(gVal,  -1.0, 1.0);
            }
        }

        // ── Add cloud optical properties ──────────────────────────────────────
        if (cloud != null)
        {
            var cloudModel = new CloudModel(wavelength_um);

            for (int icX = 0; icX < cloud.NX; icX++)
            for (int icY = 0; icY < cloud.NY; icY++)
            for (int icZ = 0; icZ < cloud.NZ; icZ++)
            {
                float lwc = cloud.LwcGrid_gm3[icX, icY, icZ];
                if (lwc <= 0f) continue;

                double cloudZ = cloud.BaseAltitude_km + (icZ + 0.5) * cloud.GridSpacing_km;
                int iz = (int)(cloudZ / dz);
                if (iz < 0 || iz >= nZ) continue;

                double rEff = cloud.EffectiveRadiusGrid_um?[icX, icY, icZ]
                              ?? cloud.UniformEffectiveRadius_um;

                double cExt = cloudModel.ExtinctionCoeff_km1(lwc, rEff);
                double cSsa = cloudModel.Ssa(rEff);
                double cG   = cloudModel.AsymmetryG(rEff);

                // Volume-weighted combination with existing clear-sky values
                float  eOld = ext[icX, icY, iz];
                float  eNew = eOld + (float)cExt;
                if (eNew > 1e-30f)
                {
                    g[icX, icY, iz]   = (float)((eOld * g[icX, icY, iz]   + cExt * cG)   / eNew);
                    ssa[icX, icY, iz] = (float)((eOld * ssa[icX, icY, iz] + cExt * cSsa) / eNew);
                    ext[icX, icY, iz] = eNew;
                }
            }
        }

        return new AtmosphericVolume(ext, ssa, g, dz, toa);
    }

    // ── Look-up ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieve optical properties for the voxel containing position (x, y, z).
    /// Horizontal indices wrap periodically; vertical indices are clamped.
    /// </summary>
    public (float ext, float ssa, float g) GetVoxel(double x, double y, double z)
    {
        int ix, iy;
        if (NX == 1)
        {
            ix = 0;
        }
        else
        {
            ix = (int)(x / GridSpacing_km) % NX;
            if (ix < 0) ix += NX;
        }

        if (NY == 1)
        {
            iy = 0;
        }
        else
        {
            iy = (int)(y / GridSpacing_km) % NY;
            if (iy < 0) iy += NY;
        }

        int iz = Math.Clamp((int)(z / GridSpacing_km), 0, NZ - 1);
        return (_ext[ix, iy, iz], _ssa[ix, iy, iz], _g[ix, iy, iz]);
    }

    /// <summary>
    /// Flatten the voxel grid to a 1D array for GPU upload.
    /// Layout: iterate Z innermost, then Y, then X (X-major, Z-minor).
    /// Index = ix*(NY*NZ) + iy*NZ + iz.
    /// </summary>
    public GpuVoxel[] ToGpuArray()
    {
        var arr = new GpuVoxel[NX * NY * NZ];
        for (int ix = 0; ix < NX; ix++)
        for (int iy = 0; iy < NY; iy++)
        for (int iz = 0; iz < NZ; iz++)
        {
            int idx = ix * (NY * NZ) + iy * NZ + iz;
            arr[idx] = new GpuVoxel
            {
                Extinction_km1 = _ext[ix, iy, iz],
                Ssa            = _ssa[ix, iy, iz],
                G              = _g[ix, iy, iz],
            };
        }
        return arr;
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static AtmosphericLayer? FindLayer(AtmosphericLayer[] layers, double z_km)
    {
        foreach (var l in layers)
            if (z_km >= l.AltitudeBase_km && z_km < l.AltitudeTop_km)
                return l;
        // Above all defined layers → use the topmost layer properties
        AtmosphericLayer? top = null;
        foreach (var l in layers)
            if (top == null || l.AltitudeBase_km > top.AltitudeBase_km)
                top = l;
        return top;
    }

    /// <summary>
    /// Rayleigh extinction coefficient at the given pressure and wavelength.
    ///
    /// σ_R [km⁻¹] = (τ_R,total / H_R) × (P / P₀)
    ///   where τ_R,total = 0.00902 × λ⁻⁴·⁰⁸⁴ (Bucholtz 1995 Appl. Opt. 34:2765),
    ///   H_R = 8.5 km (Rayleigh scale height), P₀ = 1013.25 hPa.
    /// </summary>
    private static double RayleighExt_km1(double wavelength_um, double pressure_hPa)
    {
        double tauTotal = 0.00902 * Math.Pow(wavelength_um, -4.084);
        const double H_R = 8.5;  // km
        return tauTotal / H_R * (pressure_hPa / 1013.25);
    }
}
