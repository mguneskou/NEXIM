// NEXIM — Phase 10 unit tests: SRF, NoiseEngine, SensorModel.

using NEXIM.Core.Sensor;

namespace NEXIM.Tests.Sensor;

// ─────────────────────────────────────────────────────────────────────────────
// SpectralResponseFunction tests
// ─────────────────────────────────────────────────────────────────────────────

public class SpectralResponseFunctionTests
{
    // ── BandDefinition ────────────────────────────────────────────────────

    [Fact]
    public void BandDefinition_TopHat_InsideBand_ReturnsOne()
    {
        var band = new BandDefinition
        {
            Centre_um = 0.55,
            Fwhm_um   = 0.02,
            Shape     = BandShape.TopHat,
            PeakQe    = 1.0,
        };
        Assert.Equal(1.0, band.Response(0.55),  precision: 14);
        Assert.Equal(1.0, band.Response(0.541), precision: 14);  // well inside lower edge
        Assert.Equal(1.0, band.Response(0.559), precision: 14);  // well inside upper edge
    }

    [Fact]
    public void BandDefinition_TopHat_OutsideBand_ReturnsZero()
    {
        var band = new BandDefinition { Centre_um = 0.55, Fwhm_um = 0.02, Shape = BandShape.TopHat };
        Assert.Equal(0.0, band.Response(0.539));
        Assert.Equal(0.0, band.Response(0.561));
    }

    [Fact]
    public void BandDefinition_Gaussian_AtCentre_ReturnsOne()
    {
        var band = new BandDefinition { Centre_um = 0.55, Fwhm_um = 0.02, Shape = BandShape.Gaussian };
        Assert.Equal(1.0, band.Response(0.55), precision: 14);
    }

    [Fact]
    public void BandDefinition_Gaussian_AtFwhmEdge_ReturnsHalf()
    {
        var band = new BandDefinition { Centre_um = 0.55, Fwhm_um = 0.02, Shape = BandShape.Gaussian };
        // At ±FWHM/2 = ±0.01 µm, response = 0.5
        Assert.Equal(0.5, band.Response(0.55 + 0.01), 5);
        Assert.Equal(0.5, band.Response(0.55 - 0.01), 5);
    }

    [Fact]
    public void BandDefinition_QeWeightedResponse_ScalesByPeakQe()
    {
        var band = new BandDefinition
        {
            Centre_um = 0.55, Fwhm_um = 0.02,
            Shape = BandShape.TopHat, PeakQe = 0.7,
        };
        Assert.Equal(0.7, band.QeWeightedResponse(0.55), precision: 14);
    }

    // ── UniformTopHat factory ─────────────────────────────────────────────

    [Fact]
    public void UniformTopHat_BandCount_Correct()
    {
        var srf = SpectralResponseFunction.UniformTopHat(0.4, 0.9, 10);
        Assert.Equal(10, srf.BandCount);
    }

    [Fact]
    public void UniformTopHat_BandCentres_Correct()
    {
        var srf   = SpectralResponseFunction.UniformTopHat(0.4, 0.9, 5);
        double dw = 0.1;  // (0.9-0.4)/5
        for (int i = 0; i < 5; i++)
            Assert.Equal(0.4 + (i + 0.5) * dw, srf.Bands[i].Centre_um, 10);
    }

    // ── Gaussian factory ──────────────────────────────────────────────────

    [Fact]
    public void Gaussian_BandCount_MatchesCentresLength()
    {
        double[] centres = { 0.45, 0.55, 0.65 };
        var srf = SpectralResponseFunction.Gaussian(centres, 0.01);
        Assert.Equal(3, srf.BandCount);
    }

    // ── Convolve ──────────────────────────────────────────────────────────

    [Fact]
    public void Convolve_FlatSpectrum_ReturnsInputLevel()
    {
        // A flat spectrum of constant value V should give exactly V from each band
        var srf = SpectralResponseFunction.UniformTopHat(0.4, 0.9, 5);

        int      nPts       = 500;
        double[] wavelengths = Enumerable.Range(0, nPts)
            .Select(i => 0.4 + i * (0.9 - 0.4) / (nPts - 1)).ToArray();
        double[] radiance   = Enumerable.Repeat(10.0, nPts).ToArray();

        double[] result = srf.Convolve(radiance, wavelengths);

        foreach (double v in result)
            Assert.Equal(10.0, v, 4);
    }

