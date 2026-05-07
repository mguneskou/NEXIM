// NEXIM — Mie scattering theory for spherical cloud droplets.
// Implements the Wiscombe (1980) stable recurrence algorithm as documented
// in Bohren & Huffman (1983) "Absorption and Scattering of Light by Small
// Particles", Wiley, Sections 4.4–4.8.
//
// Academic references:
//   Bohren & Huffman (1983) — canonical Mie theory reference
//   Wiscombe (1980) NCAR/TN-140+STR — stable logarithmic-derivative recurrence
//   Segelstein (1981) — water complex refractive index table

namespace NEXIM.Core.Atmospheric.MonteCarlo;

/// <summary>
/// Mie efficiency factors for a single spherical droplet.
/// </summary>
public readonly struct MieResult
{
    /// <summary>Extinction efficiency Q_ext (dimensionless).</summary>
    public double Qext { get; init; }

    /// <summary>Scattering efficiency Q_sca (dimensionless).</summary>
    public double Qsca { get; init; }

    /// <summary>Absorption efficiency Q_abs = Q_ext − Q_sca.</summary>
    public double Qabs => Qext - Qsca;

    /// <summary>
    /// Asymmetry parameter g = ⟨cos θ⟩ (−1 to 1).
    /// Near 1 for large forward-scattering cloud droplets.
    /// </summary>
    public double AsymmetryG { get; init; }
}

/// <summary>
/// Mie efficiency factors for spherical particles.
///
/// Uses the Wiscombe (1980) downward-recurrence algorithm for the logarithmic
/// derivative D_n, which is numerically stable for all size parameters
/// (unlike the forward-recurrence algorithm which overflows for x ≳ 20).
///
/// Valid for: 0.1 ≤ x ≤ 10 000 (x = 2πr/λ), complex refractive index m.
/// </summary>
public static class MieCalculator
{
    // ── Water refractive index table (Segelstein 1981) ───────────────────────
    // λ (µm), real part n_r, imaginary part n_i
    private static readonly double[] _wlTable =
        { 0.20, 0.30, 0.40, 0.50, 0.60, 0.70, 0.80, 0.90,
          1.00, 1.20, 1.40, 1.60, 1.80, 2.00, 2.50, 3.00,
          4.00, 5.00, 6.00, 7.00, 8.00, 9.00, 10.0, 12.0 };

    private static readonly double[] _nrTable =
        { 1.396, 1.349, 1.343, 1.337, 1.332, 1.331, 1.329, 1.329,
          1.327, 1.324, 1.313, 1.312, 1.296, 1.262, 1.368, 1.371,
          1.351, 1.325, 1.262, 1.317, 1.292, 1.269, 2.000, 1.400 };

    private static readonly double[] _niTable =
        { 1.1e-7, 1.6e-9, 1.1e-9, 1.0e-9, 1.4e-9, 3.0e-9, 7.4e-9, 1.6e-8,
          4.1e-8, 1.3e-7, 1.6e-5, 2.5e-4, 1.0e-4, 1.1e-3, 1.1e-2, 3.2e-2,
          4.1e-2, 2.2e-1, 4.2e-1, 1.4e-1, 8.8e-2, 1.2e-1, 2.8e-1, 3.1e-1 };

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Compute Mie efficiency factors for water droplets at the given wavelength.
    /// </summary>
    /// <param name="radius_um">Sphere radius in µm.</param>
    /// <param name="wavelength_um">Wavelength in µm.</param>
    public static MieResult ComputeForWater(double radius_um, double wavelength_um)
    {
        (double nr, double ni) = InterpolateWaterRefIndex(wavelength_um);
        double x = 2.0 * Math.PI * radius_um / wavelength_um;
        return Compute(x, nr, ni);
    }

