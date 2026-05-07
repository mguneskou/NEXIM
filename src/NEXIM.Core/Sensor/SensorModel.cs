// NEXIM — Sensor model: radiance → DN conversion pipeline.
//
// Converts spectral radiance cubes into sensor Digital Number (DN) arrays by
// applying the following pipeline in order:
//
//   1. Spectral integration: convolve high-resolution radiance with SRF bands
//   2. Radiometric calibration: radiance → photons → electrons via
//         E = L × A_pixel × Ω_ifov × τ_optics × QE × t_int / (hν)
//      where:
//         A_pixel  = pixel area on focal plane [m²]
//         Ω_ifov   = solid angle of one ground pixel as seen by the sensor [sr]
//         τ_optics = transmittance of fore-optics (0–1)
//         QE       = quantum efficiency (included in SRF band PeakQe)
//         t_int    = integration time [s]
//         hν       = photon energy at band centre wavelength [J]
//   3. Noise: shot + read + dark + quantization (via NoiseEngine)
//   4. Output: per-band DN [0, 2^bits − 1]
//
// References:
//   Schott (2007) "Remote Sensing: The Image Chain Approach" (2nd ed.),
//     Oxford University Press. ISBN 978-0-19-517817-3.  (image chain model)
//   Stoughton & Wellman (1985) "A model for signal-to-noise ratio in
//     hyperspectral sensors", Proc. SPIE 0565:86.

namespace NEXIM.Core.Sensor;

// ─────────────────────────────────────────────────────────────────────────────
// Optics parameters
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Fore-optics parameters that control the radiometric throughput of the sensor.
/// </summary>
public sealed class OpticsParameters
{
    /// <summary>
    /// Fore-optics transmittance (0–1). Accounts for reflection and absorption
    /// losses in lenses, gratings, and order-sorting filters.
    /// Typical: 0.3–0.7 for a grating spectrometer.
    /// </summary>
    public double OpticsTransmittance { get; init; } = 0.5;

    /// <summary>
    /// Instantaneous field of view (IFOV) of a single detector pixel in radians
    /// (angular subtense along track and cross track are assumed equal).
    /// IFOV = pixel_pitch / focal_length.
    /// Typical nadir pushbroom: 0.5–2 mrad.
    /// </summary>
    public double Ifov_rad { get; init; } = 1e-3;

    /// <summary>
    /// Sensor altitude above the ground in metres. Used only for labelling /
    /// metadata; does not change the photon budget.
    /// </summary>
    public double Altitude_m { get; init; } = 5_000.0;

    /// <summary>
    /// Ground sampling distance (GSD) in metres, computed as
    /// IFOV × Altitude for a nadir sensor.
    /// </summary>
    public double Gsd_m => Ifov_rad * Altitude_m;

    /// <summary>
    /// Solid angle subtended by one ground resolution element as seen from the
    /// sensor [sr]. For a square pixel: Ω = IFOV².
    /// </summary>
    public double PixelSolidAngle_sr => Ifov_rad * Ifov_rad;
}

// ─────────────────────────────────────────────────────────────────────────────
// SensorModel
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// End-to-end sensor model that converts spectral radiance [W/(m²·sr·µm)] into
/// raw detector DN values.
///
/// <para>Execution steps (per pixel):</para>
/// <list type="number">
///   <item>SRF convolution: high-res radiance → per-band effective radiance.</item>
///   <item>Photon budget: effective radiance → mean electron count via
///     E = L_eff × A_det × Ω × τ × t_int / (hν_band).</item>
///   <item>Noise sampling: mean electrons → noisy DN via <see cref="NoiseEngine"/>.</item>
/// </list>
/// </summary>
public sealed class SensorModel
{
    readonly SpectralResponseFunction _srf;
    readonly OpticsParameters         _optics;
    readonly NoiseEngine              _noise;
    readonly NoiseParameters          _noiseParams;

    // Physical constants
    const double PlanckH_Js     = 6.62607015e-34;  // J·s
    const double SpeedOfLight_ms = 2.99792458e8;   // m/s
    const double Micro           = 1e-6;           // µm → m conversion

    // ─── Constructor ─────────────────────────────────────────────────────────

    /// <param name="srf">Spectral response function defining the bands.</param>
    /// <param name="optics">Fore-optics parameters.</param>
    /// <param name="noiseParams">Detector noise parameters.</param>
    public SensorModel(
        SpectralResponseFunction srf,
        OpticsParameters         optics,
        NoiseParameters          noiseParams)
    {
        _srf         = srf         ?? throw new ArgumentNullException(nameof(srf));
        _optics      = optics      ?? throw new ArgumentNullException(nameof(optics));
        _noiseParams = noiseParams ?? throw new ArgumentNullException(nameof(noiseParams));
        _noise       = new NoiseEngine(noiseParams);
    }

    // ─── Properties ──────────────────────────────────────────────────────────

    /// <summary>Number of spectral bands.</summary>
    public int BandCount => _srf.BandCount;

    /// <summary>ADC bit depth.</summary>
    public int AdcBits => _noiseParams.AdcBits;

    // ─── Core: single pixel ──────────────────────────────────────────────────