    [Fact]
    public void Convolve_BandIsolation_TopHat_OtherBandsNearZero()
    {
        // Radiance confined to first band → only first band output non-zero.
        var srf = SpectralResponseFunction.UniformTopHat(0.4, 0.9, 5);
        // Band 0: 0.40–0.50

        int      nPts       = 1000;
        double[] wl         = Enumerable.Range(0, nPts)
            .Select(i => 0.4 + i * (0.5 - 0.4) / (nPts - 1)).ToArray();
        // Include all 500 pts range (must match across entire SRF)
        // Use the full range but set radiance to zero outside band 0
        double[] fullWl = Enumerable.Range(0, 2000)
            .Select(i => 0.4 + i * 0.5 / 1999).ToArray();
        double[] rad = fullWl.Select(l => l < 0.50 ? 5.0 : 0.0).ToArray();

        double[] result = srf.Convolve(rad, fullWl);

        Assert.True(result[0] > 4.5, $"Band 0 should be ~5.0, got {result[0]}");
        for (int b = 1; b < 5; b++)
            Assert.True(result[b] < 0.01, $"Band {b} should be ~0, got {result[b]}");
    }

    [Fact]
    public void Convolve_LengthMismatch_Throws()
    {
        var srf = SpectralResponseFunction.UniformTopHat(0.4, 0.9, 5);
        Assert.Throws<ArgumentException>(
            () => srf.Convolve(new double[100], new double[50]));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// NoiseEngine tests
// ─────────────────────────────────────────────────────────────────────────────

public class NoiseEngineTests
{
    static NoiseEngine MakeEngine(
        double readNoise = 50.0,
        double fullWell  = 80_000.0,
        int    bits      = 14,
        double dark      = 500.0,
        double tInt      = 1e-3) => new(new NoiseParameters
    {
        ReadNoise_e          = readNoise,
        FullWellCapacity_e   = fullWell,
        AdcBits              = bits,
        DarkCurrentRate_ePerS = dark,
        IntegrationTime_s    = tInt,
        PixelPitch_um        = 25.0,
    });

    [Fact]
    public void NoiseParameters_LsbSize_Correct()
    {
        var p  = new NoiseParameters { FullWellCapacity_e = 65536.0, AdcBits = 16 };
        Assert.Equal(1.0, p.LsbSize_e, 10);  // 65536 / 65536 = 1.0
    }

    [Fact]
    public void Sample_ZeroSignal_DnNearZero()
    {
        var engine = MakeEngine(readNoise: 5, dark: 0, tInt: 1e-6);
        var rng    = new Random(1);
        // With no signal and negligible dark, DN should be near 0
        var result = engine.Sample(0.0, rng);
        Assert.Equal(0, result.Dn);
    }

    [Fact]
    public void Sample_HighSignal_DnPositive()
    {
        var engine = MakeEngine();
        var rng    = new Random(2);
        var result = engine.Sample(40_000.0, rng);
        Assert.True(result.Dn > 0, "High signal should produce positive DN");
    }

    [Fact]
    public void Sample_FullWell_DnMaxOrSaturated()
    {
        var engine = MakeEngine(fullWell: 80_000, bits: 14);
        var rng    = new Random(3);
        // Signal at exactly full well
        var result = engine.Sample(80_000.0, rng);
        // DN should be AdcLevels-1 = 16383
        Assert.Equal(16383, engine.IdealDn(80_000.0));
    }

    [Fact]
    public void Snr_GrowsWithSignal()
    {
        var engine = MakeEngine();
        double snrLow  = engine.Snr(100.0);
        double snrHigh = engine.Snr(50_000.0);
        Assert.True(snrHigh > snrLow,
            $"SNR({50_000}) should exceed SNR({100}): {snrHigh} vs {snrLow}");
    }

    [Fact]
    public void Snr_ZeroSignal_ReturnsZero()
    {
        Assert.Equal(0.0, MakeEngine().Snr(0.0));
    }

    [Fact]
    public void Sample_MeanIsApproximatelyInputSignal()
    {
        // Monte Carlo: average of many samples should converge to mean signal
        var    engine = MakeEngine(readNoise: 5, dark: 0, tInt: 0);
        var    rng    = new Random(99);
        double sum    = 0.0;
        const  double signal = 10_000.0;
        const  int    N      = 5_000;

        for (int i = 0; i < N; i++)
            sum += engine.Sample(signal, rng).NoisySample_e;

        double mean = sum / N;
        // Mean should be within 2% of signal
        Assert.InRange(mean, signal * 0.98, signal * 1.02);
    }

    [Fact]
    public void IdealDn_SaturationAtFullWell()
    {
        var engine = MakeEngine(fullWell: 80_000, bits: 14);
        int dn = engine.IdealDn(80_000.0);
        Assert.Equal(16383, dn);  // 2^14 - 1
    }

    [Fact]
    public void DarkCurrentContributesMeanElectrons()
    {
        // With zero signal but non-zero dark current, mean total > 0
        var engine = MakeEngine(dark: 1_000_000.0, tInt: 1e-3);  // 1000 e dark/pixel
        var rng    = new Random(5);
        var result = engine.Sample(0.0, rng);
        Assert.True(result.DarkElectrons > 0, "Dark electrons should be > 0");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SensorModel tests
// ─────────────────────────────────────────────────────────────────────────────

public class SensorModelTests
{
    static (SensorModel model, double[] wl) MakeModel(int nBands = 5)
    {
        var srf    = SpectralResponseFunction.UniformTopHat(0.45, 0.90, nBands, peakQe: 0.8);
        var optics = new OpticsParameters
        {
            OpticsTransmittance = 0.5,
            Ifov_rad            = 1e-3,
            Altitude_m          = 5_000.0,
        };
        var noise  = new NoiseParameters
        {
            ReadNoise_e          = 30.0,
            FullWellCapacity_e   = 80_000.0,
            AdcBits              = 14,
            DarkCurrentRate_ePerS = 200.0,
            IntegrationTime_s    = 5e-3,
            PixelPitch_um        = 25.0,
        };
        var model = new SensorModel(srf, optics, noise);

        int      nPts = 500;
        double[] wl   = Enumerable.Range(0, nPts)
            .Select(i => 0.40 + i * 0.55 / (nPts - 1)).ToArray();
        return (model, wl);
    }

    [Fact]
    public void BandCount_MatchesSrfBandCount()
    {
        var (model, _) = MakeModel(7);
        Assert.Equal(7, model.BandCount);
    }

    [Fact]
    public void SimulatePixel_FlatRadiance_AllBandsPositiveDn()
    {
        var (model, wl) = MakeModel();
        double[] rad    = Enumerable.Repeat(50.0, wl.Length).ToArray();  // 50 W/m²/sr/µm
        var result = model.SimulatePixel(rad, wl, new Random(1));

        foreach (int dn in result.Dn)
            Assert.True(dn >= 0, $"DN should be non-negative, got {dn}");
        Assert.True(result.Dn.Any(dn => dn > 0), "At least one band should have non-zero DN");
    }

    [Fact]
    public void SimulatePixel_ZeroRadiance_NearZeroDn()
    {
        var (model, wl) = MakeModel();
        double[] rad    = new double[wl.Length];  // all zeros
        var result = model.SimulatePixel(rad, wl, new Random(10));

        // With zero signal only dark current contributes — DN should be small
        foreach (int dn in result.Dn)
            Assert.True(dn < 100, $"Zero-radiance DN should be small, got {dn}");
    }

    [Fact]
    public void SimulatePixel_BrightRadiance_HigherDnThanDim()
    {
        var (model, wl) = MakeModel();
        double[] bright = Enumerable.Repeat(100.0,  wl.Length).ToArray();
        double[] dim    = Enumerable.Repeat(  5.0,  wl.Length).ToArray();

        var brightResult = model.SimulatePixel(bright, wl, new Random(20));
        var dimResult    = model.SimulatePixel(dim,    wl, new Random(20));

        for (int b = 0; b < model.BandCount; b++)
            Assert.True(brightResult.Dn[b] >= dimResult.Dn[b],
                $"Bright DN ({brightResult.Dn[b]}) should be ≥ dim ({dimResult.Dn[b]}) in band {b}");
    }

    [Fact]
    public void SimulatePixel_SnrIsPositiveForNonZeroSignal()
    {
        var (model, wl) = MakeModel();
        double[] rad    = Enumerable.Repeat(20.0, wl.Length).ToArray();
        var result = model.SimulatePixel(rad, wl, new Random(30));

        foreach (double s in result.Snr)
            Assert.True(s >= 0.0, $"SNR should be non-negative, got {s}");
    }

    [Fact]
    public void SnrCurve_LengthEqualssBandCount()
    {
        var (model, _) = MakeModel(8);
        double[] snr = model.SnrCurve(10.0);
        Assert.Equal(8, snr.Length);
    }

    [Fact]
    public void SnrCurve_NonNegativeForPositiveRadiance()
    {
        var (model, _) = MakeModel();
        foreach (double s in model.SnrCurve(50.0))
            Assert.True(s >= 0.0, $"SNR should be non-negative, got {s}");
    }

    [Fact]
    public void SimulateCube_OutputDimensionsCorrect()
    {
        var (model, wl) = MakeModel(4);
        int nPixels     = 20;
        double[][] cube = Enumerable.Range(0, nPixels)
            .Select(_ => Enumerable.Repeat(10.0, wl.Length).ToArray()).ToArray();

        int[][] dn = model.SimulateCube(cube, wl, new Random(5));

        Assert.Equal(nPixels, dn.Length);
        foreach (int[] row in dn)
            Assert.Equal(4, row.Length);
    }

    [Fact]
    public void AdcBits_MatchesNoiseParameters()
    {
        var srf    = SpectralResponseFunction.UniformTopHat(0.45, 0.90, 5);
        var optics = new OpticsParameters();
        var noise  = new NoiseParameters { AdcBits = 12 };
        var model  = new SensorModel(srf, optics, noise);
        Assert.Equal(12, model.AdcBits);
    }
}
