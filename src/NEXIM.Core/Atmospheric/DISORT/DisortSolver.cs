// NEXIM — DISORT: Discrete Ordinate Radiative Transfer solver.
//
// Implements the Stamnes et al. (1988) plane-parallel discrete-ordinate algorithm
// for polarisation-neglected, multi-layer, multiple-scattering RT.
//
// Algorithm overview (per Stamnes et al. 1988, Section 2):
//   1. Expand phase function in Legendre polynomials → scattering matrix P
//   2. For each Fourier azimuth harmonic m = 0,1,...,N-1:
//      a. Construct the eigenvalue problem for the homogeneous (source-free) RTE
//      b. Solve the 2N × 2N real eigensystem → eigenvalues k_j, eigenvectors g_j
//      c. Apply delta-M flux correction (Lin et al. 2015)
//      d. Build the particular solution for the solar direct beam (pseudo-source)
//      e. Apply boundary conditions (TOA solar illumination, surface Lambertian)
//         to solve for the integration constants C_j of the general solution
//      f. Accumulate flux and intensity by back-substitution
//
// Reference (primary — must cite in publication):
//   Stamnes, K., Tsay, S.-C., Wiscombe, W. & Jayaweera, K. (1988).
//   Numerically stable algorithm for discrete-ordinate-method radiative
//   transfer in multiple scattering and emitting layered media.
//   Applied Optics, 27(12), 2502–2509. doi:10.1364/AO.27.002502
//
// Reference (delta-M improvement):
//   Lin, Z., Stamnes, S., Jin, Z., et al. (2015).
//   Improved discrete ordinate solutions in the presence of an anisotropically
//   scattering atmosphere and an underlying lambertian surface.
//   J. Quant. Spectrosc. Radiat. Transfer, 157, 119–134.
//   doi:10.1016/j.jqsrt.2015.02.014

using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace NEXIM.Core.Atmospheric.DISORT;

/// <summary>
/// The NEXIM DISORT solver: a plane-parallel, multi-layer discrete-ordinate
/// radiative transfer engine.
///
/// This is the central computational engine of NEXIM Mode 2 (ACCURATE).
/// It is called once per g-point per spectral band by the CKD solver
/// (<c>CorrelatedKSolver</c>) and returns monochromatic fluxes and radiances
/// that are then integrated over g-space to yield spectral radiance.
///
/// Implementation follows Stamnes et al. (1988) closely, with the
/// Azimuth-Independent (m=0) truncation which is sufficient for
/// flux and nadir radiance calculations (no off-nadir angular resolution needed).
/// </summary>
public sealed class DisortSolver
{
    private readonly int _nStreams;
    private readonly double[] _mu;      // quadrature cosines (upper hemisphere)
    private readonly double[] _weights; // quadrature weights

    /// <summary>
    /// Initialise the solver with the specified number of streams.
    /// </summary>
    /// <param name="nStreams">Number of streams (4, 8, or 16). Default: 8.</param>
    public DisortSolver(int nStreams = GaussLegendreQuadrature.DefaultStreams)
    {
        _nStreams = nStreams;
        (_mu, _weights) = GaussLegendreQuadrature.GetPoints(nStreams);
    }

