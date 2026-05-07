// NEXIM — Simulation orchestrator.
// Coordinates all three atmospheric RT modes, the sensor model, and output.
//
// Pipeline:
//  1. Build wavelength grid from AtmosphereConfig
//  2. Build atmospheric input (profile, geometry, aerosol, flat surface)
//  3. Run atmospheric RT (Fast / Accurate / FullPhysics)
//  4. For each scene pixel, apply sensor model to the at-sensor radiance
//  5. Return SimulationResult (DN cube + float32 BIL radiance cube)
//
// The runner is intentionally stateless; construct a new instance per run.

using System.Diagnostics;
using NEXIM.Core.Atmospheric;
using NEXIM.Core.Atmospheric.LUT;
using NEXIM.Core.Atmospheric.MonteCarlo;
using NEXIM.Core.Interfaces;
using NEXIM.Core.Models;
using NEXIM.Core.Rendering;
using NEXIM.Core.Sensor;

namespace NEXIM.UI;

/// <summary>
/// Output of a completed simulation run.
/// </summary>
public sealed class SimulationResult
{
    /// <summary>Rows × Bands slices — float32 BIL interleave for writing to .nxi / ENVI.</summary>
    public required float[][] RadianceCube  { get; init; }

    /// <summary>Per-pixel DN values: [pixelIndex][bandIndex].</summary>
    public required int[][]   DnCube        { get; init; }

    /// <summary>Centre wavelengths of all sensor bands in µm.</summary>
    public required double[]  Wavelengths_um { get; init; }

    public int Rows    { get; init; }
    public int Bands   { get; init; }
    public int Columns { get; init; }
    public double ElapsedMs { get; init; }
}

/// <summary>
/// Orchestrates a full NEXIM simulation pipeline run.
/// </summary>
public sealed class SimulationRunner
{
    readonly SceneConfig      _scene;
    readonly AtmosphereConfig _atm;
    readonly SensorConfig     _sensor;
    readonly MaterialMap?     _materialMap;

    public SimulationRunner(
        SceneConfig scene,
        AtmosphereConfig atm,
        SensorConfig sensor,
        MaterialMap? materialMap = null)
    {
        _scene       = scene;
        _atm         = atm;
        _sensor      = sensor;
        _materialMap = materialMap;
    }

    /// <summary>Run the simulation asynchronously and report progress.</summary>
    public async Task<SimulationResult> RunAsync(
        IProgress<(int pct, string msg)>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── 1. Build wavelength grid ───────────────────────────────────────
        progress?.Report((5, "Building wavelength grid…"));
        int nBands = _sensor.BandCount;
        double step = (_atm.EndWl_um - _atm.StartWl_um) / nBands;
        var atmGrid = WavelengthGrid.Uniform(_atm.StartWl_um, _atm.EndWl_um, _atm.StepWl_um);
        double[] sensorWl = Enumerable.Range(0, nBands)
            .Select(i => _atm.StartWl_um + (i + 0.5) * step).ToArray();

        ct.ThrowIfCancellationRequested();

        // ── 2. Build atmospheric input ─────────────────────────────────────
        progress?.Report((10, "Setting up atmospheric profile…"));
        StandardAtmosphere stdAtm = _atm.Atmosphere;
        var profile   = AtmosphericProfile.FromStandard(stdAtm, _atm.Altitude_km);
        var geometry  = new ViewGeometry
        {
            SolarZenith_deg  = _scene.SolarZenith_deg,
            SolarAzimuth_deg = _scene.SolarAzimuth_deg,
            ViewZenith_deg   = _scene.ViewZenith_deg,
            ViewAzimuth_deg  = _scene.ViewAzimuth_deg,
        };
        var aerosol = new AerosolParameters { Aod550 = _atm.Aot550 };
        double[] flatRefl = Enumerable.Repeat(_scene.Albedo, atmGrid.Count).ToArray();

        var atmInput = new AtmosphericInput
        {
            Grid              = atmGrid,
            Profile           = profile,
            Geometry          = geometry,
            Aerosol           = aerosol,
            SurfaceReflectance = flatRefl,
        };

        // ── 3. Run atmospheric RT ──────────────────────────────────────────
        progress?.Report((20, $"Running atmospheric RT ({_atm.Mode})…"));
        IAtmosphericRT rt = BuildRt();
        var atmResult = await rt.ComputeAsync(atmInput, ct);
        ct.ThrowIfCancellationRequested();

        // ── 4. Build sensor model ──────────────────────────────────────────
        progress?.Report((50, "Applying sensor model…"));
        var sensorPanel   = BuildSensorModel();
        int rows          = _materialMap?.Rows    ?? _scene.Rows;
        int cols          = _materialMap?.Columns ?? _scene.Columns;
        var rng           = new Random(42);

        // Pre-interpolate atmospheric components onto sensor wavelength grid
        double[] pathOnSensor  = InterpolateToGrid(atmGrid.Wavelengths_um, atmResult.PathRadiance,       sensorWl);
        double[] transOnSensor = InterpolateToGrid(atmGrid.Wavelengths_um, atmResult.Transmittance,      sensorWl);
        double[] edOnSensor    = InterpolateToGrid(atmGrid.Wavelengths_um, atmResult.DownwellingIrradiance, sensorWl);
        // flat-scene baseline (used when no material map is present)
        double[] upwellingOnSensorGrid = InterpolateToGrid(
            atmGrid.Wavelengths_um, atmResult.UpwellingRadiance, sensorWl);

        // Simulate each pixel
        int totalPixels = rows * cols;
        var bilCube = new float[rows * nBands][];
        for (int r = 0; r < rows; r++)
            for (int b = 0; b < nBands; b++)
                bilCube[r * nBands + b] = new float[cols];

        var dnCube = new int[totalPixels][];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                ct.ThrowIfCancellationRequested();

                double[] pixelRadiance;
                if (_materialMap is not null)
                {
                    // Physics: L = L_path + T * rho * E_d / π
                    double[] refl = _materialMap.GetReflectance(r, c, sensorWl);
                    pixelRadiance = new double[nBands];
                    for (int b = 0; b < nBands; b++)
                        pixelRadiance[b] = pathOnSensor[b] +
                            transOnSensor[b] * refl[b] * edOnSensor[b] / Math.PI;
                }
                else
                {
                    // Copy the baseline so each pixel gets independent noise.
                    pixelRadiance = (double[])upwellingOnSensorGrid.Clone();
                }

                var pixelResult = sensorPanel.SimulatePixel(pixelRadiance, sensorWl, rng);
                int pixIdx = r * cols + c;
                dnCube[pixIdx] = pixelResult.Dn;
                for (int b = 0; b < nBands; b++)
                {
                    // Store noise-scaled radiance: L_noisy = L_eff * (e_noisy / e_mean)
                    // so the BIL cube reflects true sensor measurement variability.
                    double meanE  = pixelResult.MeanElectrons[b];
                    double factor = meanE > 1e-12
                        ? pixelResult.NoisyElectrons[b] / meanE
                        : 1.0;
                    bilCube[r * nBands + b][c] = (float)(pixelResult.EffectiveRadiance[b] * factor);
                }

                if (_materialMap is not null && (pixIdx + 1) % 100 == 0)
                    progress?.Report((50 + (int)((double)pixIdx / totalPixels * 45),
                        $"Rendering pixel {pixIdx + 1}/{totalPixels} (image-driven)…"));
            }

