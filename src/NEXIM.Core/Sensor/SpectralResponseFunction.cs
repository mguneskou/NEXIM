// NEXIM — Sensor spectral response function (SRF).
//
// The SRF describes how efficiently each spectral band of the detector
// converts incident photons / radiance to detector signal. It combines:
//
//   • Per-band quantum efficiency (QE) as a function of wavelength
//   • Bandpass filter shapes: top-hat (pushbroom), Gaussian (spectrometer)
//   • Cross-band stray-light leakage (point-spread in spectral dimension)
//
// Reference for Gaussian bandpass model:
//   Mouroulis et al. (2000) "Design of pushbroom imaging spectrometers for
//   optimum recovery of spectroscopic and spatial information",
//   Appl. Opt. 39(13):2210. doi:10.1364/AO.39.002210.

using MathNet.Numerics.Integration;

namespace NEXIM.Core.Sensor;

// ─────────────────────────────────────────────────────────────────────────────
// Band definition
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Shape of an individual spectral band's response function.
/// </summary>
public enum BandShape
{
    /// <summary>
    /// Top-hat (rectangular) bandpass. Uniform response ∈ [centre−width/2,
    /// centre+width/2], zero outside. Typical for pushbroom sensors with
    /// order-sorting filters.
    /// </summary>
    TopHat,

    /// <summary>
    /// Gaussian bandpass. Response = exp(−(λ−λ₀)²/(2σ²)) where σ = FWHM/(2√(2ln2)).
    /// Typical for imaging spectrometers (AVIRIS, HyMap).
    /// </summary>
    Gaussian,
}

/// <summary>
/// Definition of a single spectral detector band.
/// </summary>
public sealed class BandDefinition
{
    /// <summary>Band index (0-based).</summary>
    public int Index { get; init; }

    /// <summary>Centre wavelength in µm.</summary>
    public double Centre_um { get; init; }

    /// <summary>
    /// Full-width at half-maximum (FWHM) in µm.
    /// For <see cref="BandShape.TopHat"/> this is the full filter passband width.
    /// For <see cref="BandShape.Gaussian"/> this is the FWHM of the Gaussian.
    /// </summary>
    public double Fwhm_um { get; init; }

    /// <summary>Band shape model.</summary>
    public BandShape Shape { get; init; } = BandShape.TopHat;

    /// <summary>Peak quantum efficiency [0,1].</summary>
    public double PeakQe { get; init; } = 1.0;

    /// <summary>
    /// Evaluate the normalised response (before QE scaling) at wavelength
    /// <paramref name="lambda_um"/>.
    /// The response is normalised so that the peak value equals 1.
    /// </summary>
    public double Response(double lambda_um)
    {
        double delta = lambda_um - Centre_um;
        return Shape switch
        {
            BandShape.TopHat  => Math.Abs(delta) <= Fwhm_um * 0.5 ? 1.0 : 0.0,
            BandShape.Gaussian => Math.Exp(-delta * delta / (2.0 * SigmaFromFwhm(Fwhm_um) * SigmaFromFwhm(Fwhm_um))),
            _ => 0.0,
        };
    }

    /// <summary>
    /// Evaluate the QE-weighted response at <paramref name="lambda_um"/>.
    /// = PeakQe × Response(lambda_um).
    /// </summary>
    public double QeWeightedResponse(double lambda_um)
        => PeakQe * Response(lambda_um);

    static double SigmaFromFwhm(double fwhm) => fwhm / (2.0 * Math.Sqrt(2.0 * Math.Log(2.0)));
}

// ─────────────────────────────────────────────────────────────────────────────
// SpectralResponseFunction
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Complete spectral response function (SRF) for a hyperspectral sensor.
/// Stores the per-band <see cref="BandDefinition"/> and provides methods for
/// integrating radiance spectra against the SRF to obtain per-band signal values.
/// </summary>
public sealed class SpectralResponseFunction
{
    readonly BandDefinition[] _bands;

    /// <summary>Number of spectral bands.</summary>
    public int BandCount => _bands.Length;

    /// <summary>
    /// Ordered list of band definitions (index 0 = shortest wavelength band).
    /// </summary>
    public IReadOnlyList<BandDefinition> Bands => _bands;

    /// <param name="bands">
    /// Band definitions ordered by increasing centre wavelength.
    /// Must contain at least one band.
    /// </param>
    public SpectralResponseFunction(IEnumerable<BandDefinition> bands)
    {
        _bands = bands?.ToArray()
            ?? throw new ArgumentNullException(nameof(bands));
        if (_bands.Length == 0)
            throw new ArgumentException("SRF must define at least one band.", nameof(bands));
    }

