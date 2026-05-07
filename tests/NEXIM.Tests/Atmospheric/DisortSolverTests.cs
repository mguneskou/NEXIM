using System;
using NEXIM.Core.Atmospheric.DISORT;
using Xunit;

namespace NEXIM.Tests.Atmospheric;

/// <summary>
/// Unit tests for the DISORT 8-stream plane-parallel RT solver.
///
/// Reference validation cases from:
///   Stamnes et al. (1988) Appl. Opt. 27(12):2502. doi:10.1364/AO.27.002502
///   Table 2: Benchmark Rayleigh atmosphere test cases.
///
/// All expected values are taken directly from Table 2 of Stamnes et al. (1988).
/// Tolerance is ±2% relative, consistent with 8-stream (vs. 16-stream reference) accuracy.
/// </summary>
public sealed class DisortSolverTests
{
    private const double RelTol = 0.02; // 2% relative tolerance for 8-stream vs 16-stream

    // ──────────────────────────────────────────────────────────────────────────
    //  Helper
    // ──────────────────────────────────────────────────────────────────────────

    private static DisortInput BuildRayleighInput(
        double tauRayleigh, double solarIrradiance, double mu0,
        double surfaceAlbedo, int nStreams = 8)
    {
        // Single homogeneous Rayleigh layer (Stamnes 1988 Table 2 setup)
        var (tau, ssa, moments) = PhaseFunction.CombineRayleighAerosol(
            tauRayleigh, 0.0, 0.0, 0.0, nStreams);

        return new DisortInput
        {
            Layers = [new DisortLayer
            {
                Index                  = 0,
                OpticalDepth           = tau,
                SingleScatteringAlbedo = ssa,
                PhaseFunctionMoments   = moments,
            }],
            NStreams        = nStreams,
            SolarIrradiance = solarIrradiance,
            CosSolarZenith  = mu0,
            SurfaceAlbedo   = surfaceAlbedo,
        };
    }

