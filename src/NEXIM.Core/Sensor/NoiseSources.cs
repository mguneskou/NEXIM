// NEXIM — Sensor noise sources.
//
// Implements the standard focal-plane-array (FPA) noise model used in
// hyperspectral sensor simulation:
//
//   N_total = N_shot + N_read + N_dark + N_quantization
//
// References:
//   Janesick (2001) "Scientific Charge-Coupled Devices", SPIE Press,
//     ISBN 978-0-8194-3698-6. (definitive CCD noise treatment)
//   Jensen et al. (2013) "HSI simulation framework", Proc. SPIE 8743,
//     doi:10.1117/12.2015944. (hyperspectral context)

namespace NEXIM.Core.Sensor;

// ─────────────────────────────────────────────────────────────────────────────
// Noise parameters
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Physical parameters describing the noise characteristics of a focal-plane
/// array (FPA) detector. All noise values are in units of electrons (e⁻) unless
/// stated otherwise.
/// </summary>
public sealed class NoiseParameters
{
    // ── Detector geometry / integration ──────────────────────────────────

    /// <summary>
    /// Detector pixel pitch (side length of a square pixel) in micrometres (µm).
    /// Typical range for hyperspectral sensors: 15–30 µm.
    /// </summary>
    public double PixelPitch_um { get; init; } = 25.0;

    /// <summary>
    /// Integration (exposure) time in seconds. Drives signal level and dark
    /// current accumulation.
    /// </summary>
    public double IntegrationTime_s { get; init; } = 1e-3;

    // ── Full-well / digitisation ──────────────────────────────────────────

    /// <summary>
    /// Full-well capacity: maximum number of electrons that can be stored in
    /// one pixel before saturation [e⁻]. Typical: 50 000–200 000 e⁻.
    /// </summary>
    public double FullWellCapacity_e { get; init; } = 80_000.0;

    /// <summary>
    /// Analog-to-digital converter (ADC) bit depth. Determines the number of
    /// quantization levels (2^bits) and the size of one least-significant bit.
    /// Typical hyperspectral: 12–16 bit.
    /// </summary>
    public int AdcBits { get; init; } = 14;

    // ── Read noise ────────────────────────────────────────────────────────

    /// <summary>
    /// RMS read-noise floor [e⁻ RMS per pixel per read]. Includes amplifier
    /// thermal noise and reset noise. Typical: 10–100 e⁻ for InGaAs / MCT;
    /// &lt;5 e⁻ for scientific CMOS.
    /// </summary>
    public double ReadNoise_e { get; init; } = 50.0;

    // ── Dark current ──────────────────────────────────────────────────────

    /// <summary>
    /// Dark current rate in electrons per second per pixel [e⁻/s/pixel].
    /// Typical cooled InGaAs: &lt;1 000 e⁻/s; uncooled MCT: 10⁶–10⁷ e⁻/s.
    /// </summary>
    public double DarkCurrentRate_ePerS { get; init; } = 500.0;

    // ── Derived ───────────────────────────────────────────────────────────

    /// <summary>
    /// Number of ADC quantization levels: 2^<see cref="AdcBits"/>.
    /// </summary>
    public int AdcLevels => 1 << AdcBits;

    /// <summary>
    /// Size of one ADC least-significant bit in electrons: FullWellCapacity / AdcLevels.
    /// </summary>
    public double LsbSize_e => FullWellCapacity_e / AdcLevels;
}

// ─────────────────────────────────────────────────────────────────────────────
// Noise engine
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Simulates the four principal noise sources that corrupt a raw detector signal,
/// returning the total noisy electron count and the digitised DN value.
/// </summary>
public sealed class NoiseEngine
{
    readonly NoiseParameters _p;

    /// <param name="parameters">Detector noise parameters.</param>
    public NoiseEngine(NoiseParameters parameters)
        => _p = parameters ?? throw new ArgumentNullException(nameof(parameters));

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Result of applying the full noise chain to a mean signal.
    /// </summary>
    public readonly struct NoiseResult
    {
        /// <summary>Mean signal input before noise, in electrons.</summary>
        public double MeanSignal_e  { get; init; }
        /// <summary>Shot noise σ [e⁻ RMS]: √(mean signal + dark electrons).</summary>
        public double ShotNoise_e   { get; init; }
        /// <summary>Read noise σ [e⁻ RMS].</summary>
        public double ReadNoise_e   { get; init; }
        /// <summary>Mean dark electrons accumulated during integration.</summary>
        public double DarkElectrons { get; init; }
        /// <summary>Quantization noise σ [e⁻ RMS]: LSB / √12.</summary>
        public double QuantNoise_e  { get; init; }
        /// <summary>
        /// Total combined noise σ [e⁻ RMS]:
        /// √(shot² + read² + quant²).
        /// Dark current is included in the shot noise term.
        /// </summary>
        public double TotalNoise_e  { get; init; }
        /// <summary>Noisy sampled electron count (clipped to [0, FullWell]).</summary>
        public double NoisySample_e { get; init; }
        /// <summary>Digitised value in DN [0, AdcLevels−1].</summary>
        public int    Dn            { get; init; }
    }

