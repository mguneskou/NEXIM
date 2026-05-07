// NEXIM — Gauss-Legendre quadrature for DISORT.
//
// DISORT uses Gauss-Legendre quadrature to represent the angular (zenith)
// dependence of the radiation field.  The quadrature abscissae and weights
// are the roots and associated weights of the N-th Legendre polynomial.
//
// Reference:
//   Stamnes, K., Tsay, S.-C., Wiscombe, W. & Jayaweera, K. (1988).
//   Numerically stable algorithm for discrete-ordinate-method radiative
//   transfer in multiple scattering and emitting layered media.
//   Applied Optics, 27(12), 2502–2509. doi:10.1364/AO.27.002502

using MathNet.Numerics.LinearAlgebra;

namespace NEXIM.Core.Atmospheric.DISORT;

/// <summary>
/// Gauss-Legendre quadrature points and weights for N = 2, 4, 8, or 16 streams.
///
/// NEXIM defaults to 8 streams (4 abscissae per hemisphere), providing
/// MODTRAN-class accuracy (±0.5–1%) at ~100–500 ms per spectrum.
/// The N = 16 option (8 per hemisphere) is available for higher accuracy runs.
///
/// The abscissae μ are in [0,1] (cosines of zenith angle over the upper hemisphere).
/// The full 2N-stream system uses ±μ for both hemispheres.
/// </summary>
public static class GaussLegendreQuadrature
{
    /// <summary>Default stream count used by NEXIM Mode 2.</summary>
    public const int DefaultStreams = 8;

    /// <summary>
    /// Returns the Gauss-Legendre abscissae (μ > 0, upper hemisphere) and weights
    /// for a 2N-stream DISORT calculation (N abscissae per hemisphere).
    /// </summary>
    /// <param name="nStreams">
    /// Total number of streams (must be 4, 8, or 16).
    /// Half this number are the per-hemisphere quadrature points.
    /// </param>
    /// <returns>
    /// Tuple of (abscissae, weights) each of length nStreams/2,
    /// representing μ ∈ (0,1] and their Gauss-Legendre weights.
    /// </returns>
    public static (double[] Mu, double[] Weights) GetPoints(int nStreams = DefaultStreams)
    {
        int n = nStreams / 2; // per-hemisphere points
        if (n is not (2 or 4 or 8))
            throw new ArgumentException(
                $"nStreams must be 4, 8, or 16 (got {nStreams}). " +
                "NEXIM Mode 2 uses 8 streams (4 per hemisphere).", nameof(nStreams));

        // Use MathNet.Numerics Gauss-Legendre rule on [0, 1], map to cosine [0,1]
        var rule = new MathNet.Numerics.Integration.GaussLegendreRule(0.0, 1.0, n);
        double[] x = rule.Abscissas;
        double[] w = rule.Weights;

        // MathNet returns points in ascending order on [a,b]; we want descending μ
        // (largest cosine first, i.e. near-nadir first) to match DISORT convention.
        var mu = x.Reverse().ToArray();
        var weights = w.Reverse().ToArray();

        return (mu, weights);
    }

    /// <summary>
    /// Build the full 2N stream cosine array used in DISORT:
    /// [−μ_N, …, −μ_1, +μ_1, …, +μ_N] where negative = downward, positive = upward.
    /// </summary>
    public static double[] GetFullStreamCosines(int nStreams = DefaultStreams)
    {
        var (mu, _) = GetPoints(nStreams);
        int n = mu.Length;
        var full = new double[2 * n];
        for (int i = 0; i < n; i++)
        {
            full[i]         = -mu[n - 1 - i]; // downward, most oblique first
            full[n + i]     = mu[i];           // upward, least oblique first
        }
        return full;
    }
}