    // ─── Builders ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a uniform top-hat SRF with <paramref name="nBands"/> equally-spaced
    /// contiguous bands spanning [<paramref name="start_um"/>, <paramref name="end_um"/>].
    /// FWHM = (end−start)/nBands. Peak QE = <paramref name="peakQe"/>.
    /// </summary>
    public static SpectralResponseFunction UniformTopHat(
        double start_um, double end_um, int nBands, double peakQe = 1.0)
    {
        if (nBands <= 0) throw new ArgumentOutOfRangeException(nameof(nBands));
        if (end_um <= start_um) throw new ArgumentException("end_um must exceed start_um.");

        double fwhm = (end_um - start_um) / nBands;
        var bands = new BandDefinition[nBands];
        for (int i = 0; i < nBands; i++)
        {
            bands[i] = new BandDefinition
            {
                Index    = i,
                Centre_um = start_um + (i + 0.5) * fwhm,
                Fwhm_um  = fwhm,
                Shape    = BandShape.TopHat,
                PeakQe   = peakQe,
            };
        }
        return new SpectralResponseFunction(bands);
    }

    /// <summary>
    /// Creates a Gaussian SRF with the given centre wavelengths and a uniform
    /// FWHM. Bands need not be contiguous or uniformly spaced.
    /// </summary>
    public static SpectralResponseFunction Gaussian(
        double[] centres_um, double fwhm_um, double peakQe = 1.0)
    {
        ArgumentNullException.ThrowIfNull(centres_um);
        if (centres_um.Length == 0) throw new ArgumentException("centres_um must be non-empty.", nameof(centres_um));

        var sorted = centres_um.OrderBy(c => c).ToArray();
        var bands  = sorted.Select((c, i) => new BandDefinition
        {
            Index     = i,
            Centre_um = c,
            Fwhm_um   = fwhm_um,
            Shape     = BandShape.Gaussian,
            PeakQe    = peakQe,
        });
        return new SpectralResponseFunction(bands);
    }

    // ─── SRF integration ─────────────────────────────────────────────────────

    /// <summary>
    /// Convolves a spectral radiance array with each band's SRF to produce
    /// per-band effective radiances.
    ///
    /// For each band b:
    ///   L_b = ∫ L(λ) × SRF_b(λ) dλ  /  ∫ SRF_b(λ) dλ
    ///
    /// The denominator normalises the band so that a flat-spectrum input of
    /// unit radiance yields unit output for each band.
    ///
    /// Numerical integration uses trapezoidal quadrature over the supplied grid.
    /// </summary>
    /// <param name="radiance">
    /// Spectral radiance values [W/(m²·sr·µm)] at each point of
    /// <paramref name="wavelengths_um"/>. Must be the same length.
    /// </param>
    /// <param name="wavelengths_um">Wavelength grid in µm (same length as <paramref name="radiance"/>).</param>
    /// <returns>
    /// Per-band effective radiance [W/(m²·sr·µm)], length = <see cref="BandCount"/>.
    /// </returns>
    public double[] Convolve(double[] radiance, double[] wavelengths_um)
    {
        ArgumentNullException.ThrowIfNull(radiance);
        ArgumentNullException.ThrowIfNull(wavelengths_um);
        if (radiance.Length != wavelengths_um.Length)
            throw new ArgumentException("radiance and wavelengths_um must have equal length.");
        if (radiance.Length < 2)
            throw new ArgumentException("At least 2 spectral points are required for integration.");

        int      nBands = _bands.Length;
        int      nPts   = wavelengths_um.Length;
        double[] result = new double[nBands];

        for (int b = 0; b < nBands; b++)
        {
            var band     = _bands[b];
            double num   = 0.0;  // ∫ L(λ) × SRF(λ) dλ
            double denom = 0.0;  // ∫ SRF(λ) dλ

            // Trapezoidal rule
            for (int i = 0; i < nPts - 1; i++)
            {
                double lam0  = wavelengths_um[i];
                double lam1  = wavelengths_um[i + 1];
                double dlam  = lam1 - lam0;
                double srf0  = band.QeWeightedResponse(lam0);
                double srf1  = band.QeWeightedResponse(lam1);
                num   += 0.5 * dlam * (radiance[i] * srf0 + radiance[i + 1] * srf1);
                denom += 0.5 * dlam * (srf0 + srf1);
            }

            result[b] = denom > 0.0 ? num / denom : 0.0;
        }

        return result;
    }

    /// <summary>
    /// Returns the effective radiance at a single band index by trapezoidal
    /// convolution of the supplied high-resolution spectrum.
    /// </summary>
    public double ConvolveBand(int bandIndex, double[] radiance, double[] wavelengths_um)
    {
        if ((uint)bandIndex >= (uint)_bands.Length)
            throw new ArgumentOutOfRangeException(nameof(bandIndex));

        var    band  = _bands[bandIndex];
        int    nPts  = wavelengths_um.Length;
        double num   = 0.0;
        double denom = 0.0;

        for (int i = 0; i < nPts - 1; i++)
        {
            double lam0 = wavelengths_um[i];
            double lam1 = wavelengths_um[i + 1];
            double dlam = lam1 - lam0;
            double s0   = band.QeWeightedResponse(lam0);
            double s1   = band.QeWeightedResponse(lam1);
            num   += 0.5 * dlam * (radiance[i] * s0 + radiance[i + 1] * s1);
            denom += 0.5 * dlam * (s0 + s1);
        }

        return denom > 0.0 ? num / denom : 0.0;
    }
}
