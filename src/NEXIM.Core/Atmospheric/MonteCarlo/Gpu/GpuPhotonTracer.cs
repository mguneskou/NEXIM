// NEXIM — ILGPU kernel for GPU-parallel photon tracing.
//
// One GPU thread traces one photon.  All per-thread state lives in registers
// (no heap allocation inside the kernel).  Random numbers are generated with
// a simple XorShift64 PRNG seeded from the thread index.
//
// Atomic accumulation of radiance uses ILGPU's Atomic.Add for float.
//
// Kernel parameters are passed as plain value-type arguments (blittable structs
// or ILGPU ArrayView types) as required by the ILGPU kernel contract.
//
// Reference:
//   Mayer (2009) EPJ Web of Conferences 1:75 — MC algorithm
//   ILGPU docs — kernel constraints (https://ilgpu.net/docs/02-kernels/)

using ILGPU;
using ILGPU.Runtime;

namespace NEXIM.Core.Atmospheric.MonteCarlo.Gpu;

// ── Blittable parameter structs ───────────────────────────────────────────────

/// <summary>
/// Atmospheric geometry and scene parameters passed as a single struct to the GPU kernel.
/// Must be an unmanaged (blittable) value type.
/// </summary>
public struct GpuAtmParams
{
    /// <summary>Grid dimensions.</summary>
    public int NX, NY, NZ;

    /// <summary>Voxel edge length in km.</summary>
    public float Dz_km;

    /// <summary>TOA altitude in km.</summary>
    public float ToaAltitude_km;

    /// <summary>Solar zenith angle in radians.</summary>
    public float Sza_rad;

    /// <summary>Solar azimuth angle in radians.</summary>
    public float Saa_rad;

    /// <summary>Lambertian surface reflectance [0, 1].</summary>
    public float SurfaceReflectance;

    /// <summary>Maximum number of scattering events per photon.</summary>
    public int MaxScatters;
}

/// <summary>
/// ILGPU kernel entry point for GPU-parallel photon tracing.
/// All methods are static; no heap allocation is performed inside the kernel.
/// </summary>
public static class GpuPhotonTracer
{
    // ── Kernel entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// GPU kernel: each thread traces one photon and accumulates its
    /// upwelling weight contribution into <paramref name="outputRadiance"/>[0].
    /// </summary>
    /// <param name="index">ILGPU thread index (= photon index).</param>
    /// <param name="voxels">Flattened voxel grid (X-major, Z-minor layout).</param>
    /// <param name="outputRadiance">
    /// Single-element accumulator.  Photon weight is added atomically.
    /// Divide by total photon count on the host after kernel completion.
    /// </param>
    /// <param name="par">Atmospheric geometry parameters.</param>
    public static void TraceKernel(
        Index1D                                       index,
        ArrayView1D<GpuVoxel, Stride1D.Dense>         voxels,
        ArrayView1D<float,    Stride1D.Dense>         outputRadiance,
        GpuAtmParams                                  par)
    {
        // ── Per-thread XorShift64 RNG ─────────────────────────────────────
        ulong seed = (ulong)index.X * 6364136223846793005UL + 1442695040888963407UL;
        if (seed == 0UL) seed = 1UL;

        // ── Initialise photon at TOA along solar beam ─────────────────────
        float sinSza  = XorShift64Sin(par.Sza_rad);
        float cosSza  = XorShift64Cos(par.Sza_rad);
        float u = sinSza * XorShift64Cos(par.Saa_rad);
        float v = sinSza * XorShift64Sin(par.Saa_rad);
        float w = -cosSza;                             // downward

        float x = 0f, y = 0f, z = par.ToaAltitude_km;
        float weight = 1f;
        int   scatters = 0;
        bool  alive = true;

        float result = 0f;

        while (alive)
        {
            // Sample free path
            float tau  = -MathF.Log(XorShift64Next(ref seed) + 1e-30f);
            float remaining = tau;
            bool  consumed = false;

            for (int step = 0; step < 8_000; step++)
            {
                int ix = (par.NX == 1) ? 0 : Mod(FloatToInt(x / par.Dz_km), par.NX);
                int iy = (par.NY == 1) ? 0 : Mod(FloatToInt(y / par.Dz_km), par.NY);
                int iz = Clamp(FloatToInt(z / par.Dz_km), 0, par.NZ - 1);
                int vi = ix * (par.NY * par.NZ) + iy * par.NZ + iz;

                float sigma = voxels[vi].Extinction_km1;
                if (sigma < 1e-30f) sigma = 1e-30f;

                float tZ = NextCrossing(z, w, par.Dz_km);
                float tX = (par.NX > 1) ? NextCrossing(x, u, par.Dz_km) : 1e20f;
                float tY = (par.NY > 1) ? NextCrossing(y, v, par.Dz_km) : 1e20f;
                float t  = MinOf3(tZ, tX, tY) + 1e-9f;
                float tauAvail = sigma * t;

                if (remaining <= tauAvail)
                {
                    float dl = remaining / sigma;
                    x += dl * u; y += dl * v; z += dl * w;
                    consumed = true;
                    break;
                }

                remaining -= tauAvail;
                x += t * u; y += t * v; z += t * w;

                // Boundary check
                if (z >= par.ToaAltitude_km)
                {
                    if (w > 0f) { result = weight; alive = false; break; }
                    z = par.ToaAltitude_km - 1e-6f; w = -w;
                    continue;
                }
                if (z <= 0f)
                {
                    z = 1e-9f;
                    weight *= par.SurfaceReflectance;
                    if (weight < 1e-30f) { alive = false; break; }
                    // Lambertian reflection
                    float cosT2 = MathF.Sqrt(XorShift64Next(ref seed));
                    float sinT2 = MathF.Sqrt(MathF.Max(0f, 1f - cosT2 * cosT2));
                    float phi2  = 2f * MathF.PI * XorShift64Next(ref seed);
                    u = sinT2 * MathF.Cos(phi2); v = sinT2 * MathF.Sin(phi2); w = cosT2;
                    scatters++;
                    remaining = -MathF.Log(XorShift64Next(ref seed) + 1e-30f);
                    consumed = false;
                    continue;
                }
            }

            if (!alive) break;
            if (!consumed) { alive = false; break; }

            // Scatter
            {
                int ix2 = (par.NX == 1) ? 0 : Mod(FloatToInt(x / par.Dz_km), par.NX);
                int iy2 = (par.NY == 1) ? 0 : Mod(FloatToInt(y / par.Dz_km), par.NY);
                int iz2 = Clamp(FloatToInt(z / par.Dz_km), 0, par.NZ - 1);
                int vi2 = ix2 * (par.NY * par.NZ) + iy2 * par.NZ + iz2;

                float ssa = voxels[vi2].Ssa;
                float g   = voxels[vi2].G;
                weight *= ssa;

                // Russian roulette
                if (weight < 0.001f)
                {
                    if (XorShift64Next(ref seed) > 0.1f) { alive = false; break; }
                    weight /= 0.1f;
                }

                // HG scatter direction
                (u, v, w) = ScatterHG(g, u, v, w, ref seed);
                scatters++;
                if (scatters >= par.MaxScatters) { alive = false; break; }
            }
        }

        // Atomic accumulation
        if (result > 0f)
            Atomic.Add(ref outputRadiance[0], result);
    }