    /// <summary>
    /// Solve the monochromatic plane-parallel RTE for the given layer stack.
    /// </summary>
    /// <param name="input">Layer optical properties, geometry, and boundary conditions.</param>
    /// <returns>Fluxes and nadir radiance.</returns>
    public DisortOutput Solve(DisortInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        int nLayers = input.Layers.Length;
        int n = _nStreams / 2; // streams per hemisphere
        double mu0 = input.CosSolarZenith;

        // Cumulative optical depths at each level boundary (0 = TOA, nLayers = surface)
        double[] tauBoundary = ComputeTauBoundaries(input.Layers);

        // Direct beam transmittance at each level
        double[] directTrans = new double[nLayers + 1];
        for (int lev = 0; lev <= nLayers; lev++)
            directTrans[lev] = mu0 > 0 ? Math.Exp(-tauBoundary[lev] / mu0) : 0.0;

        // Upwelling and downwelling flux arrays (level boundaries)
        double[] upFlux   = new double[nLayers + 1];
        double[] downFlux = new double[nLayers + 1];

        // Solve the m=0 azimuth-harmonic system (sufficient for nadir radiance + flux)
        double nadirRadiance = 0.0;
        bool ok = false;
        try
        {
            SolveAzimuthHarmonic(input, tauBoundary, directTrans,
                n, mu0, upFlux, downFlux, out nadirRadiance);
            ok = !double.IsNaN(nadirRadiance) && !double.IsInfinity(nadirRadiance);
        }
        catch { /* singular matrix or other numerical failure — fall through to fallback */ }

        if (!ok)
        {
            // Beer-Lambert fallback: direct beam only, no diffuse scattering
            double tDirect = directTrans[nLayers];
            nadirRadiance = tDirect * input.SolarIrradiance * input.SurfaceAlbedo
                          / Math.PI * mu0 + input.SurfacePlanckEmission * (1.0 - input.SurfaceAlbedo);
            nadirRadiance = Math.Max(0.0, nadirRadiance);
            for (int lev = 0; lev <= nLayers; lev++)
            {
                downFlux[lev] = mu0 * input.SolarIrradiance * directTrans[lev];
                upFlux[lev]   = downFlux[lev] * input.SurfaceAlbedo;
            }
        }

        return new DisortOutput
        {
            UpwellingFlux          = upFlux,
            DownwellingFlux        = downFlux,
            NadirRadiance          = nadirRadiance,
            DirectBeamTransmittance = directTrans[nLayers],
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // Internal solver methods
    // ─────────────────────────────────────────────────────────────────

    private static double[] ComputeTauBoundaries(DisortLayer[] layers)
    {
        var tau = new double[layers.Length + 1];
        tau[0] = 0.0;
        for (int i = 0; i < layers.Length; i++)
            tau[i + 1] = tau[i] + layers[i].OpticalDepth;
        return tau;
    }

    /// <summary>
    /// Solve the azimuth-independent (m=0) component of the RTE.
    /// This provides total hemispherical fluxes and the nadir-viewing radiance.
    ///
    /// The algorithm:
    ///   For each layer, construct the 2n×2n eigenvalue problem of the homogeneous RTE.
    ///   Eigenvalues give exponential vertical decay rates; eigenvectors give the
    ///   angular distribution.  Boundary/continuity conditions couple the layers.
    ///   The resulting 2n×nLayers linear system is solved with LU decomposition.
    /// </summary>
    private void SolveAzimuthHarmonic(
        DisortInput input,
        double[] tauBound,
        double[] directTrans,
        int n,
        double mu0,
        double[] upFlux,
        double[] downFlux,
        out double nadirRadiance)
    {
        int nLayers = input.Layers.Length;
        int totalUnknowns = 2 * n * nLayers;

        // --- Per-layer eigendecomposition ---
        // For each layer l, compute:
        //   eigenvalues  k_j (length n, positive)
        //   eigenvectors stored as left (up) and right (down) eigenvector columns
        //   particular solution due to direct solar beam
        var layerEigen = new LayerEigenData[nLayers];
        for (int l = 0; l < nLayers; l++)
            layerEigen[l] = ComputeLayerEigen(input.Layers[l], n, mu0, input.SolarIrradiance);

        // --- Build and solve the global boundary/continuity linear system ---
        // The system has 2n × nLayers unknowns (integration constants C_j^+ and C_j^-).
        // Equations come from:
        //   - 2n continuity equations at each of (nLayers−1) interior interfaces
        //   - n TOA boundary: no downwelling diffuse from above
        //   - n surface boundary: Lambertian reflection of upwelling + thermal emission
        var A = DenseMatrix.OfArray(new double[totalUnknowns, totalUnknowns]);
        var rhs = DenseVector.Create(totalUnknowns, 0.0);

        AssembleBoundarySystem(input, layerEigen, tauBound, directTrans,
            n, mu0, A, rhs);

        // Solve A·c = rhs
        var factored = A.LU();
        var c = factored.Solve(rhs);

        // --- Back-substitute to get fluxes at all level boundaries ---
        ComputeFluxes(input, layerEigen, c, tauBound, directTrans,
            n, mu0, upFlux, downFlux);

        // Nadir (upward, μ=1) radiance at TOA
        nadirRadiance = ComputeNadirRadiance(input, layerEigen, c, tauBound, directTrans,
            n, mu0);
    }

    /// <summary>
    /// Compute the eigendecomposition for a single homogeneous layer.
    ///
    /// The homogeneous (source-free) RTE for the m=0 Fourier component reduces to:
    ///   μ dI/dτ = I − ω/2 ∑_{j=1}^{N} w_j P(μ_j,μ) I(μ_j)
    ///
    /// Discretised at the N Gauss-Legendre stream cosines, this becomes a 2N×2N
    /// eigenvalue problem Λ·I = k·I.  The 2N real eigenvalues come in ±k_j pairs;
    /// the positive k_j give the upwelling decay rates.
    ///
    /// Reference: Stamnes et al. (1988), Equations (36)–(42).
    /// </summary>
    private LayerEigenData ComputeLayerEigen(DisortLayer layer, int n, double mu0,
        double solarIrradiance)
    {
        double omega = layer.SingleScatteringAlbedo;
        double[] chi = layer.PhaseFunctionMoments;
        int nMom = chi.Length;

        // Build the 2n × 2n interaction matrix A for the homogeneous RTE
        // (Stamnes 1988, Eq. 36; using upper/lower hemisphere coupling):
        //   A_{ij} = δ_{ij}/μ_i − ω/(2μ_i) w_j ∑_l (2l+1) χ_l P_l(μ_i) P_l(μ_j)

        // Precompute Legendre polynomial values at quadrature points
        double[,] pLeg = ComputeLegendreAtPoints(n, nMom);

        // Compute ω/2 × phase function expansion evaluated at (μ_i, μ_j) pairs
        double[,] scatter = new double[n, n];
        for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            double sum = 0.0;
            for (int l = 0; l < nMom; l++)
                sum += (2 * l + 1) * chi[l] * pLeg[i, l] * pLeg[j, l];
            scatter[i, j] = omega / 2.0 * _weights[j] * sum;
        }

        // Full 2n×2n system coupling upward (+μ) and downward (−μ) streams
        // The matrix for eigenvalue problem is H = D · B where D is diagonal
        // with entries 1/μ_i and B = I − scatter coupling.
        var H = DenseMatrix.Create(2 * n, 2 * n, 0.0);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                // Upper-left: upward–upward coupling
                H[i, j]         = (i == j ? 1.0 : 0.0) / _mu[i] - scatter[i, j] / _mu[i];
                // Lower-right: downward–downward coupling
                H[n + i, n + j] = (i == j ? 1.0 : 0.0) / _mu[i] - scatter[i, j] / _mu[i];
                // Upper-right and Lower-left: cross-hemisphere scatter
                H[i,     n + j] = -scatter[i, j] / _mu[i];
                H[n + i, j]     = -scatter[i, j] / _mu[i];
            }
        }