    /// <summary>
    /// Core Mie computation for a sphere of complex refractive index m = nr − i·ni.
    /// </summary>
    /// <param name="sizeParam">Size parameter x = 2πr/λ.</param>
    /// <param name="nr">Real part of the refractive index.</param>
    /// <param name="ni">Imaginary part of the refractive index (positive = absorbing).</param>
    public static MieResult Compute(double sizeParam, double nr, double ni)
    {
        double x = sizeParam;
        if (x < 1e-10)
            return new MieResult { Qext = 0, Qsca = 0, AsymmetryG = 0 };

        // Modulus of complex refractive index
        double mAbs  = Math.Sqrt(nr * nr + ni * ni);
        double xmAbs = mAbs * x;

        // ── Step 1: Number of Mie terms (Wiscombe 1980 Eq. 3) ────────────────
        int nStop = (int)(x + 4.0 * Math.Pow(x, 1.0 / 3.0) + 2.0) + 1;

        // ── Step 2: Logarithmic derivative D_n(y) via downward recurrence ────
        //   D_n = n/y − 1/(D_{n+1} + n/y)   where y = m·x (complex)
        //   Initialise at n_max >> n_stop to avoid contamination.
        int nmx = (int)Math.Max(nStop + 16, xmAbs + 16) + 1;

        // Store real and imaginary parts of D_n separately
        double[] dRe = new double[nmx + 1];
        double[] dIm = new double[nmx + 1];
        // D[nmx] = 0 + 0i (downward initialisation)

        // Real and imaginary parts of the complex denominator y = m·x
        double yRe =  nr * x;
        double yIm = -ni * x;   // Convention: m = nr - i·ni, y = m·x = nr·x - i·ni·x

        for (int n = nmx - 1; n >= 1; n--)
        {
            double rn = (double)n;
            // dn_curr = D_n (what we want), dn_next = D_{n+1} (already computed)
            // tmp = D_{n+1} + n/y
            double tmpRe = dRe[n + 1] + rn * yRe / (yRe * yRe + yIm * yIm);
            double tmpIm = dIm[n + 1] - rn * yIm / (yRe * yRe + yIm * yIm);

            // n/y = n * conj(y) / |y|²
            double ny_denom = yRe * yRe + yIm * yIm;
            double nyRe = rn * yRe / ny_denom;
            double nyIm = rn * (-yIm) / ny_denom;

            // D_n = n/y − 1/tmp
            double tmpMag2 = tmpRe * tmpRe + tmpIm * tmpIm;
            if (tmpMag2 < 1e-300) tmpMag2 = 1e-300;
            dRe[n] = nyRe - tmpRe / tmpMag2;
            dIm[n] = nyIm + tmpIm / tmpMag2;
        }

        // ── Step 3: Upward recurrence for Riccati-Bessel ψ_n and χ_n ─────────
        //   ψ_{-1} = cos(x),  ψ_0 = sin(x)
        //   χ_{-1} = -sin(x), χ_0 =  cos(x)
        //   ψ_n = (2n-1)/x · ψ_{n-1} - ψ_{n-2}
        //   χ_n = (2n-1)/x · χ_{n-1} - χ_{n-2}
        //   ξ_n = ψ_n - i·χ_n

        double psiPrev = Math.Cos(x);   // ψ_{-1}
        double psiCurr = Math.Sin(x);   // ψ_0
        double chiPrev = -Math.Sin(x);  // χ_{-1}
        double chiCurr =  Math.Cos(x);  // χ_0

        double qext = 0.0, qsca = 0.0, gSum = 0.0;
        double anPrevRe = 0.0, anPrevIm = 0.0;   // a_{n-1}
        double bnPrevRe = 0.0, bnPrevIm = 0.0;   // b_{n-1}

        for (int n = 1; n <= nStop; n++)
        {
            double rn = (double)n;
            double fn = (2.0 * rn - 1.0) / x;

            // Recur ψ_n and χ_n
            double psiNext = fn * psiCurr - psiPrev;
            double chiNext = fn * chiCurr - chiPrev;

            // ξ_n = ψ_n - i·χ_n
            double xiRe = psiNext;
            double xiIm = -chiNext;
            double xiPrevRe = psiCurr;
            double xiPrevIm = -chiCurr;

            // ── Compute a_n ──────────────────────────────────────────────────
            //   Numerator:   (D_n/m + n/x)·ψ_n - ψ_{n-1}
            //   Denominator: (D_n/m + n/x)·ξ_n - ξ_{n-1}
            //   D_n/m = D_n × conj(m) / |m|²
            double mMag2 = nr * nr + ni * ni;
            // D_n/m (complex divide)
            double dnmRe = (dRe[n] * nr - dIm[n] * (-ni)) / mMag2;
            double dnmIm = (dIm[n] * nr + dRe[n] * (-ni)) / mMag2;

            double anNumRe = (dnmRe + rn / x) * psiNext - psiCurr;
            double anNumIm =  dnmIm            * psiNext;
            double anDenRe = (dnmRe + rn / x) * xiRe - dnmIm * xiIm - xiPrevRe;
            double anDenIm = (dnmRe + rn / x) * xiIm + dnmIm * xiRe - xiPrevIm;
            double anDenMag2 = anDenRe * anDenRe + anDenIm * anDenIm;
            if (anDenMag2 < 1e-300) anDenMag2 = 1e-300;
            double anRe = (anNumRe * anDenRe + anNumIm * anDenIm) / anDenMag2;
            double anIm = (anNumIm * anDenRe - anNumRe * anDenIm) / anDenMag2;

            // ── Compute b_n ──────────────────────────────────────────────────
            //   Numerator:   (m·D_n + n/x)·ψ_n - ψ_{n-1}
            //   Denominator: (m·D_n + n/x)·ξ_n - ξ_{n-1}
            double mdn_Re = nr * dRe[n] - (-ni) * dIm[n];   // Re(m·D_n)
            double mdn_Im = nr * dIm[n] + (-ni) * dRe[n];   // Im(m·D_n)   [m = nr - i ni]

            double bnNumRe = (mdn_Re + rn / x) * psiNext - psiCurr;
            double bnNumIm =  mdn_Im            * psiNext;
            double bnDenRe = (mdn_Re + rn / x) * xiRe - mdn_Im * xiIm - xiPrevRe;
            double bnDenIm = (mdn_Re + rn / x) * xiIm + mdn_Im * xiRe - xiPrevIm;
            double bnDenMag2 = bnDenRe * bnDenRe + bnDenIm * bnDenIm;
            if (bnDenMag2 < 1e-300) bnDenMag2 = 1e-300;
            double bnRe = (bnNumRe * bnDenRe + bnNumIm * bnDenIm) / bnDenMag2;
            double bnIm = (bnNumIm * bnDenRe - bnNumRe * bnDenIm) / bnDenMag2;

            // ── Accumulate efficiency factors ────────────────────────────────
            double coeff = 2.0 * rn + 1.0;
            qext += coeff * (anRe + bnRe);
            qsca += coeff * (anRe * anRe + anIm * anIm + bnRe * bnRe + bnIm * bnIm);

            // g asymmetry: consecutive-term part (Bohren & Huffman Eq. 4.70)
            // At loop iteration n we have a_{n-1} (prev) and a_n (current).
            // The cross term for index k=n-1 is: k(k+2)/(k+1) × Re[a_k a*_{k+1} + b_k b*_{k+1}]
            //   = (n-1)(n+1)/n × Re[a_{n-1} a*_n + b_{n-1} b*_n]
            if (n > 1)
            {
                double nm1 = rn - 1.0;
                double crossCoeff = nm1 * (nm1 + 2.0) / (nm1 + 1.0);  // (n-1)(n+1)/n
                double cross1 = anPrevRe * anRe + anPrevIm * anIm
                              + bnPrevRe * bnRe + bnPrevIm * bnIm;
                gSum += crossCoeff * cross1;
            }
            // Single-term part: (2n+1)/(n(n+1)) × Re(a_n·b_n*)
            gSum += coeff / (rn * (rn + 1.0))
                  * (anRe * bnRe + anIm * bnIm);

            // Advance recurrences
            psiPrev = psiCurr; psiCurr = psiNext;
            chiPrev = chiCurr; chiCurr = chiNext;
            anPrevRe = anRe; anPrevIm = anIm;
            bnPrevRe = bnRe; bnPrevIm = bnIm;
        }

        double factor = 2.0 / (x * x);
        qext *= factor;
        qsca *= factor;
        double g = (qsca > 1e-30)
            ? 4.0 / (x * x * qsca) * gSum
            : 0.0;
        g = Math.Clamp(g, -1.0, 1.0);
        // Ensure physical bounds
        qsca = Math.Max(0, qsca);
        qext = Math.Max(qsca, qext);

        return new MieResult { Qext = qext, Qsca = qsca, AsymmetryG = g };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Linearly interpolate the complex refractive index of liquid water
    /// using the Segelstein (1981) tabulation.
    /// </summary>
    private static (double nr, double ni) InterpolateWaterRefIndex(double wl_um)
    {
        int n = _wlTable.Length;

        if (wl_um <= _wlTable[0])
            return (_nrTable[0], _niTable[0]);
        if (wl_um >= _wlTable[n - 1])
            return (_nrTable[n - 1], _niTable[n - 1]);

        // Binary search for bracket
        int lo = 0, hi = n - 2;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_wlTable[mid + 1] <= wl_um) lo = mid + 1;
            else hi = mid;
        }

        double t = (wl_um - _wlTable[lo]) / (_wlTable[lo + 1] - _wlTable[lo]);
        double nr = _nrTable[lo] + t * (_nrTable[lo + 1] - _nrTable[lo]);
        double ni = _niTable[lo] + t * (_niTable[lo + 1] - _niTable[lo]);
        return (nr, Math.Max(0.0, ni));
    }
}
