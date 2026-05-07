// NEXIM — Unit tests for Phase 8: Monte Carlo atmospheric RT.
// Tests cover:
//   1. MieCalculator: Rayleigh limit (x→0), geometric limit (x≫1)
//   2. CloudModel: extinction coefficient plausibility
//   3. AtmosphericVolume: grid build, voxel lookup
//   4. PhotonTracer: energy conservation, escape direction
//   5. MonteCarloSolver (CPU): non-negative radiance, higher albedo → more upwelling
//   6. FullPhysicsAtmosphericRT: IAtmosphericRT interface contract
//
// Run: dotnet test NEXIM.slnx

using NEXIM.Core.Atmospheric;
using NEXIM.Core.Atmospheric.MonteCarlo;
using NEXIM.Core.Models;
using Xunit;

namespace NEXIM.Tests.Atmospheric;

public sealed class MonteCarloTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static AtmosphericInput MakeInput(double rho = 0.3, int nBands = 3)
    {
        var wl = new double[nBands];
        for (int i = 0; i < nBands; i++)
            wl[i] = 0.5 + i * 0.1;  // 0.5, 0.6, 0.7 µm
        return new AtmosphericInput
        {
            Grid              = new WavelengthGrid(wl),
            Profile           = AtmosphericProfile.FromStandard(StandardAtmosphere.USStandard),
            Geometry          = new ViewGeometry { SolarZenith_deg = 30.0 },
            Aerosol           = new AerosolParameters { Aod550 = 0.1 },
            SurfaceReflectance = [rho],
        };
    }

    // ── MieCalculator tests ────────────────────────────────────────────────────

    [Fact]
    public void Mie_SmallParticle_RayleighLimit_QextProportionalX4()
    {
        // For x ≪ 1, Q_ext ∝ x⁴ (Rayleigh limit)
        var r1 = MieCalculator.Compute(0.01, 1.5, 0.0);
        var r2 = MieCalculator.Compute(0.02, 1.5, 0.0);
        double ratio = r2.Qext / r1.Qext;
        // Expect (0.02/0.01)⁴ = 16; allow broad tolerance for Mie oscillations
        Assert.True(ratio > 10.0 && ratio < 25.0,
            $"Rayleigh limit: expected Qext ratio ≈16, got {ratio:F2}");
    }

    [Fact]
    public void Mie_LargeParticle_GeometricLimit_QextNearTwo()
    {
        // For x ≫ 1 and weakly absorbing spheres, Q_ext → 2
        var result = MieCalculator.ComputeForWater(radius_um: 20.0, wavelength_um: 0.5);
        Assert.True(result.Qext > 1.5 && result.Qext < 3.5,
            $"Geometric limit Q_ext should be ~2, got {result.Qext:F3}");
    }

    [Fact]
    public void Mie_CloudDroplet_AsymmetryG_IsStronglyForward()
    {
        var result = MieCalculator.ComputeForWater(radius_um: 10.0, wavelength_um: 0.55);
        Assert.True(result.AsymmetryG > 0.7 && result.AsymmetryG < 1.0,
            $"Cloud droplet g should be in [0.7, 1.0], got {result.AsymmetryG:F3}");
    }

    [Fact]
    public void Mie_Qsca_LessThanOrEqualQext()
    {
        var result = MieCalculator.ComputeForWater(radius_um: 10.0, wavelength_um: 0.55);
        Assert.True(result.Qsca <= result.Qext + 1e-6,
            $"Q_sca ({result.Qsca:F4}) must not exceed Q_ext ({result.Qext:F4})");
    }

    // ── CloudModel tests ───────────────────────────────────────────────────────

    [Fact]
    public void CloudModel_ExtinctionCoeff_PlausibleMagnitude()
    {
        // σ_ext ≈ 1500 × LWC / r_eff = 1500 × 0.1 / 10 = 15 km⁻¹ (Qext ≈ 2)
        var    model = new CloudModel(wavelength_um: 0.55);
        double ext   = model.ExtinctionCoeff_km1(lwc_gm3: 0.1, rEff_um: 10.0);
        Assert.True(ext > 5.0 && ext < 40.0,
            $"Cloud extinction should be ~15 km⁻¹, got {ext:F2}");
    }

    [Fact]
    public void CloudModel_Ssa_NearUnityAtVisible()
    {
        var model = new CloudModel(wavelength_um: 0.55);
        double ssa = model.Ssa(rEff_um: 10.0);
        Assert.True(ssa > 0.98, $"Cloud SSA at 0.55 µm should be > 0.98, got {ssa:F4}");
    }

    // ── AtmosphericVolume tests ────────────────────────────────────────────────

    [Fact]
    public void AtmosphericVolume_Build_ClearSky_PositiveExtinction()
    {
        var layers = StandardAtmosphereLayers.USStandard18Levels;
        var vol    = AtmosphericVolume.Build(layers, null, 0.55,
                         new AerosolParameters { Aod550 = 0.1 });

        Assert.Equal(1, vol.NX);
        Assert.Equal(1, vol.NY);
        Assert.True(vol.NZ > 10, "Should have many vertical levels");

        var (ext, ssa, g) = vol.GetVoxel(0, 0, 0.1);
        Assert.True(ext > 0f, $"Surface extinction should be > 0, got {ext}");
        Assert.True(ssa >= 0f && ssa <= 1f, $"SSA out of range: {ssa}");
    }

    [Fact]
    public void AtmosphericVolume_Build_WithCloud_HigherExtinction()
    {
        var layers  = StandardAtmosphereLayers.USStandard18Levels;
        var aerosol = new AerosolParameters { Aod550 = 0.05 };

        var volClear = AtmosphericVolume.Build(layers, null, 0.55, aerosol);
        var cloud    = CloudField.HomogeneousSlab(2.0, 0.5, lwc_gm3: 0.2, rEff_um: 10.0);
        var volCloud = AtmosphericVolume.Build(layers, cloud, 0.55, aerosol);

        var (extClear, _, _) = volClear.GetVoxel(0, 0, 2.2);
        var (extCloud, _, _) = volCloud.GetVoxel(0, 0, 2.2);

        Assert.True(extCloud > extClear * 5.0,
            $"Cloud ext ({extCloud:F1}) should be >> clear ({extClear:F4})");
    }

    // ── PhotonTracer tests ─────────────────────────────────────────────────────

    [Fact]
    public void PhotonTracer_HighAlbedo_PhotonsEscapeUpward()
    {
        var layers = StandardAtmosphereLayers.USStandard18Levels;
        var vol    = AtmosphericVolume.Build(layers, null, 0.55,
                         new AerosolParameters { Aod550 = 0.1 });
        var rng = new Random(42);
        int escaped = 0;

        for (int i = 0; i < 1000; i++)
        {
            var photon = PhotonPacket.FromSolar(vol.ToaAltitude_km, 0.55, 30.0, 0.0);
            double w   = PhotonTracer.Trace(ref photon, vol, 0.5, rng);
            if (w > 0) escaped++;
        }

        Assert.True(escaped > 50, $"Expected > 50 escaping photons out of 1000, got {escaped}");
    }

    [Fact]
    public void PhotonTracer_ZeroAlbedo_FewerEscapesThanBrightSurface()
    {
        // With zero surface albedo, photons can still escape via Rayleigh backscatter
        // but far fewer than with a reflective surface (rho=0.5).
        var layers = StandardAtmosphereLayers.USStandard18Levels;
        var vol    = AtmosphericVolume.Build(layers, null, 0.55,
                         new AerosolParameters { Aod550 = 0.01 });

        var rng1 = new Random(99); int escapedDark   = 0;
        var rng2 = new Random(99); int escapedBright = 0;

        for (int i = 0; i < 500; i++)
        {
            var p1 = PhotonPacket.FromSolar(vol.ToaAltitude_km, 0.55, 60.0, 0.0);
            if (PhotonTracer.Trace(ref p1, vol, 0.0, rng1) > 0) escapedDark++;

            var p2 = PhotonPacket.FromSolar(vol.ToaAltitude_km, 0.55, 60.0, 0.0);
            if (PhotonTracer.Trace(ref p2, vol, 0.5, rng2) > 0) escapedBright++;
        }

        Assert.True(escapedBright > escapedDark,
            $"Bright surface ({escapedBright}) should yield more upwelling than dark ({escapedDark})");
    }

    // ── MonteCarloSolver (CPU) tests ───────────────────────────────────────────

    [Fact]
    public void MonteCarloSolver_NonNegativeRadiance()
    {
        var input  = MakeInput(rho: 0.3, nBands: 2);
        var layers = StandardAtmosphereLayers.USStandard18Levels;
        var solver = new MonteCarloSolver { PhotonCount = 5_000 };
        var (upwell, _, transm, _) = solver.Solve(input, layers);

        for (int i = 0; i < upwell.Length; i++)
        {
            Assert.True(upwell[i] >= 0,      $"Band {i}: upwelling must be >= 0");
            Assert.True(transm[i] >= 0,      $"Band {i}: transmittance must be >= 0");
            Assert.True(transm[i] <= 1 + 1e-6, $"Band {i}: transmittance must be <= 1");
        }
    }

    [Fact]
    public void MonteCarloSolver_BrighterSurface_MoreUpwelling()
    {
        var layers = StandardAtmosphereLayers.USStandard18Levels;
        var solver = new MonteCarloSolver { PhotonCount = 10_000 };

        var (upDark,   _, _, _) = solver.Solve(MakeInput(rho: 0.0), layers);
        var (upBright, _, _, _) = solver.Solve(MakeInput(rho: 0.5), layers);

        for (int i = 0; i < upDark.Length; i++)
            Assert.True(upBright[i] >= upDark[i] - 1e-10,
                $"Band {i}: brighter surface must produce >= upwelling radiance");
    }

    // ── FullPhysicsAtmosphericRT (IAtmosphericRT contract) ────────────────────

    [Fact]
    public void FullPhysicsRT_ModeName_ContainsFullPhysics()
    {
        var rt = new FullPhysicsAtmosphericRT(photonCount: 100, preferGpu: false);
        Assert.Contains("FULL-PHYSICS", rt.ModeName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FullPhysicsRT_ComputeAsync_ReturnsValidResult()
    {
        var rt     = new FullPhysicsAtmosphericRT(photonCount: 5_000, preferGpu: false);
        var input  = MakeInput(rho: 0.3, nBands: 3);
        var result = await rt.ComputeAsync(input);

        Assert.Equal(3, result.UpwellingRadiance.Length);
        Assert.Equal(3, result.Transmittance.Length);
        Assert.True(result.ComputeTime_ms > 0);
        Assert.Equal("FULL-PHYSICS (Monte Carlo)", result.ModeName);

        foreach (double v in result.Transmittance)
            Assert.True(v >= 0 && v <= 1 + 1e-6, $"Transmittance {v:F4} out of [0,1]");
    }

    [Fact]
    public async Task FullPhysicsRT_CancellationToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var rt    = new FullPhysicsAtmosphericRT(photonCount: 100_000, preferGpu: false);
        var input = MakeInput(nBands: 10);

        // TaskCanceledException derives from OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rt.ComputeAsync(input, cts.Token));
    }

    [Fact]
    public void FullPhysicsRT_EstimateComputeTime_ScalesWithPhotonCount()
    {
        var input = MakeInput(nBands: 5);
        var rt1   = new FullPhysicsAtmosphericRT(photonCount: 10_000,  preferGpu: false);
        var rt2   = new FullPhysicsAtmosphericRT(photonCount: 100_000, preferGpu: false);

        double t1 = rt1.EstimateComputeTime_ms(input);
        double t2 = rt2.EstimateComputeTime_ms(input);
        Assert.True(t2 > t1, "Larger photon count should give larger time estimate");
    }
}