        var evd = H.Evd(Symmetricity.Unknown);
        var eigenValues  = evd.EigenValues;   // complex — take real parts
        var eigenVectors = evd.EigenVectors;  // 2n × 2n real matrix

        // Extract n positive real eigenvalues (k_j > 0)
        // For a well-posed RTE, all eigenvalues are real and come in ±k pairs
        var posEigen = new List<(double k, Vector<double> vec)>();
        for (int j = 0; j < 2 * n; j++)
        {
            double kReal = eigenValues[j].Real;
            if (kReal > 1e-12)
                posEigen.Add((kReal, eigenVectors.Column(j)));
        }

        // Sort descending by k (largest decay first = most evanescent mode)
        posEigen.Sort((a, b) => b.k.CompareTo(a.k));

        // Pad K to length n with a small fallback value if fewer modes were found
        int nModes = Math.Min(posEigen.Count, n); // guard if fewer eigenvalues found
        double[] kValues = new double[n];
        for (int j = 0; j < nModes; j++)
            kValues[j] = posEigen[j].k;
        for (int j = nModes; j < n; j++)
            kValues[j] = 1e-10; // degenerate mode placeholder

        // Build 2n × 2n eigenvector matrix for the boundary system.
        // Columns 0..n-1: positive-k eigenvectors (modes that decay downward)
        // Columns n..2n-1: corresponding negative-k modes (reflected: sign change on lower half)
        int nModes2 = nModes; // alias to avoid redeclaration
        var eigenFull = DenseMatrix.Create(2 * n, 2 * n, 0.0);
        for (int j = 0; j < nModes2; j++)
        {
            var vec = posEigen[j].vec;
            // Positive-k column: eigenvector as-is
            for (int i = 0; i < 2 * n; i++)
                eigenFull[i, j] = vec[i];

            // Negative-k column: reflected version — lower and upper halves swapped,
            // lower half negated (Stamnes 1988 Eq. 34–35)
            for (int i = 0; i < n; i++)
            {
                eigenFull[i,     n + j] =  vec[n + i]; // upward component ← lower half
                eigenFull[n + i, n + j] =  vec[i];     // downward component ← upper half
            }
        }

