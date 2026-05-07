// NEXIM — Spectral band definition.
// A spectral band represents a single sensor channel with a central wavelength
// and a spectral response function (SRF).

namespace NEXIM.Core.Models;

/// <summary>
/// Spectral response function shape for a sensor band.
/// </summary>
public enum SrfType
{
    /// <summary>Ideal rectangular (top-hat) bandpass.</summary>
    Rectangular,

    /// <summary>
    /// Gaussian SRF parameterised by centre wavelength and FWHM.
    /// Most common approximation for pushbroom hyperspectral sensors.
    /// </summary>
    Gaussian,

    /// <summary>Tabulated SRF provided as an explicit wavelength–response curve.</summary>
    Tabulated,
}

/// <summary>
/// Defines a single spectral band of a hyperspectral sensor.
/// </summary>
public sealed class SpectralBand
{
    /// <summary>Centre wavelength in µm.</summary>
    public double CentreWavelength_um { get; }

    /// <summary>Full-width at half-maximum (FWHM) in µm.</summary>
    public double Fwhm_um { get; }

    /// <summary>Shape of the spectral response function.</summary>
    public SrfType ResponseType { get; }

    /// <summary>
    /// Optional tabulated SRF: array of (wavelength µm, normalised response) pairs.
    /// Only populated when <see cref="ResponseType"/> is <see cref="SrfType.Tabulated"/>.
    /// </summary>
    public (double Wavelength_um, double Response)[]? TabulatedSrf { get; }

    public SpectralBand(double centreWavelength_um, double fwhm_um, SrfType responseType = SrfType.Gaussian)
    {
        if (centreWavelength_um <= 0) throw new ArgumentOutOfRangeException(nameof(centreWavelength_um));
        if (fwhm_um <= 0) throw new ArgumentOutOfRangeException(nameof(fwhm_um));
        CentreWavelength_um = centreWavelength_um;
        Fwhm_um = fwhm_um;
        ResponseType = responseType;
    }

    public SpectralBand(double centreWavelength_um, double fwhm_um,
        (double Wavelength_um, double Response)[] tabulatedSrf)
        : this(centreWavelength_um, fwhm_um, SrfType.Tabulated)
    {
        ArgumentNullException.ThrowIfNull(tabulatedSrf);
        if (tabulatedSrf.Length < 2)
            throw new ArgumentException("Tabulated SRF must have at least 2 points.", nameof(tabulatedSrf));
        TabulatedSrf = tabulatedSrf;
    }

    /// <summary>
    /// Evaluate the (unnormalised) response at <paramref name="wavelength_um"/>.
    /// </summary>
    public double Evaluate(double wavelength_um) => ResponseType switch
    {
        SrfType.Rectangular => Math.Abs(wavelength_um - CentreWavelength_um) <= Fwhm_um / 2.0 ? 1.0 : 0.0,
        SrfType.Gaussian => GaussianResponse(wavelength_um),
        SrfType.Tabulated => InterpolateSrf(wavelength_um),
        _ => throw new InvalidOperationException($"Unknown SrfType {ResponseType}"),
    };

    private double GaussianResponse(double wavelength_um)
    {
        // σ = FWHM / (2√(2 ln 2))
        double sigma = Fwhm_um / (2.0 * Math.Sqrt(2.0 * Math.Log(2.0)));
        double dx = wavelength_um - CentreWavelength_um;
        return Math.Exp(-0.5 * (dx * dx) / (sigma * sigma));
    }

    private double InterpolateSrf(double wavelength_um)
    {
        var srf = TabulatedSrf!;
        if (wavelength_um <= srf[0].Wavelength_um) return srf[0].Response;
        if (wavelength_um >= srf[^1].Wavelength_um) return srf[^1].Response;

        // Binary search for bracketing interval
        int lo = 0, hi = srf.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (srf[mid].Wavelength_um <= wavelength_um) lo = mid; else hi = mid;
        }
        double t = (wavelength_um - srf[lo].Wavelength_um) /
                   (srf[hi].Wavelength_um - srf[lo].Wavelength_um);
        return srf[lo].Response + t * (srf[hi].Response - srf[lo].Response);
    }
}
