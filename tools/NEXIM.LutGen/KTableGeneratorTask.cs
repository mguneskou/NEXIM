// NEXIM.LutGen — K-table generation orchestrator.
//
// Generates H₂O_0000.ktbl … H₂O_XXXX.ktbl files covering the LUT wavelength
// grid (0.4–2.5 µm, 250 nodes).  Each file contains the k-distribution for
// one spectral interval at 8 pressure levels × 5 temperature offsets × 16 g-points.
//
// Generation is parallelised across wavelength bands for speed.
// Typical runtime: ~15–30 s on a 4-core machine.

using NEXIM.Core.Atmospheric.CKD;

namespace NEXIM.LutGen;

internal static class KTableGeneratorTask
{
    // LUT wavelength grid parameters (must match LutGridGenerator)
    public static readonly double[] LutWavelengths_um = BuildLutGrid();

    private static double[] BuildLutGrid()
    {
        const int N     = 250;
        const double wl0 = 0.4;
        const double wl1 = 2.5;
        var arr = new double[N];
        for (int i = 0; i < N; i++)
            arr[i] = wl0 + i * (wl1 - wl0) / (N - 1);
        arr[N - 1] = wl1; // clamp last point exactly
        return arr;
    }

    /// <summary>
    /// Generate all k-table files into <paramref name="outputDir"/>.
    /// Creates the directory if it does not exist.
    /// </summary>
    public static void GenerateAll(string outputDir, int maxThreads = -1)
    {
        Directory.CreateDirectory(outputDir);
        int n = LutWavelengths_um.Length;

        Console.WriteLine($"  Generating {n} H₂O k-tables into: {outputDir}");

        // Interval width: use spacing between adjacent grid points
        double[] intervals = new double[n];
        for (int i = 0; i < n; i++)
        {
            double lo = i > 0     ? LutWavelengths_um[i - 1] : LutWavelengths_um[0];
            double hi = i < n - 1 ? LutWavelengths_um[i + 1] : LutWavelengths_um[n - 1];
            intervals[i] = (hi - lo) * 0.5;
        }
        // Ensure minimum interval width
        for (int i = 0; i < n; i++)
            intervals[i] = Math.Max(intervals[i], 0.001);

        int done = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxThreads > 0 ? maxThreads : Environment.ProcessorCount,
        };

        Parallel.For(0, n, options, i =>
        {
            double lambda  = LutWavelengths_um[i];
            double dLambda = intervals[i];

            float[] kValues = KDistributionComputer.Compute(
                lambda, dLambda,
                SpectralConstants.PressureLevels_hPa,
                SpectralConstants.TemperatureOffsets_K,
                SpectralConstants.GPoints);

            string path = Path.Combine(outputDir, $"H2O_{i:D4}.ktbl");

            KtblWriter.Write(
                path,
                GasSpecies.H2O,
                bandCentre_um          : lambda,
                bandWidth_um           : dLambda,
                pressureLevels_hPa     : SpectralConstants.PressureLevels_hPa,
                tempOffsets_K          : SpectralConstants.TemperatureOffsets_K,
                gPoints                : SpectralConstants.GPoints,
                gWeights               : SpectralConstants.GWeights,
                kValues                : kValues);

            int d = Interlocked.Increment(ref done);
            if (d % 25 == 0 || d == n)
                Console.WriteLine($"    {d}/{n} k-tables written  ({lambda:F3} µm)");
        });

        Console.WriteLine($"  H₂O k-tables: {n} files written.");
    }
}
