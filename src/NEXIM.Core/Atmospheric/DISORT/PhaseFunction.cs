// NEXIM — Phase function models for DISORT.
//
// The phase function P(Θ) describes the angular redistribution of radiation
// by scattering events.  DISORT works with the Legendre polynomial expansion:
//   P(cos Θ) = Σ_{l=0}^{L} (2l+1) χ_l P_l(cos Θ)
// where χ_l are the phase function moments.
//
// References:
//   Henyey, L.C. & Greenstein, J.L. (1941).
//   Diffuse radiation in the Galaxy. Astrophys. J. 93, 70–83.
//   doi:10.1086/144246
//
//   Stamnes et al. (1988) Appl. Opt. 27(12):2502.
//   doi:10.1364/AO.27.002502  (DISORT; Chapter 2, phase function expansion)

namespace NEXIM.Core.Atmospheric.DISORT;

/// <summary>
/// Computes Legendre expansion moments χ_l for standard atmospheric phase functions.
///
/// The N Legendre moments are consumed directly by the DISORT eigensystem
/// to construct the scattering source terms.
/// </summary>
public static class PhaseFunction
{
    /// <summary>
    /// Henyey-Greenstein phase function moments.
    ///
    /// The analytic l-th moment of the HG phase function is simply g^l:
    ///   χ_l = g^l
    /// where g ∈ [−1,1] is the asymmetry parameter.
    ///
    /// Reference: Henyey &amp; Greenstein (1941) ApJ 93:70. doi:10.1086/144246.
    /// </summary>
    /// <param name="asymmetryG">Asymmetry parameter g (0 = isotropic, 1 = full forward).</param>
    /// <param name="nMoments">Number of Legendre moments to return (typically = nStreams).</param>
    /// <returns>Array of moments χ_0 … χ_{nMoments-1}.</returns>
    public static double[] HenyeyGreenstein(double asymmetryG, int nMoments)
    {
        if (nMoments <= 0) throw new ArgumentOutOfRangeException(nameof(nMoments));
        var moments = new double[nMoments];
        double gPow = 1.0;
        for (int l = 0; l < nMoments; l++)
        {
            moments[l] = gPow;
            gPow *= asymmetryG;
        }
        return moments;
    }

    /// <summary>
    /// Rayleigh scattering phase function moments.
    ///
    /// Rayleigh scattering by air molecules has the exact phase function:
    ///   P(cos Θ) = (3/4)(1 + cos²Θ)
    /// Legendre moments: χ_0 = 1, χ_2 = 1/10, all others = 0.
    ///
    /// This is the dominant scattering mechanism in the UV–visible.
    /// </summary>
    /// <param name="nMoments">Number of Legendre moments to return.</param>
    public static double[] Rayleigh(int nMoments)
    {
        var moments = new double[nMoments];
        moments[0] = 1.0;
        if (nMoments > 2) moments[2] = 0.1; // χ_2 = 1/10
        return moments;
    }

    /// <summary>
    /// Compute optical properties for a combined aerosol + Rayleigh layer.
    ///
    /// The layer total optical depth, single-scattering albedo (SSA), and
    /// combined phase function moments are computed as weighted sums of the
    /// Rayleigh and aerosol contributions:
    ///
    ///   ω_total = (ω_ray × τ_ray + ω_aer × τ_aer) / τ_total
    ///   χ_l_total = (ω_ray × τ_ray × χ_l_ray + ω_aer × τ_aer × χ_l_aer)
    ///               / (ω_total × τ_total)
    /// </summary>
    /// <param name="tauRayleigh">Rayleigh scattering optical depth for the layer.</param>
    /// <param name="tauAerosol">Aerosol extinction optical depth for the layer.</param>
    /// <param name="ssaAerosol">Aerosol single-scattering albedo (0–1).</param>
    /// <param name="gAerosol">Aerosol HG asymmetry parameter.</param>
    /// <param name="nMoments">Number of Legendre moments to compute.</param>
    /// <returns>
    /// Tuple: total optical depth, combined SSA, combined phase function moments.
    /// </returns>
    public static (double TotalTau, double TotalSsa, double[] PhaseMoments)
        CombineRayleighAerosol(
            double tauRayleigh, double tauAerosol, double ssaAerosol,
            double gAerosol, int nMoments)
    {
        double totalTau = tauRayleigh + tauAerosol;
        if (totalTau < 1e-30)
            return (0.0, 0.0, new double[nMoments]);

        // Rayleigh: SSA = 1 (pure scattering)
        double ssaRay = 1.0;
        double[] momRay = Rayleigh(nMoments);
        double[] momAer = HenyeyGreenstein(gAerosol, nMoments);

        double totalSsa = (ssaRay * tauRayleigh + ssaAerosol * tauAerosol) / totalTau;
        var combined = new double[nMoments];

        if (totalSsa < 1e-30)
            return (totalTau, 0.0, combined);

        for (int l = 0; l < nMoments; l++)
        {
            combined[l] = (ssaRay * tauRayleigh * momRay[l]
                         + ssaAerosol * tauAerosol * momAer[l])
                        / (totalSsa * totalTau);
        }

        return (totalTau, totalSsa, combined);
    }
}