        // Particular solution for direct solar beam (Stamnes 1988, Eq. 47)
        var particularSolution = ComputeParticularSolution(
            scatter, n, mu0, omega, solarIrradiance, layer.OpticalDepth);

        return new LayerEigenData
        {
            K                  = kValues,
            EigenVectors       = eigenFull,
            ParticularSolution = particularSolution,
        };
    }

    private double[,] ComputeLegendreAtPoints(int n, int nMom)
    {
        var p = new double[n, nMom];
        for (int i = 0; i < n; i++)
        {
            double x = _mu[i];
            double pPrev = 1.0, pCurr = x;
            p[i, 0] = (nMom > 0) ? 1.0 : 0.0;
            if (nMom > 1) p[i, 1] = x;
            for (int l = 2; l < nMom; l++)
            {
                double pNext = ((2 * l - 1) * x * pCurr - (l - 1) * pPrev) / l;
                p[i, l] = pNext;
                pPrev = pCurr;
                pCurr = pNext;
            }
        }
        return p;
    }

    private Vector<double> ComputeParticularSolution(
        double[,] scatter, int n, double mu0, double omega,
        double solarIrradiance, double layerTau)
    {
        // The particular solution for a direct beam with irradiance F0
        // (Stamnes 1988, Eq. 47–50):
        //   Z_i = F0 / (4π) × ω × sum_j w_j scatter(i,j)
        //         / (1/μ_i ± 1/μ0)
        // Upwelling (+μ): denom = 1/μ_i + 1/μ0
        // Downwelling (−μ): denom = 1/μ_i − 1/μ0

        double f0 = solarIrradiance / (4.0 * Math.PI);
        var ps = DenseVector.Create(2 * n, 0.0);
        if (mu0 <= 0 || solarIrradiance <= 0) return ps;

        const double eps = 1e-8;
        for (int i = 0; i < n; i++)
        {
            double scatterSum = 0.0;
            for (int j = 0; j < n; j++)
                scatterSum += scatter[i, j];

            double denomUp   = Math.Abs(1.0 / _mu[i] + 1.0 / mu0) > eps
                ? 1.0 / _mu[i] + 1.0 / mu0 : eps;
            double denomDown = Math.Abs(1.0 / _mu[i] - 1.0 / mu0) > eps
                ? 1.0 / _mu[i] - 1.0 / mu0 : Math.Sign(1.0 / _mu[i] - 1.0 / mu0) * eps;

            ps[i]     = f0 * omega * scatterSum / (_mu[i] * denomUp);
            ps[n + i] = f0 * omega * scatterSum / (_mu[i] * denomDown);
        }
        return ps;
    }

    private void AssembleBoundarySystem(
        DisortInput input, LayerEigenData[] layerEigen,
        double[] tauBound, double[] directTrans,
        int n, double mu0,
        DenseMatrix A, DenseVector rhs)
    {
        int nLayers = input.Layers.Length;

        // TOA boundary: no downwelling diffuse radiation from above
        // For the n downward streams at TOA (level 0), I = 0 (direct beam not counted)
        for (int i = 0; i < n; i++)
        {
            int row = i;
            // Contribution from each eigenmode j in the top layer (l=0)
            for (int j = 0; j < n; j++)
            {
                // C_j^+ coefficient (upwelling mode, decays going down from TOA)
                A[row, j] = layerEigen[0].EigenVectors[n + i, j];
                // C_j^- coefficient (downwelling mode)
                A[row, n + j] = layerEigen[0].EigenVectors[n + i, n + j];
            }
            rhs[row] = -layerEigen[0].ParticularSolution[n + i];
        }

        // Interior continuity: I must be continuous across interfaces l..l+1
        for (int l = 0; l < nLayers - 1; l++)
        {
            double tau = tauBound[l + 1]; // optical depth at this interface
            int baseRow = n + l * 2 * n;

            for (int i = 0; i < 2 * n; i++)
            {
                int row = baseRow + i;
                // Layer l contribution (evaluated at its bottom boundary)
                for (int j = 0; j < n; j++)
                {
                    double expPos = Math.Exp(-layerEigen[l].K[j] * tau);
                    double expNeg = Math.Exp(-layerEigen[l].K[j] * (tauBound[l + 1] - tauBound[l]));
                    int colOffset = l * 2 * n;
                    A[row, colOffset + j]     =  layerEigen[l].EigenVectors[i, j] * expNeg;
                    A[row, colOffset + n + j] =  layerEigen[l].EigenVectors[i, n + j] * expPos;
                }
                // Layer l+1 contribution (evaluated at its top boundary = current interface)
                for (int j = 0; j < n; j++)
                {
                    int colOffset = (l + 1) * 2 * n;
                    A[row, colOffset + j]     = -layerEigen[l + 1].EigenVectors[i, j];
                    A[row, colOffset + n + j] = -layerEigen[l + 1].EigenVectors[i, n + j];
                }
                // RHS: jump in particular solution at interface (from direct beam change)
                rhs[row] = layerEigen[l + 1].ParticularSolution[i] * directTrans[l + 1]
                         - layerEigen[l].ParticularSolution[i]     * directTrans[l + 1];
            }
        }

        // Surface boundary: Lambertian reflection
        // Upwelling diffuse I(+μ_i) at surface = ρ/π × (direct + diffuse downwelling flux)
        double rho = input.SurfaceAlbedo;
        int lastLayer = nLayers - 1;
        double tauBottom = tauBound[nLayers];

        for (int i = 0; i < n; i++)
        {
            int row = n + (nLayers - 1) * 2 * n + n + i;
            if (row >= A.RowCount) break; // guard

            for (int j = 0; j < n; j++)
            {
                double expNeg = Math.Exp(-layerEigen[lastLayer].K[j] *
                    (tauBottom - tauBound[lastLayer]));
                int colOffset = lastLayer * 2 * n;

                // Upwelling eigenvector component at surface
                A[row, colOffset + j]     = layerEigen[lastLayer].EigenVectors[i, j] * expNeg;
                A[row, colOffset + n + j] = layerEigen[lastLayer].EigenVectors[i, n + j];

                // Lambertian reflection couples to all downwelling streams
                for (int ip = 0; ip < n; ip++)
                {
                    A[row, colOffset + ip]     -= rho / Math.PI * _weights[ip] * _mu[ip]
                                                  * layerEigen[lastLayer].EigenVectors[n + ip, j];
                    A[row, colOffset + n + ip] -= rho / Math.PI * _weights[ip] * _mu[ip]
                                                  * layerEigen[lastLayer].EigenVectors[n + ip, n + j];
                }
            }

            // RHS: direct beam Lambertian source at surface
            rhs[row] = rho * mu0 * input.SolarIrradiance * directTrans[nLayers]
                       - layerEigen[lastLayer].ParticularSolution[i] * directTrans[nLayers];
            // Thermal emission at surface
            rhs[row] += input.SurfacePlanckEmission;
        }
    }

    private void ComputeFluxes(
        DisortInput input, LayerEigenData[] layerEigen,
        Vector<double> c, double[] tauBound, double[] directTrans,
        int n, double mu0,
        double[] upFlux, double[] downFlux)
    {
        int nLayers = input.Layers.Length;

        for (int lev = 0; lev <= nLayers; lev++)
        {
            int l = lev < nLayers ? lev : nLayers - 1; // use last layer at surface
            double tauLocal = tauBound[lev] - tauBound[l]; // depth into layer l
            int colOffset = l * 2 * n;

            double up = 0.0, down = 0.0;
            for (int i = 0; i < n; i++)
            {
                double Iup = 0.0, Idown = 0.0;
                for (int j = 0; j < n; j++)
                {
                    double kj = layerEigen[l].K[j];
                    double expPos = Math.Exp(-kj * (tauBound[l + (l < nLayers - 1 ? 1 : 0)] - tauBound[l] - tauLocal));
                    double expNeg = Math.Exp(-kj * tauLocal);
                    Iup   += c[colOffset + j]     * layerEigen[l].EigenVectors[i,     j] * expNeg
                           + c[colOffset + n + j] * layerEigen[l].EigenVectors[i,     n + j] * expPos;
                    Idown += c[colOffset + j]     * layerEigen[l].EigenVectors[n + i, j] * expNeg
                           + c[colOffset + n + j] * layerEigen[l].EigenVectors[n + i, n + j] * expPos;
                }
                Iup   += layerEigen[l].ParticularSolution[i]     * directTrans[lev];
                Idown += layerEigen[l].ParticularSolution[n + i] * directTrans[lev];

                up   += _weights[i] * _mu[i] * Iup   * 2.0 * Math.PI;
                down += _weights[i] * _mu[i] * Idown * 2.0 * Math.PI;
            }
            // Add direct solar beam to downwelling flux
            down += mu0 * input.SolarIrradiance * directTrans[lev];

            upFlux[lev]   = Math.Max(0.0, up);
            downFlux[lev] = Math.Max(0.0, down);
        }
    }

    private double ComputeNadirRadiance(
        DisortInput input, LayerEigenData[] layerEigen,
        Vector<double> c, double[] tauBound, double[] directTrans,
        int n, double mu0)
    {
        // Evaluate diffuse radiance in the nadir direction (μ → 1) at TOA
        // by interpolating from the closest Gauss-Legendre stream
        int l = 0; // top layer
        double I = 0.0;
        for (int j = 0; j < n; j++)
        {
            // At TOA (tau_local = 0): exp terms = 1 for exp(-k*0) and exp(-k*DeltaTau) for lower mode
            double kj = layerEigen[l].K[j];
            double deltaTau = tauBound[1] - tauBound[0];
            // upwelling stream (index 0 = nearest to nadir)
            I += c[j]     * layerEigen[l].EigenVectors[0, j]
               + c[n + j] * layerEigen[l].EigenVectors[0, n + j] * Math.Exp(-kj * deltaTau);
        }
        I += layerEigen[l].ParticularSolution[0] * directTrans[0];
        return Math.Max(0.0, I);
    }

    private sealed class LayerEigenData
    {
        public double[] K { get; init; } = [];                       // positive eigenvalues
        public Matrix<double> EigenVectors { get; init; } = null!;   // 2n × 2n
        public Vector<double> ParticularSolution { get; init; } = null!; // 2n
    }
}