    /// <summary>
    /// Result of simulating the sensor response for a single pixel.
    /// </summary>
    public sealed class PixelResult
    {
        /// <summary>Per-band effective radiance after SRF convolution [W/(m²·sr·µm)].</summary>
        public required double[] EffectiveRadiance  { get; init; }
        /// <summary>Per-band mean electron count (noiseless).</summary>
        public required double[] MeanElectrons      { get; init; }
        /// <summary>Per-band noisy electron count (after noise sampling).</summary>
        public required double[] NoisyElectrons     { get; init; }
        /// <summary>Per-band digitised DN value [0, 2^bits − 1].</summary>
        public required int[]    Dn                 { get; init; }
        /// <summary>Per-band signal-to-noise ratio (linear).</summary>
        public required double[] Snr                { get; init; }
    }

    /// <summary>
    /// Simulates one pixel: convolves a high-resolution radiance spectrum with
    /// the SRF, applies the photon budget, and adds stochastic noise.
    /// </summary>
    /// <param name="spectralRadiance">
    /// High-resolution spectral radiance [W/(m²·sr·µm)], length must equal
    /// <paramref name="wavelengths_um"/>.Length.
    /// </param>
    /// <param name="wavelengths_um">
    /// Wavelength sampling of <paramref name="spectralRadiance"/> in µm.
    /// </param>
    /// <param name="rng">
    /// Random number generator for noise sampling. Pass a seeded instance for
    /// reproducible results.
    /// </param>
    /// <returns>
    /// A <see cref="PixelResult"/> with effective radiance, electrons, DN, and SNR
    /// for each band.
    /// </returns>
    public PixelResult SimulatePixel(
        double[] spectralRadiance,
        double[] wavelengths_um,
        Random   rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        // Step 1: SRF convolution
        double[] effRad = _srf.Convolve(spectralRadiance, wavelengths_um);

        int      nBands      = _srf.BandCount;
        double[] meanE       = new double[nBands];
        double[] noisyE      = new double[nBands];
        int[]    dn          = new int[nBands];
        double[] snr         = new double[nBands];

        double aPixel = (_noiseParams.PixelPitch_um * Micro)
                      * (_noiseParams.PixelPitch_um * Micro); // [m²]

        for (int b = 0; b < nBands; b++)
        {
            double lam_um = _srf.Bands[b].Centre_um;

            // Photon energy [J] at band centre wavelength
            double ePhoton = PlanckH_Js * SpeedOfLight_ms / (lam_um * Micro);

            // Step 2: mean electrons
            // E = L × A × Ω × τ × t_int / hν
            // (QE is already folded into the SRF convolution via PeakQe)
            double signal = effRad[b]
                          * aPixel
                          * _optics.PixelSolidAngle_sr
                          * _optics.OpticsTransmittance
                          * _noiseParams.IntegrationTime_s
                          / ePhoton;
            signal     = Math.Max(0.0, signal);
            meanE[b]   = signal;

            // Step 3: noise + DN
            var noiseResult = _noise.Sample(signal, rng);
            noisyE[b] = noiseResult.NoisySample_e;
            dn[b]     = noiseResult.Dn;
            snr[b]    = _noise.Snr(signal);
        }

        return new PixelResult
        {
            EffectiveRadiance = effRad,
            MeanElectrons     = meanE,
            NoisyElectrons    = noisyE,
            Dn                = dn,
            Snr               = snr,
        };
    }

    // ─── Batch: radiance cube ─────────────────────────────────────────────────

    /// <summary>
    /// Simulates a full hyperspectral image cube.
    /// </summary>
    /// <param name="radiance">
    /// Spectral radiance cube [nPixels × nWavelengths], where radiance[p] is the
    /// high-resolution spectrum for pixel p.
    /// </param>
    /// <param name="wavelengths_um">Wavelengths in µm (length = nWavelengths).</param>
    /// <param name="rng">RNG for noise (seeded for reproducibility).</param>
    /// <returns>
    /// DN cube [nPixels × nBands], where dnCube[p][b] is the DN for pixel p,
    /// band b.
    /// </returns>
    public int[][] SimulateCube(double[][] radiance, double[] wavelengths_um, Random rng)
    {
        ArgumentNullException.ThrowIfNull(radiance);
        int nPixels = radiance.Length;
        var dnCube  = new int[nPixels][];

        for (int p = 0; p < nPixels; p++)
            dnCube[p] = SimulatePixel(radiance[p], wavelengths_um, rng).Dn;

        return dnCube;
    }

    // ─── Convenience: SNR curve ───────────────────────────────────────────────

    /// <summary>
    /// Returns the theoretical SNR for a spectrally flat radiance level for each
    /// band in the sensor. Useful for sensor design trade studies.
    /// </summary>
    /// <param name="flatRadiance">Flat spectral radiance [W/(m²·sr·µm)].</param>
    public double[] SnrCurve(double flatRadiance)
    {
        int      nBands = _srf.BandCount;
        double[] snr    = new double[nBands];
        double   aPixel = (_noiseParams.PixelPitch_um * Micro)
                        * (_noiseParams.PixelPitch_um * Micro);

        for (int b = 0; b < nBands; b++)
        {
            double lam_um  = _srf.Bands[b].Centre_um;
            double ePhoton = PlanckH_Js * SpeedOfLight_ms / (lam_um * Micro);
            double signal  = flatRadiance
                           * _srf.Bands[b].PeakQe
                           * aPixel
                           * _optics.PixelSolidAngle_sr
                           * _optics.OpticsTransmittance
                           * _noiseParams.IntegrationTime_s
                           / ePhoton;
            snr[b] = _noise.Snr(Math.Max(0.0, signal));
        }
        return snr;
    }
}
