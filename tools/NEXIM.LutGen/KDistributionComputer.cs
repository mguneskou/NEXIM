// NEXIM.LutGen — k-Distribution Computer.
//
// For each spectral interval and each (pressure, temperature) grid point,
// computes the k-distribution k(g) ∈ [0,1] by:
//
//   1. Placing N_SUB = 20 aggregate Lorentzian "super-lines" uniformly across
//      the interval.  Each super-line carries equal strength
//      S_i = k_mean(λ) × δν / N_SUB [cm/molecule].
//
//   2. Sampling k(ν) on a 200-point sub-grid within the interval and sorting
//      to form the cumulative k-distribution.
//
//   3. Evaluating the sorted CDF at the 16 Gauss-Legendre quadrature abscissae
//      to produce the final k(g) array.
//
// Pressure/temperature scaling follows standard HITRAN conventions:
//   γ(P, T) = γ₀ × (P/P₀) × (T₀/T)^0.75    [half-width]
//   S(T)    = S₀ × (T₀/T)^0.5 × exp(−E" × hc/kB × (1/T − 1/T₀))  [strength]
//
// References:
//   Lacis & Oinas (1991) doi:10.1029/90JD01945
//   Clough et al. (1989) doi:10.1016/0022-4073(89)90028-6

namespace NEXIM.LutGen;

internal static class KDistributionComputer
{
    private const int NSub      = 20;   // aggregate sub-lines per interval
    private const int NSample   = 200;  // spectral sampling points per interval
    private const double UmToCm1 = 1e4; // µm → cm⁻¹: ν[cm⁻¹] = 10000/λ[µm]

    /// <summary>
    /// Compute a k-value array [nP × nT × nG] for one spectral interval.
    /// </summary>
    /// <param name="centreLambda_um">Band centre wavelength [µm].</param>
    /// <param name="intervalWidth_um">Spectral interval width [µm] — typically the grid spacing.</param>
    /// <param name="pressures_hPa">Pressure grid.</param>
    /// <param name="tempOffsets_K">Temperature offsets relative to reference T₀.</param>
    /// <param name="gPoints">Gauss-Legendre quadrature abscissae on [0,1].</param>
    /// <returns>
    /// float[nP, nT, nG] — row-major k-values [cm²/molecule] per (P, T, g).
    /// </returns>
    public static float[] Compute(
        double   centreLambda_um,
        double   intervalWidth_um,
        double[] pressures_hPa,
        double[] tempOffsets_K,
        double[] gPoints)
    {
        int nP = pressures_hPa.Length;
        int nT = tempOffsets_K.Length;
        int nG = gPoints.Length;
        var result = new float[nP * nT * nG];

        // Compute band effective lower-state energy (weighted average of band contributions)
        double eLower = EffectiveLowerEnergy(centreLambda_um);

        // Mean cross-section at reference conditions
        double sigmaRef = SpectralConstants.H2OMeanCrossSection(centreLambda_um);

        // Centre and width in cm⁻¹
        double nu0_cm1    = UmToCm1 / centreLambda_um;
        double dnu_cm1    = Math.Abs(UmToCm1 / (centreLambda_um - intervalWidth_um * 0.5)
                                   - UmToCm1 / (centreLambda_um + intervalWidth_um * 0.5));

        // Sub-line positions uniformly across the interval
        var subLineNu = new double[NSub];
        for (int i = 0; i < NSub; i++)
            subLineNu[i] = nu0_cm1 - dnu_cm1 * 0.5 + (i + 0.5) * dnu_cm1 / NSub;

        // Sub-sample frequencies across the interval
        var sampleNu = new double[NSample];
        for (int j = 0; j < NSample; j++)
            sampleNu[j] = nu0_cm1 - dnu_cm1 * 0.5 + (j + 0.5) * dnu_cm1 / NSample;

        // Per-line total strength at reference conditions:
        //   S_i = sigmaRef × dnu / NSub   [cm/molecule]
        //   (integral of Lorentzian = S, so S distributed over NSub lines)
        double sRef = sigmaRef * dnu_cm1 / NSub;

        var kBuffer = new double[NSample];

        for (int ip = 0; ip < nP; ip++)
        {
            double P = pressures_hPa[ip];

            // Pressure-broadened half-width [cm⁻¹]
            double gamma0 = SpectralConstants.H2OGamma0 * (P / SpectralConstants.P0_hPa);

            for (int it = 0; it < nT; it++)
            {
                double T = SpectralConstants.T0_K + tempOffsets_K[it];
                T = Math.Max(T, 100.0); // physical minimum

                // Temperature-scaled half-width
                double gamma = gamma0 * Math.Pow(SpectralConstants.T0_K / T, SpectralConstants.GammaTempExp);

                // Temperature-scaled line strength
                double fT    = SpectralConstants.H2OTempScaling(T, eLower);
                double sScaled = sRef * fT;

                // Compute k(ν) at each sample point by summing Lorentzian sub-lines
                for (int j = 0; j < NSample; j++)
                {
                    double kNu = SpectralConstants.H2OContinuumBase;
                    double nuJ = sampleNu[j];
                    for (int i = 0; i < NSub; i++)
                    {
                        double dnu   = nuJ - subLineNu[i];
                        double lorentz = gamma / Math.PI / (dnu * dnu + gamma * gamma);
                        kNu += sScaled * lorentz;
                    }
                    kBuffer[j] = kNu;
                }

                // Sort to form the cumulative k-distribution
                Array.Sort(kBuffer);

                // Evaluate at Gauss-Legendre abscissae via linear interpolation
                for (int ig = 0; ig < nG; ig++)
                {
                    double g   = gPoints[ig];
                    double pos = g * (NSample - 1);
                    int    lo  = (int)pos;
                    int    hi  = Math.Min(lo + 1, NSample - 1);
                    double t   = pos - lo;
                    double k   = kBuffer[lo] * (1.0 - t) + kBuffer[hi] * t;

                    result[ip * nT * nG + it * nG + ig] = (float)Math.Max(k, 0.0);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Effective lower-state energy [cm⁻¹] for the spectral interval, computed
    /// as the contribution-weighted average of all overlapping H₂O band clusters.
    /// </summary>
    private static double EffectiveLowerEnergy(double lambda_um)
    {
        double totalWeight = 0.0;
        double weightedE   = 0.0;

        foreach (var band in SpectralConstants.H2OBands)
        {
            double dlambda  = lambda_um - band.Centre_um;
            double sigmaEnv = band.Fwhm_um / (2.0 * Math.Sqrt(2.0 * Math.Log(2.0)));
            double weight   = band.SigmaPeak_cm2
                            * Math.Exp(-dlambda * dlambda / (2.0 * sigmaEnv * sigmaEnv));
            weightedE   += weight * band.E_lower_cm1;
            totalWeight += weight;
        }

        return totalWeight > 0 ? weightedE / totalWeight : 200.0;
    }
}