    // ── PRNG (XorShift64 — no heap allocation) ────────────────────────────────

    private static float XorShift64Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        // Map to (0, 1)
        return (state >> 11) * (1f / (float)(ulong.MaxValue >> 11));
    }

    // ── Trig helpers (avoid calling into System.Math in kernel) ───────────────

    private static float XorShift64Sin(float x)  => MathF.Sin(x);
    private static float XorShift64Cos(float x)  => MathF.Cos(x);

    // ── HG phase function sampling ────────────────────────────────────────────

    private static (float u, float v, float w) ScatterHG(
        float g, float u0, float v0, float w0, ref ulong seed)
    {
        float cosTheta;
        if (MathF.Abs(g) < 1e-5f)
        {
            cosTheta = 2f * XorShift64Next(ref seed) - 1f;
        }
        else
        {
            float xi  = XorShift64Next(ref seed);
            float tmp = (1f - g * g) / (1f - g + 2f * g * xi);
            cosTheta  = (1f + g * g - tmp * tmp) / (2f * g);
            cosTheta  = MathF.Min(1f, MathF.Max(-1f, cosTheta));
        }

        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi      = 2f * MathF.PI * XorShift64Next(ref seed);
        float cp       = MathF.Cos(phi);
        float sp       = MathF.Sin(phi);

        if (MathF.Abs(w0) > 0.9999f)
        {
            float s = w0 >= 0f ? 1f : -1f;
            return (sinTheta * cp, sinTheta * sp, s * cosTheta);
        }

        float sinInc = MathF.Sqrt(1f - w0 * w0);
        float f      = sinTheta / sinInc;
        float u1 = u0 * cosTheta + f * (u0 * w0 * cp - v0 * sp);
        float v1 = v0 * cosTheta + f * (v0 * w0 * cp + u0 * sp);
        float w1 = w0 * cosTheta - f * sinInc * sinInc * cp;

        float norm = MathF.Sqrt(u1 * u1 + v1 * v1 + w1 * w1);
        if (norm > 1e-10f) { u1 /= norm; v1 /= norm; w1 /= norm; }
        return (u1, v1, w1);
    }

    // ── Utility (integer arithmetic, GPU-safe) ────────────────────────────────

    private static float NextCrossing(float pos, float dir, float dz)
    {
        if (dir > 1e-15f)  return ((MathF.Floor(pos / dz) + 1f) * dz - pos) / dir;
        if (dir < -1e-15f) return (MathF.Floor(pos / dz) * dz - pos) / dir;
        return 1e20f;
    }

    private static int Clamp(int v, int lo, int hi)
        => v < lo ? lo : (v > hi ? hi : v);

    private static int Mod(int v, int m)
    {
        int r = v % m;
        return r < 0 ? r + m : r;
    }

    private static int FloatToInt(float v) => (int)v;

    private static float MinOf3(float a, float b, float c)
        => a < b ? (a < c ? a : c) : (b < c ? b : c);
}
