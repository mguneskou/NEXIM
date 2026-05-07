using System;
using System.Threading;
using System.Threading.Tasks;
using NEXIM.Core.Atmospheric;
using NEXIM.Core.Models;
using Xunit;

namespace NEXIM.Tests.Atmospheric;

/// <summary>
/// Smoke tests for the Mode 2 CKD+DISORT pipeline.
///
/// These tests verify the end-to-end pipeline integration without requiring
/// actual k-table files. When no k-tables are present, AccurateAtmosphericRT
/// falls back to a pure Rayleigh+aerosol DISORT calculation.
///
/// For quantitative accuracy validation against MODTRAN6, see:
///   docs/validation/CameoSim_comparison.md (generated during Phase 15 validation)
///
/// Reference: Berk et al. (2017) Proc. SPIE 10198:101980H.
/// </summary>
public sealed class CkdPipelineTests
{
    private static AtmosphericInput BuildUSStandardInput(int nBands = 10)
    {
        // Visible-SWIR wavelength grid: 0.4–2.5 µm
        var grid = WavelengthGrid.Uniform(0.4, 2.5, (2.5 - 0.4) / (nBands - 1));

        return new AtmosphericInput
        {
            Grid             = grid,
            Profile          = AtmosphericProfile.FromStandard(StandardAtmosphere.USStandard),
            Geometry         = new ViewGeometry
            {
                SolarZenith_deg  = 30.0,
                SolarAzimuth_deg = 180.0,
                ViewZenith_deg   = 0.0,
                ViewAzimuth_deg  = 0.0,
            },
            SurfaceReflectance = [0.1], // spectrally flat 10% reflectance
        };
    }

    [Fact]
    public async Task ComputeAsync_ReturnsCorrectBandCount()
    {
        const int nBands = 10;
        var input  = BuildUSStandardInput(nBands);
        var rt     = new AccurateAtmosphericRT(ktablePath: null);
        var result = await rt.ComputeAsync(input);

        Assert.Equal(nBands, result.UpwellingRadiance.Length);
        Assert.Equal(nBands, result.Transmittance.Length);
        Assert.Equal(nBands, result.PathRadiance.Length);
        Assert.Equal(nBands, result.DownwellingIrradiance.Length);
    }

    [Fact]
    public async Task ComputeAsync_TransmittanceInRange()
    {
        var input  = BuildUSStandardInput(10);
        var rt     = new AccurateAtmosphericRT(ktablePath: null);
        var result = await rt.ComputeAsync(input);

        for (int i = 0; i < result.Transmittance.Length; i++)
        {
            double t = result.Transmittance[i];
            Assert.True(t >= 0.0 && t <= 1.0,
                $"Band {i}: transmittance {t:G4} is outside [0, 1]");
        }
    }

    [Fact]
    public async Task ComputeAsync_RadianceNonNegative()
    {
        var input  = BuildUSStandardInput(10);
        var rt     = new AccurateAtmosphericRT(ktablePath: null);
        var result = await rt.ComputeAsync(input);

        for (int i = 0; i < result.UpwellingRadiance.Length; i++)
        {
            Assert.True(result.UpwellingRadiance[i] >= 0,
                $"Band {i}: upwelling radiance is negative ({result.UpwellingRadiance[i]:G4})");
        }
    }

    [Fact]
    public async Task ComputeAsync_ModeName_IsAccurate()
    {
        var input  = BuildUSStandardInput(5);
        var rt     = new AccurateAtmosphericRT(ktablePath: null);
        var result = await rt.ComputeAsync(input);

        Assert.Contains("ACCURATE", result.ModeName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComputeAsync_CancellationToken_Cancels()
    {
        var input = BuildUSStandardInput(200); // large enough to take time
        var rt    = new AccurateAtmosphericRT(ktablePath: null);
        var cts   = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(1)); // cancel almost immediately

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => rt.ComputeAsync(input, cts.Token));
    }

    [Fact]
    public async Task ComputeAsync_HigherAlbedo_HigherUpwellingRadiance()
    {
        var grid    = WavelengthGrid.Uniform(0.5, 1.0, 0.125);
        var geom    = new ViewGeometry { SolarZenith_deg = 30.0 };
        var profile = AtmosphericProfile.FromStandard(StandardAtmosphere.USStandard);

        var inputLow = new AtmosphericInput
        {
            Grid               = grid,
            Profile            = profile,
            Geometry           = geom,
            SurfaceReflectance = [0.05],
        };
        var inputHigh = new AtmosphericInput
        {
            Grid               = grid,
            Profile            = profile,
            Geometry           = geom,
            SurfaceReflectance = [0.50],
        };

        var rt      = new AccurateAtmosphericRT(ktablePath: null);
        var resLow  = await rt.ComputeAsync(inputLow);
        var resHigh = await rt.ComputeAsync(inputHigh);

        // Higher surface albedo → higher upwelling radiance in at least some bands
        int countHigher = 0;
        for (int i = 0; i < resLow.UpwellingRadiance.Length; i++)
            if (resHigh.UpwellingRadiance[i] > resLow.UpwellingRadiance[i])
                countHigher++;

        Assert.True(countHigher > 0,
            "High-albedo scene should produce higher radiance in at least one band");
    }

    [Fact]
    public void EstimateComputeTime_ScalesWithBands()
    {
        var rt = new AccurateAtmosphericRT(ktablePath: null);

        var input10  = BuildUSStandardInput(10);
        var input100 = BuildUSStandardInput(100);

        double t10  = rt.EstimateComputeTime_ms(input10);
        double t100 = rt.EstimateComputeTime_ms(input100);

        Assert.True(t100 > t10,
            $"100-band estimate ({t100} ms) should exceed 10-band ({t10} ms)");
    }
}
