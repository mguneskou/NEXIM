// NEXIM — Mode 3 FULL-PHYSICS atmospheric radiative transfer entry point.
// Implements IAtmosphericRT using a Monte Carlo photon tracer (Mayer 2009).
//
// Architecture:
//   • If a GPU is available (ILGPU CUDA/OpenCL), uses GpuMonteCarloSolver.
//   • Otherwise falls back to the CPU-parallel MonteCarloSolver.
//   • Cloud fields (3D LWC grids) are fully supported via AtmosphericVolume.
//
// Accuracy: ±0.1–0.5%, limited by photon count statistics.
// Typical compute time: seconds to minutes depending on N and GPU availability.
//
// Academic references:
//   Mayer (2009) EPJ Web of Conferences 1:75 — MC radiative transfer review
//   Marshak & Davis (2005) "3D Radiative Transfer in Cloudy Atmospheres" — DDA
//   Bohren & Huffman (1983) — Mie theory for cloud droplets
//   Anderson et al. (1986) AFGL-TR-86-0110 — standard atmosphere profiles

using System.Diagnostics;
using NEXIM.Core.Atmospheric.MonteCarlo.Gpu;
using NEXIM.Core.Interfaces;
using NEXIM.Core.Models;

namespace NEXIM.Core.Atmospheric.MonteCarlo;

/// <summary>
/// Mode 3 FULL-PHYSICS atmospheric RT via Monte Carlo photon tracing.
///
/// Supports arbitrary 3D cloud fields, correct Mie scattering for liquid water
/// droplets, Rayleigh scattering, and aerosol extinction.  Lambertian surface
/// boundary condition.
///
/// The GPU path uses an ILGPU kernel (one thread per photon) and is selected
/// automatically when a compatible CUDA or OpenCL device is present.
/// </summary>
public sealed class FullPhysicsAtmosphericRT : IAtmosphericRT
{
    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly int    _photonCount;
    private readonly bool   _preferGpu;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Create a Mode 3 solver.
    /// </summary>
    /// <param name="photonCount">
    /// Number of photons per wavelength band.  Default 100 000 (≈0.3% uncertainty).
    /// Use 1 000 000 for publication-quality results.
    /// </param>
    /// <param name="preferGpu">
    /// When <c>true</c> (default), attempt to use an ILGPU GPU accelerator.
    /// Falls back to CPU multi-threading when no GPU is available.
    /// </param>
    public FullPhysicsAtmosphericRT(int photonCount = 100_000, bool preferGpu = true)
    {
        _photonCount = photonCount;
        _preferGpu   = preferGpu;
    }

    // ── IAtmosphericRT ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ModeName => "FULL-PHYSICS (Monte Carlo)";

    /// <inheritdoc/>
    public async Task<RadianceResult> ComputeAsync(
        AtmosphericInput input,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var (upwell, pathRad, transm, downE) =
            await Task.Run(() => ComputeCore(input, cancellationToken), cancellationToken);
        sw.Stop();

        return new RadianceResult
        {
            UpwellingRadiance   = upwell,
            PathRadiance        = pathRad,
            Transmittance       = transm,
            DownwellingIrradiance = downE,
            Grid                = input.Grid,
            ComputeTime_ms      = sw.Elapsed.TotalMilliseconds,
            ModeName            = ModeName,
        };
    }

    /// <inheritdoc/>
    public double EstimateComputeTime_ms(AtmosphericInput input)
    {
        // Empirical: ~0.05 ms per photon on CPU (8-core); GPU ~10× faster.
        double msPerPhoton = _preferGpu ? 0.005 : 0.05;
        return msPerPhoton * _photonCount * input.Grid.Count;
    }

    // ── Core solver ───────────────────────────────────────────────────────────

    private (double[] upwelling, double[] pathRadiance,
             double[] transmittance, double[] downwelling)
        ComputeCore(AtmosphericInput input, CancellationToken ct)
    {
        var layers = GetAtmosphericLayers(input.Profile);

        // ── Try GPU path ──────────────────────────────────────────────────
        if (_preferGpu)
        {
            try
            {
                using var gpu = new GpuMonteCarloSolver(preferGpu: true)
                {
                    PhotonCount = _photonCount,
                };
                if (gpu != null)
                    return gpu.Solve(input, layers, ct);
            }
            catch
            {
                // GPU unavailable or kernel compilation failed — fall through to CPU.
            }
        }

        // ── CPU fallback ──────────────────────────────────────────────────
        var cpu = new MonteCarloSolver { PhotonCount = _photonCount };
        return cpu.Solve(input, layers, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AtmosphericLayer[] GetAtmosphericLayers(AtmosphericProfile profile)
    {
        if (!profile.IsStandard && profile.CustomLayers != null)
            return profile.CustomLayers;
        return StandardAtmosphereLayers.USStandard18Levels;
    }
}
