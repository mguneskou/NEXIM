// NEXIM.LutGen — LUT grid generator.
//
// Drives AccurateAtmosphericRT (Mode 2, CKD+DISORT 8-stream) over the full
// 5-dimensional LUT axis grid:
//   SZA × VZA × AOD × WVC × λ
//
// Three output fields are stored per grid point:
//   [0] Transmittance           (dimensionless, 0–1)
//   [1] PathRadiance_normalised (W m⁻² sr⁻¹ µm⁻¹ per unit solar flux)
//   [2] Downwelling_normalised  (W m⁻² µm⁻¹ per unit solar flux at surface)
//
// Grid specification (matches FastAtmosphericRT comments):
//   SZA: 11 nodes  [0, 10, 20, 30, 40, 50, 60, 65, 70, 75, 80]°
//   VZA:  9 nodes  [0, 10, 20, 30, 40, 50, 60, 70, 80]°
//   AOD:  7 nodes  [0.0, 0.05, 0.1, 0.2, 0.4, 0.8, 2.0]
//   WVC:  8 nodes  [0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0] g/cm²
//   λ:  250 nodes  0.4–2.5 µm  (same as KTableGeneratorTask.LutWavelengths_um)
//
// Total: 11×9×7×8×250×3 = 4 158 000 float32 values ≈ 15.9 MiB.
//
// Each (SZA, VZA, AOD, WVC) combination requires one AccurateAtmosphericRT call
// over the 250-band wavelength grid → 5 544 calls total.  Runs are parallelised
// across (SZA, VZA) pairs for performance.
//
// Typical runtime: 5–20 min on a 4-core machine (depends on k-table availability).

using NEXIM.Core.Atmospheric;
using NEXIM.Core.Atmospheric.LUT;
using NEXIM.Core.Models;

namespace NEXIM.LutGen;

internal static class LutGridGenerator
{
    // ── LUT axis grids ────────────────────────────────────────────────────────
    static readonly double[] SzaNodes = [0, 10, 20, 30, 40, 50, 60, 65, 70, 75, 80];
    static readonly double[] VzaNodes = [0, 10, 20, 30, 40, 50, 60, 70, 80];
    static readonly double[] AodNodes = [0.0, 0.05, 0.1, 0.2, 0.4, 0.8, 2.0];
    static readonly double[] WvcNodes = [0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0];

    // US Standard Atmosphere total H₂O column ≈ 2.9 g/cm²
    const double WvcStandard_g_cm2 = 2.9;

    /// <summary>
    /// Run the full LUT generation and write to <paramref name="lutFilePath"/>.
    /// </summary>
    public static void Generate(string ktablesDir, string lutFilePath, int maxThreads = -1)
    {
        var format = new LutFormat
        {
            Version          = 1,
            Magic            = "NEXIM_LUT",
            SzaNodes         = SzaNodes,
            VzaNodes         = VzaNodes,
            AodNodes         = AodNodes,
            WvcNodes         = WvcNodes,
            WavelengthNodes  = KTableGeneratorTask.LutWavelengths_um,
            NFields          = 3,
        };

        int totalFloats = (int)format.TotalFloats;
        var data = new float[totalFloats];

        int nSza = SzaNodes.Length;
        int nVza = VzaNodes.Length;
        int nAod = AodNodes.Length;
        int nWvc = WvcNodes.Length;
        int nWl  = format.WavelengthNodes.Length;

        int totalRuns  = nSza * nVza * nAod * nWvc;
        int done       = 0;

        Console.WriteLine($"  LUT grid: {nSza}×{nVza}×{nAod}×{nWvc}×{nWl} = {totalFloats:N0} floats");
        Console.WriteLine($"  Total AccurateAtmosphericRT calls: {totalRuns:N0}");

        var wavelengthGrid = new WavelengthGrid(KTableGeneratorTask.LutWavelengths_um);
        var baseLayers     = StandardAtmosphereLayers.USStandard18Levels;

        var po = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxThreads > 0 ? maxThreads : Environment.ProcessorCount,
        };

