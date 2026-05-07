// NEXIM — GPU-accelerated Monte Carlo atmospheric RT solver.
// Uploads the voxel grid to the GPU accelerator, launches the photon-tracing
// kernel, and downloads the accumulated radiance.
//
// Falls back gracefully to the ILGPU CPU accelerator when no GPU is present;
// in that case ILGPU compiles the same kernel to multithreaded SIMD code.
//
// Reference:
//   ILGPU docs — https://ilgpu.net/docs/02-kernels/

using ILGPU;
using ILGPU.Runtime;
using NEXIM.Core.Models;

namespace NEXIM.Core.Atmospheric.MonteCarlo.Gpu;

/// <summary>
/// GPU-parallel Monte Carlo solver.  Wraps the ILGPU context and kernel
/// compilation; designed to be re-used across multiple wavelength bands.
/// </summary>
public sealed class GpuMonteCarloSolver : IDisposable
{
    private const double PlanckC1 = 1.1910429e-16;
    private const double PlanckC2 = 0.014387769;

    private readonly IlgpuContext _ctx;
    private readonly Action<
        Index1D,
        ArrayView1D<GpuVoxel, Stride1D.Dense>,
        ArrayView1D<float,    Stride1D.Dense>,
        GpuAtmParams> _kernel;

    private bool _disposed;

    /// <summary>Number of photons per wavelength band (GPU threads).</summary>
    public int PhotonCount { get; init; } = 1_000_000;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Initialise the GPU context and compile the tracing kernel.
    /// </summary>
    /// <param name="preferGpu">When <c>true</c>, attempt to use a hardware GPU.</param>
    public GpuMonteCarloSolver(bool preferGpu = true)
    {
        _ctx    = IlgpuContext.Create(preferGpu);
        _kernel = _ctx.Accelerator.LoadAutoGroupedStreamKernel<
            Index1D,
            ArrayView1D<GpuVoxel, Stride1D.Dense>,
            ArrayView1D<float,    Stride1D.Dense>,
            GpuAtmParams>(GpuPhotonTracer.TraceKernel);
    }

    // ── Solver entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Run the GPU Monte Carlo simulation for all wavelengths in the input.
    /// </summary>
    public (double[] upwelling, double[] pathRadiance,
            double[] transmittance, double[] downwelling)
        Solve(AtmosphericInput input, AtmosphericLayer[] layers,
              CancellationToken ct = default)
    {
        int nBands = input.Grid.Count;
        var upwell  = new double[nBands];
        var pathRad = new double[nBands];
        var transm  = new double[nBands];
        var downE   = new double[nBands];

        double szaDeg = input.Geometry.SolarZenith_deg;
        double saaDeg = input.Geometry.SolarAzimuth_deg;

        for (int ib = 0; ib < nBands; ib++)
        {
            ct.ThrowIfCancellationRequested();

            double lambda  = input.Grid.Wavelengths_um[ib];
            double rhoLamb = GetSurfaceRefl(input.SurfaceReflectance, ib);
            double mu0     = Math.Cos(szaDeg * Math.PI / 180.0);
            double F0      = SolarIrradiance(lambda) * mu0;

            var volume = AtmosphericVolume.Build(layers, input.CloudField, lambda, input.Aerosol);

            // ── Upload voxels ─────────────────────────────────────────────
            GpuVoxel[] voxelArr = volume.ToGpuArray();
            using var voxelBuf  = _ctx.Accelerator.Allocate1D<GpuVoxel>(voxelArr.Length);
            voxelBuf.CopyFromCPU(voxelArr);

            // ── Output accumulator ────────────────────────────────────────
            using var outBuf = _ctx.Accelerator.Allocate1D<float>(1);
            outBuf.MemSetToZero();

            // ── Kernel parameters ─────────────────────────────────────────
            var par = new GpuAtmParams
            {
                NX                 = volume.NX,
                NY                 = volume.NY,
                NZ                 = volume.NZ,
                Dz_km              = (float)volume.GridSpacing_km,
                ToaAltitude_km     = (float)volume.ToaAltitude_km,
                Sza_rad            = (float)(szaDeg * Math.PI / 180.0),
                Saa_rad            = (float)(saaDeg * Math.PI / 180.0),
                SurfaceReflectance = (float)rhoLamb,
                MaxScatters        = 500,
            };

            // ── Launch kernel ─────────────────────────────────────────────
            _kernel(PhotonCount, voxelBuf.View, outBuf.View, par);
            _ctx.Accelerator.Synchronize();

            // ── Download result ───────────────────────────────────────────
            float[] hostOut = outBuf.GetAsArray1D();
            double meanWeight = (PhotonCount > 0) ? hostOut[0] / PhotonCount : 0.0;

            upwell[ib]  = F0 * meanWeight / Math.PI;
            transm[ib]  = Math.Clamp(meanWeight, 0.0, 1.0);
            pathRad[ib] = Math.Max(0, upwell[ib]
                          - F0 * rhoLamb * mu0 / Math.PI * transm[ib]);
            downE[ib]   = F0;
        }

        return (upwell, pathRad, transm, downE);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double GetSurfaceRefl(double[] rho, int band)
    {
        if (rho.Length == 1) return rho[0];
        return band < rho.Length ? rho[band] : rho[^1];
    }

    private static double SolarIrradiance(double lambda_um)
    {
        const double T  = 5778.0;
        const double Rs = 6.957e8;
        const double AU = 1.496e11;
        double omega = Math.PI * (Rs / AU) * (Rs / AU);
        double lam_m = lambda_um * 1e-6;
        double B     = PlanckC1 / (lam_m * lam_m * lam_m * lam_m * lam_m)
                     / (Math.Exp(PlanckC2 / (lam_m * T)) - 1.0);
        return B * omega * 1e-6;   // W m⁻² µm⁻¹
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ctx.Dispose();
    }
}