    /// <summary>
    /// Applies all noise sources to a mean signal level and returns a single
    /// stochastic sample.
    /// </summary>
    /// <param name="meanSignal_e">
    /// Expected number of photo-electrons generated by the optical signal
    /// (before dark current is added). Must be ≥ 0.
    /// </param>
    /// <param name="rng">
    /// Random number generator used for Poisson/Gaussian sampling. The caller
    /// owns the RNG so that sequences can be seeded deterministically.
    /// </param>
    /// <returns>
    /// A <see cref="NoiseResult"/> containing all intermediate quantities and
    /// the final DN value.
    /// </returns>
    public NoiseResult Sample(double meanSignal_e, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        meanSignal_e = Math.Max(0.0, meanSignal_e);

        // 1. Dark electrons
        double darkE    = _p.DarkCurrentRate_ePerS * _p.IntegrationTime_s;

        // 2. Shot noise: Poisson on (signal + dark); σ = √(μ_total)
        double meanTotal = meanSignal_e + darkE;
        double shotNoise = Math.Sqrt(meanTotal);

        // 3. Read noise: zero-mean Gaussian with σ = ReadNoise_e
        double readNoise = _p.ReadNoise_e;

        // 4. Quantization noise: uniform ±LSB/2 → σ = LSB/√12
        double quantNoise = _p.LsbSize_e / Math.Sqrt(12.0);

        // 5. Combined σ (add in quadrature, dark is inside shot)
        double totalNoise = Math.Sqrt(shotNoise * shotNoise + readNoise * readNoise + quantNoise * quantNoise);

        // 6. Stochastic sample: Gaussian approximation of Poisson for mean > ~20;
        //    exact Poisson sampling used for small means via Knuth algorithm.
        double noisySample = meanTotal >= 20.0
            ? meanTotal + totalNoise * rng.NextGaussian()
            : SamplePoisson(meanTotal, rng) + readNoise * rng.NextGaussian();

        // 7. Clip to [0, FullWell]
        noisySample = Math.Clamp(noisySample, 0.0, _p.FullWellCapacity_e);

        // 8. Digitise: DN = floor(noisySample / LSB), clipped to [0, AdcLevels−1]
        int dn = Math.Clamp((int)(noisySample / _p.LsbSize_e), 0, _p.AdcLevels - 1);

        return new NoiseResult
        {
            MeanSignal_e  = meanSignal_e,
            ShotNoise_e   = shotNoise,
            ReadNoise_e   = readNoise,
            DarkElectrons = darkE,
            QuantNoise_e  = quantNoise,
            TotalNoise_e  = totalNoise,
            NoisySample_e = noisySample,
            Dn            = dn,
        };
    }

    /// <summary>
    /// Computes the theoretical (noiseless) DN for a given mean signal,
    /// rounded to the nearest integer and clipped to [0, AdcLevels−1].
    /// Useful for SNR calculations.
    /// </summary>
    public int IdealDn(double meanSignal_e)
    {
        double total = Math.Max(0.0, meanSignal_e + _p.DarkCurrentRate_ePerS * _p.IntegrationTime_s);
        return Math.Clamp((int)(total / _p.LsbSize_e), 0, _p.AdcLevels - 1);
    }

    /// <summary>
    /// Signal-to-Noise Ratio (SNR) in linear scale for a given mean signal.
    /// SNR = μ_signal / σ_total, where σ_total is the quadrature sum of all
    /// noise terms evaluated at that signal level.
    /// Returns 0 when signal ≤ 0.
    /// </summary>
    public double Snr(double meanSignal_e)
    {
        if (meanSignal_e <= 0.0) return 0.0;
        double darkE      = _p.DarkCurrentRate_ePerS * _p.IntegrationTime_s;
        double shotNoise  = Math.Sqrt(meanSignal_e + darkE);
        double quantNoise = _p.LsbSize_e / Math.Sqrt(12.0);
        double totalNoise = Math.Sqrt(shotNoise * shotNoise
                                    + _p.ReadNoise_e * _p.ReadNoise_e
                                    + quantNoise * quantNoise);
        return meanSignal_e / totalNoise;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Knuth's exact Poisson sampler. Suitable for mean &lt; 20 (for larger means
    /// the Gaussian approximation is used).
    /// </summary>
    static double SamplePoisson(double mean, Random rng)
    {
        double limit = Math.Exp(-mean);
        double prod  = rng.NextDouble();
        int    k     = 0;
        while (prod > limit)
        {
            prod *= rng.NextDouble();
            k++;
        }
        return k;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Extension: Gaussian sampling via Box-Muller
// ─────────────────────────────────────────────────────────────────────────────

internal static class RandomExtensions
{
    /// <summary>
    /// Samples a standard normal N(0,1) deviate using the Box-Muller transform.
    /// </summary>
    public static double NextGaussian(this Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(); // exclude 0
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
