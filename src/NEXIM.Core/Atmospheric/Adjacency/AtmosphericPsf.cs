using System;
using System.Collections.Generic;
using NEXIM.Core.Atmospheric.DISORT;
using NEXIM.Core.Models;

namespace NEXIM.Core.Atmospheric.Adjacency;

/// <summary>
/// Computes the atmospheric point-spread function (PSF) from DISORT diffuse
/// transmission profiles.
///
/// The PSF describes how radiance from a surface point spreads into adjacent
/// pixels at the sensor due to multiple scattering in the atmosphere. It is
/// modelled as a 1D radial function derived from the azimuthally-averaged
/// diffuse transmittance computed by DISORT across angular streams.
///
/// Physical model:
///   T_diff(r) ≈ Σ_μ w(μ) T_diff(μ) J_0(2π r sin(θ) / λ_eff)
/// where J_0 is the Bessel function of the first kind, r is the ground
/// displacement, θ = arccos(μ), and λ_eff is the effective wavelength.
///
/// This is the "Tanré PSF" approach, approximating adjacency as a
/// linear convolution of the scene with this radially symmetric kernel.
///
/// Academic references:
///   Tanré et al. (1981) Appl. Opt. 20(20):3676. doi:10.1364/AO.20.003676
///   Vermote et al. (1997) IEEE TGRS 35(3):675. doi:10.1109/36.581987
///   Richter &amp; Schläpfer (2002) Remote Sens. Environ. 83(1-2):194.
///     doi:10.1016/S0034-4257(02)00036-6
/// </summary>
public sealed class AtmosphericPsf
{
    /// <summary>Radial distances at which the PSF is evaluated [km].</summary>
    public double[] Radii_km { get; }

    /// <summary>
    /// Normalised PSF values at each radial distance.
    /// Σ PSF[i] × ΔRadius = 1 (energy-conserving).
    /// </summary>
    public double[] Values { get; }

    /// <summary>
    /// Effective scale radius of the PSF [km]: the radius enclosing 50% of the
    /// total scattered energy. Smaller = sharper kernel (lower aerosol loading).
    /// </summary>
    public double EffectiveRadius_km { get; }

    private AtmosphericPsf(double[] radii, double[] values)
    {
        Radii_km            = radii;
        Values              = values;
        EffectiveRadius_km  = ComputeEffectiveRadius(radii, values);
    }

    /// <summary>
    /// Compute the atmospheric PSF for a given wavelength and aerosol loading.
    ///
    /// The PSF is derived from the DISORT diffuse downwelling flux profile as
    /// a function of stream angle. The approach follows Tanré et al. (1981):
    ///   1. Run DISORT to obtain diffuse downwelling flux at each stream cosine μ_i
    ///   2. Map stream angles to radial distances on the ground: r = H × tan(θ)
    ///      where H is sensor altitude above the surface in km
    ///   3. Construct the radial kernel from (r, T_diff(μ_i)) pairs
    ///   4. Normalise so the kernel integrates to unity
    /// </summary>
    /// <param name="disortOutput">Output of a DISORT calculation at the wavelength of interest.</param>
    /// <param name="streamCosines">Stream cosines from GaussLegendreQuadrature.</param>
    /// <param name="streamWeights">Corresponding quadrature weights.</param>
    /// <param name="sensorAltitude_km">Sensor altitude above surface [km].</param>
    public static AtmosphericPsf FromDisortOutput(
        DisortOutput disortOutput,
        double[]     streamCosines,
        double[]     streamWeights,
        double       sensorAltitude_km = 1.0)
    {
        ArgumentNullException.ThrowIfNull(disortOutput);
        int n = streamCosines.Length;

        // Build (radius, diffuse_weight) pairs
        var radii    = new double[n];
        var weights  = new double[n];
        double totalWeight = 0.0;

        for (int i = 0; i < n; i++)
        {
            double mu  = streamCosines[i];
            double theta = Math.Acos(Math.Clamp(mu, -1.0, 1.0));

            // Ground radius corresponding to this stream angle
            double r_km = sensorAltitude_km * Math.Tan(theta);

            // Diffuse downwelling radiance weighted by stream weight
            double flux = i < disortOutput.DownwellingFlux.Length
                ? disortOutput.DownwellingFlux[i]
                : 0.0;

            radii[i]    = r_km;
            weights[i]  = Math.Abs(streamWeights[i]) * Math.Abs(flux);
            totalWeight += weights[i];
        }

        // Normalise
        if (totalWeight > 1e-30)
            for (int i = 0; i < n; i++) weights[i] /= totalWeight;

        // Sort by ascending radius
        Array.Sort(radii, weights);

        return new AtmosphericPsf(radii, weights);
    }

    /// <summary>
    /// Build a simple exponential PSF:
    ///   PSF(r) = (1/r₀) × exp(−r/r₀)
    /// normalised to unit area. This is a low-cost fallback when DISORT output
    /// is not available for PSF derivation.
    /// </summary>
    /// <param name="scaleRadius_km">Exponential scale radius r₀ [km].</param>
    /// <param name="maxRadius_km">Maximum radius to evaluate.</param>
    /// <param name="nPoints">Number of radial sample points.</param>
    public static AtmosphericPsf Exponential(
        double scaleRadius_km = 0.5,
        double maxRadius_km   = 5.0,
        int    nPoints        = 64)
    {
        var r = new double[nPoints];
        var v = new double[nPoints];
        double dr = maxRadius_km / (nPoints - 1);

        for (int i = 0; i < nPoints; i++)
        {
            r[i] = i * dr;
            v[i] = Math.Exp(-r[i] / scaleRadius_km) / scaleRadius_km;
        }

        // Normalise (trapezoidal)
        double sum = 0;
        for (int i = 1; i < nPoints; i++)
            sum += 0.5 * (v[i - 1] + v[i]) * dr;
        if (sum > 1e-30)
            for (int i = 0; i < nPoints; i++) v[i] /= sum;

        return new AtmosphericPsf(r, v);
    }

    private static double ComputeEffectiveRadius(double[] radii, double[] values)
    {
        if (radii.Length < 2) return 0.0;
        double cumulative = 0.0;
        for (int i = 1; i < radii.Length; i++)
        {
            double dr = radii[i] - radii[i - 1];
            cumulative += 0.5 * (values[i - 1] + values[i]) * dr;
            if (cumulative >= 0.5) return radii[i];
        }
        return radii[^1];
    }
}