            if (_materialMap is null)
            {
                int pct = 50 + (int)((double)(r + 1) / rows * 45);
                progress?.Report((pct, $"Rendering row {r + 1}/{rows}…"));
            }
        }

        sw.Stop();
        progress?.Report((100, "Complete."));

        return new SimulationResult
        {
            RadianceCube   = bilCube,
            DnCube         = dnCube,
            Wavelengths_um = sensorWl,
            Rows           = rows,
            Bands          = nBands,
            Columns        = cols,
            ElapsedMs      = sw.Elapsed.TotalMilliseconds,
        };
    }

    // ── RT factory ────────────────────────────────────────────────────────────

    IAtmosphericRT BuildRt() => _atm.Mode switch
    {
        AtmosphericMode.Fast =>
            // Requires a pre-generated LUT file; fall back to Accurate if absent.
            File.Exists("data/lut/nexim_v1.lut")
                ? new FastAtmosphericRT("data/lut/nexim_v1.lut")
                : (IAtmosphericRT)new AccurateAtmosphericRT(),

        AtmosphericMode.FullPhysics =>
            new FullPhysicsAtmosphericRT(photonCount: 10_000, preferGpu: false),

        _ => new AccurateAtmosphericRT(),
    };

    // ── Sensor model factory ──────────────────────────────────────────────────

    SensorModel BuildSensorModel()
    {
        int n      = _sensor.BandCount;
        double start = _sensor.StartWl_um, end = _sensor.EndWl_um;
        double spacing = (end - start) / n;
        double fwhm    = spacing * _sensor.FwhmFraction;

        SpectralResponseFunction srf = _sensor.UseGaussianSrf
            ? SpectralResponseFunction.Gaussian(
                Enumerable.Range(0, n).Select(i => start + (i + 0.5) * spacing).ToArray(),
                fwhm, _sensor.PeakQe)
            : SpectralResponseFunction.UniformTopHat(start, end, n, _sensor.PeakQe);

        var optics = new OpticsParameters
        {
            OpticsTransmittance = _sensor.OpticsTransmittance,
            Ifov_rad            = _sensor.Ifov_mrad * 1e-3,
            Altitude_m          = _sensor.Altitude_m,
        };
        var noise = new NoiseParameters
        {
            PixelPitch_um         = _sensor.PixelPitch_um,
            IntegrationTime_s     = _sensor.IntegrationTime_ms * 1e-3,
            FullWellCapacity_e    = _sensor.FullWellCapacity_e,
            AdcBits               = _sensor.AdcBits,
            ReadNoise_e           = _sensor.ReadNoise_e,
            DarkCurrentRate_ePerS = _sensor.DarkCurrentRate_ePerS,
        };
        return new SensorModel(srf, optics, noise);
    }

    // ── Linear interpolation ──────────────────────────────────────────────────

    static double[] InterpolateToGrid(double[] srcX, double[] srcY, double[] dstX)
    {
        var result = new double[dstX.Length];
        for (int i = 0; i < dstX.Length; i++)
            result[i] = LinearInterp(srcX, srcY, dstX[i]);
        return result;
    }

    static double LinearInterp(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        int lo = Array.BinarySearch(xs, x);
        if (lo >= 0) return ys[lo];
        lo = ~lo - 1;
        double t = (x - xs[lo]) / (xs[lo + 1] - xs[lo]);
        return ys[lo] + t * (ys[lo + 1] - ys[lo]);
    }
}