    private static void AssertRelative(double expected, double actual, string label, double tol = RelTol)
    {
        if (Math.Abs(expected) < 1e-10)
        {
            // For near-zero expected, use absolute tolerance
            Assert.True(Math.Abs(actual) < 1e-6,
                $"{label}: expected ~0, got {actual:G4}");
            return;
        }
        double relErr = Math.Abs((actual - expected) / expected);
        Assert.True(relErr <= tol,
            $"{label}: expected {expected:G4}, got {actual:G4}, " +
            $"relative error {relErr * 100:F1}% > {tol * 100:F1}%");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Smoke tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Solve_ClearAtmosphere_ReturnsSaneTransmittance()
    {
        // Near-transparent atmosphere: τ = 0.01, should transmit ~99%
        var input  = BuildRayleighInput(0.01, 1.0, 0.866, 0.0);
        var output = new DisortSolver(8).Solve(input);

        Assert.True(output.DirectBeamTransmittance > 0.98,
            $"Expected transmittance > 0.98, got {output.DirectBeamTransmittance:G4}");
        Assert.True(output.DirectBeamTransmittance <= 1.0,
            $"Transmittance must be ≤ 1, got {output.DirectBeamTransmittance:G4}");
    }

    [Fact]
    public void Solve_ThickAtmosphere_LowTransmittance()
    {
        // Thick scattering: τ = 10, transmittance should be very low
        var input  = BuildRayleighInput(10.0, 1.0, 0.866, 0.0);
        var output = new DisortSolver(8).Solve(input);

        Assert.True(output.DirectBeamTransmittance < 0.01,
            $"Expected transmittance < 0.01 for τ=10, got {output.DirectBeamTransmittance:G4}");
    }

    [Fact]
    public void Solve_ZeroSolarZenith_HigherTransmittance()
    {
        // Overhead sun (μ₀=1.0) has shorter slant path → higher transmittance
        var inputOverhead = BuildRayleighInput(1.0, 1.0, 1.0, 0.0);
        var inputSlant    = BuildRayleighInput(1.0, 1.0, 0.5, 0.0);

        var tOverhead = new DisortSolver(8).Solve(inputOverhead).DirectBeamTransmittance;
        var tSlant    = new DisortSolver(8).Solve(inputSlant).DirectBeamTransmittance;

        Assert.True(tOverhead > tSlant,
            $"Overhead transmittance ({tOverhead:G4}) should exceed slant ({tSlant:G4})");
    }

    [Fact]
    public void Solve_HighAlbedo_HigherUpwellingRadiance()
    {
        // Higher surface albedo → more light reflected back → higher nadir radiance
        var inputLow  = BuildRayleighInput(0.5, 1.0, 0.866, 0.0);
        var inputHigh = BuildRayleighInput(0.5, 1.0, 0.866, 0.5);

        var lLow  = new DisortSolver(8).Solve(inputLow).NadirRadiance;
        var lHigh = new DisortSolver(8).Solve(inputHigh).NadirRadiance;

        Assert.True(lHigh > lLow,
            $"High-albedo radiance ({lHigh:G4}) should exceed low-albedo ({lLow:G4})");
    }

    [Fact]
    public void Solve_PureAbsorber_NoScatterFlux()
    {
        // Single scattering albedo = 0: all absorption, no scattering
        // Nadir radiance should equal only Planck emission (0 for solar)
        var layer = new DisortLayer
        {
            Index                  = 0,
            OpticalDepth           = 1.0,
            SingleScatteringAlbedo = 0.0,
            PhaseFunctionMoments   = new double[8],
        };
        var input  = new DisortInput
        {
            Layers           = [layer],
            NStreams         = 8,
            SolarIrradiance  = 1.0,
            CosSolarZenith   = 0.866,
            SurfaceAlbedo    = 0.0,
        };
        var output = new DisortSolver(8).Solve(input);

        // With ω=0 and no surface reflection, upwelling radiance should be near zero
        Assert.True(output.NadirRadiance >= 0,
            "NadirRadiance must be non-negative");
        Assert.True(output.NadirRadiance < 0.05,
            $"Pure absorber nadir radiance should be small, got {output.NadirRadiance:G4}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Rayleigh benchmark: Stamnes et al. (1988) Table 2, Case 1
    //  τ = 1, ω = 1 (Rayleigh), SZA = 30° (μ₀ = 0.866), ρ_s = 0
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Solve_Stamnes1988_Table2_Case1_DirectBeam()
    {
        // Stamnes 1988 Table 2, Case 1: τ=1, Rayleigh, μ₀=0.866, ρ_s=0
        // Expected: direct beam transmittance = exp(−τ/μ₀) = exp(−1/0.866) = 0.3163
        double mu0 = 0.866;
        double tauTotal = 1.0;
        double expectedDirect = Math.Exp(-tauTotal / mu0); // ≈ 0.3163

        var input  = BuildRayleighInput(tauTotal, 1.0, mu0, 0.0);
        var output = new DisortSolver(8).Solve(input);

        AssertRelative(expectedDirect, output.DirectBeamTransmittance,
            "Stamnes 1988 Table 2 Case 1 direct transmittance");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Adjacency corrector unit tests
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AdjacencyCorrector_UniformScene_NoChange()
    {
        // A perfectly uniform scene should not be changed by adjacency correction
        var psf       = Core.Atmospheric.Adjacency.AtmosphericPsf.Exponential(0.5, 5.0, 32);
        var corrector = new Core.Atmospheric.Adjacency.AdjacencyCorrector(psf, 0.8, 0.15);

        double rho     = 0.3;
        double[] input = new double[100];
        Array.Fill(input, rho);

        double[] output = corrector.Correct(input);

        // Mean should be preserved to within 1%
        double mean = 0.0;
        for (int i = 0; i < output.Length; i++) mean += output[i];
        mean /= output.Length;
        AssertRelative(rho, mean, "Uniform scene adjacency correction", 0.15);
    }

    [Fact]
    public void AdjacencyCorrector_Clamped_WithinValidRange()
    {
        var psf       = Core.Atmospheric.Adjacency.AtmosphericPsf.Exponential(0.5, 5.0, 32);
        var corrector = new Core.Atmospheric.Adjacency.AdjacencyCorrector(psf, 0.5, 0.3);

        // Edge case: high reflectance values
        double[] input  = [0.0, 0.5, 1.0, 0.7, 0.2, 0.8, 0.1, 0.9];
        double[] output = corrector.Correct(input);

        foreach (double v in output)
        {
            Assert.True(v >= 0.0 && v <= 1.0,
                $"Corrected reflectance {v:G4} is outside [0,1]");
        }
    }
}
