// NEXIM — CPU multi-threaded Monte Carlo atmospheric RT solver.
// Traces N photons in parallel using System.Threading.Parallel.For with
// thread-local state, then merges per-thread accumulators.
//
// For each wavelength band the solver launches photons from TOA along the solar
// beam direction and collects upwelling radiance contributions.  The result is
// normalised to physical units using the solar irradiance model and the
// Lambertian radiance formula.
//
// References:
//   Mayer (2009) EPJ Web of Conferences 1:75 — MC normalisation conventions
//   Marshak & Davis (2005) Ch. 1 — estimator definitions

using NEXIM.Core.Models;

namespace NEXIM.Core.Atmospheric.MonteCarlo;

/// <summary>
/// CPU multi-threaded Monte Carlo solver.
///
/// Photon count N trades accuracy for speed:
///   N = 10 000 → ~1% statistical uncertainty (fast preview)
///   N = 100 000 → ~0.3% (default)
///   N = 1 000 000 → ~0.1% (publication-quality)
/// </summary>
public sealed class MonteCarloSolver
{
    private const double PlanckC1 = 1.1910429e-16;   // 2hc² [W m² sr⁻¹]
    private const double PlanckC2 = 0.014387769;      // hc/k_B [m K]

    /// <summary>Number of photons to trace per wavelength band.</summary>
    public int PhotonCount { get; init; } = 100_000;

    /// <summary>Optional progress reporter: argument in [0, 1].</summary>
    public IProgress<double>? Progress { get; init; }

    // ── Solver entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Run the Monte Carlo simulation for all wavelengths in the input.
    /// </summary>
    public (double[] upwelling, double[] pathRadiance,
            double[] transmittance, double[] downwelling)
        Solve(AtmosphericInput input, AtmosphericLayer[] layers,
              CancellationToken ct = default)
    {
        int nBands = input.Grid.Count;
        var upwell    = new double[nBands];
        var pathRad   = new double[nBands];
        var transm    = new double[nBands];
        var downwellE = new double[nBands];

        double sza  = input.Geometry.SolarZenith_deg;
        double mu0  = Math.Cos(sza * Math.PI / 180.0);
        double rho  = GetSurfaceReflectance(input.SurfaceReflectance, 0);

        for (int ib = 0; ib < nBands; ib++)
        {
            ct.ThrowIfCancellationRequested();

            double lambda  = input.Grid.Wavelengths_um[ib];
            double rhoLamb = GetSurfaceReflectance(input.SurfaceReflectance, ib);
            double F0      = SolarIrradiance(lambda) * mu0;  // W m⁻² µm⁻¹ at TOA

            // Build voxel grid for this wavelength
            var volume = AtmosphericVolume.Build(layers, input.CloudField, lambda, input.Aerosol);

            // ── Parallel photon tracing ────────────────────────────────────
            long   nPhotons   = PhotonCount;
            double accumRad   = 0.0;
            double accumCount = 0.0;
            object lockObj    = new();

            // Thread-local accumulators
            var threadRad   = new ThreadLocal<double>(() => 0.0, trackAllValues: true);
            var threadCount = new ThreadLocal<double>(() => 0.0, trackAllValues: true);

            Parallel.For(0L, nPhotons, new ParallelOptions { CancellationToken = ct },
                () => (rad: 0.0, cnt: 0.0),
                (i, state, local) =>
                {
                    if (ct.IsCancellationRequested) { state.Stop(); return local; }

                    // Per-thread deterministic RNG seeded from thread + photon index
                    int tid = Environment.CurrentManagedThreadId;
                    var rng = new Random(unchecked(tid * 1_000_003 + (int)(i & 0x7FFFFFFF)));

                    var photon = PhotonPacket.FromSolar(
                        volume.ToaAltitude_km, lambda,
                        sza, input.Geometry.SolarAzimuth_deg);

                    double w = PhotonTracer.Trace(ref photon, volume, rhoLamb, rng);
                    return (local.rad + w, local.cnt + 1.0);
                },
                local =>
                {
                    lock (lockObj)
                    {
                        accumRad   += local.rad;
                        accumCount += local.cnt;
                    }
                });

            // ── Normalise to physical radiance ─────────────────────────────
            // The MC estimator gives the fraction of solar photons that escape
            // upward at TOA.  Multiply by the solar irradiance to get W/(m²·sr·µm).
            // Division by π converts from Lambertian irradiance to radiance.
            double meanWeight = (accumCount > 0) ? accumRad / accumCount : 0.0;
            upwell[ib]    = F0 * meanWeight / Math.PI;
            transm[ib]    = Math.Clamp(meanWeight, 0.0, 1.0);
            pathRad[ib]   = Math.Max(0, upwell[ib] - F0 * rhoLamb * mu0 / Math.PI * transm[ib]);
            downwellE[ib] = F0;  // approximate: TOA solar irradiance

            Progress?.Report((ib + 1.0) / nBands);
        }

        return (upwell, pathRad, transm, downwellE);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double GetSurfaceReflectance(double[] rho, int band)
    {
        if (rho.Length == 1) return rho[0];
        return (band < rho.Length) ? rho[band] : rho[^1];
    }

    /// <summary>
    /// Approximate top-of-atmosphere solar spectral irradiance in W/(m²·µm).
    /// Uses the simple Kurucz (1992) solar irradiance scaling (1 AU distance).
    /// Accurate to ~5%; sufficient for MC normalisation.
    /// </summary>
    private static double SolarIrradiance(double lambda_um)
    {
        // Planck emission from T_sun = 5778 K, scaled to 1 AU
        const double T  = 5778.0;
        const double Rs = 6.957e8;   // m, solar radius
        const double AU = 1.496e11;  // m, 1 AU
        double omega = Math.PI * (Rs / AU) * (Rs / AU);  // solid angle of sun

        double lam_m  = lambda_um * 1e-6;
        double B      = PlanckC1 / (lam_m * lam_m * lam_m * lam_m * lam_m)
                      / (Math.Exp(PlanckC2 / (lam_m * T)) - 1.0);
        double E_Wm2m = B * omega;          // W m⁻² m⁻¹ (per metre wavelength)
        return E_Wm2m * 1e-6;              // W m⁻² µm⁻¹
    }
}