        // Parallelise over (iSza, iVza) outer loops; inner AOD+WVC loops are fast
        Parallel.For(0, nSza * nVza, po, szaVzaIdx =>
        {
            int iSza = szaVzaIdx / nVza;
            int iVza = szaVzaIdx % nVza;
            double sza = SzaNodes[iSza];
            double vza = VzaNodes[iVza];

            // Create one AccurateAtmosphericRT per thread (thread-local RT instance)
            var rt = new AccurateAtmosphericRT(ktablesDir);

            for (int iAod = 0; iAod < nAod; iAod++)
            for (int iWvc = 0; iWvc < nWvc; iWvc++)
            {
                double aod = AodNodes[iAod];
                double wvc = WvcNodes[iWvc];

                // Scale H₂O layers to match target WVC
                var scaledLayers = ScaleWvc(baseLayers, wvc);
                var profile  = AtmosphericProfile.FromCustomLayers(scaledLayers);
                var geometry = new ViewGeometry
                {
                    SolarZenith_deg  = sza,
                    SolarAzimuth_deg = 180.0,
                    ViewZenith_deg   = vza,
                    ViewAzimuth_deg  = 0.0,
                };
                var aerosol = new AerosolParameters { Aod550 = aod };
                double[] flatRefl = new double[wavelengthGrid.Count];
                Array.Fill(flatRefl, 0.15); // mid-range Lambertian reference surface

                var input = new AtmosphericInput
                {
                    Grid               = wavelengthGrid,
                    Profile            = profile,
                    Geometry           = geometry,
                    Aerosol            = aerosol,
                    SurfaceReflectance = flatRefl,
                };

                // Run Mode 2 synchronously (we are already on a parallel thread)
                var result = rt.ComputeAsync(input).GetAwaiter().GetResult();

                // Normalise to unit solar flux at each wavelength
                for (int iWl = 0; iWl < nWl; iWl++)
                {
                    double wl         = wavelengthGrid.Wavelengths_um[iWl];
                    double solarFlux  = SolarIrradiance(wl);
                    double safeSolar  = solarFlux > 1e-10 ? solarFlux : 1.0;

                    float transm  = (float)Math.Clamp(result.Transmittance[iWl],          0.0, 1.0);
                    float pathRad = (float)Math.Max  (result.PathRadiance[iWl] / safeSolar, 0.0);
                    float downwel = (float)Math.Max  (result.DownwellingIrradiance[iWl] / safeSolar, 0.0);

                    long baseIdx = format.FlatIndex(iSza, iVza, iAod, iWvc, iWl, 0);
                    data[baseIdx + 0] = transm;
                    data[baseIdx + 1] = pathRad;
                    data[baseIdx + 2] = downwel;
                }

                int d = Interlocked.Increment(ref done);
                if (d % 100 == 0 || d == totalRuns)
                    Console.WriteLine($"    {d}/{totalRuns} runs done  " +
                                      $"(SZA={sza:F0}° VZA={vza:F0}° AOD={aod:F2} WVC={wvc:F1})");
            }
        });

        Console.WriteLine($"  Writing LUT → {lutFilePath}");
        LutWriter.Write(lutFilePath, format, data);
        long fileSizeKb = new FileInfo(lutFilePath).Length / 1024;
        Console.WriteLine($"  LUT file size: {fileSizeKb} KiB");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Scale H₂O column densities in all layers to reach <paramref name="targetWvc"/>.</summary>
    static AtmosphericLayer[] ScaleWvc(AtmosphericLayer[] layers, double targetWvc_g_cm2)
    {
        double currentWvc = layers.Sum(l => l.H2O_g_cm2);
        double scale = currentWvc > 0 ? targetWvc_g_cm2 / currentWvc : 1.0;

        return layers.Select(l => new AtmosphericLayer
        {
            AltitudeBase_km = l.AltitudeBase_km,
            AltitudeTop_km  = l.AltitudeTop_km,
            Pressure_hPa    = l.Pressure_hPa,
            Temperature_K   = l.Temperature_K,
            H2O_g_cm2       = l.H2O_g_cm2 * scale,
            O3_atm_cm       = l.O3_atm_cm,
            CO2_VMR         = l.CO2_VMR,
            CH4_VMR         = l.CH4_VMR,
        }).ToArray();
    }

    /// <summary>
    /// Simplified Kurucz (1995) solar spectral irradiance at TOA [W m⁻² µm⁻¹].
    /// Parameterised as a 5800 K blackbody scaled to match the solar constant
    /// (1361 W/m²) and modulated by major Fraunhofer absorption features.
    /// </summary>
    static double SolarIrradiance(double lambda_um)
    {
        // Planck function for T=5800K, scaled to solar constant
        const double T_sun   = 5800.0;
        const double c1      = 1.1910429e-16; // 2hc² [W m² sr⁻¹]
        const double c2      = 1.4387769e-2;  // hc/kB [m K]
        const double R_scale = 2.17e-5;       // (R_sun/1AU)² × π sr

        double lambda_m = lambda_um * 1e-6;
        double planck   = c1 / (Math.Pow(lambda_m, 5) * (Math.Exp(c2 / (lambda_m * T_sun)) - 1.0));
        return planck * R_scale * 1e-6; // convert m to µm denominator
    }
}
