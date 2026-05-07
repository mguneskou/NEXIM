// NEXIM — Cloud optical property model.
// Converts liquid water content (LWC) and effective droplet radius to
// bulk optical properties (extinction, SSA, asymmetry parameter) using
// Mie theory for liquid water spheres.
//
// The extinction-to-LWC conversion uses the standard cloud radiative
// transfer formula (e.g. Stephens 1978, J. Atmos. Sci. 35:2111):
//
//   σ_ext [km⁻¹] = (3/4) × Q_ext × LWC [g m⁻³]
//                   ──────────────────────────────
//                   ρ_w [g m⁻³] × r_eff [m]
//
// with ρ_w = 10⁶ g m⁻³ (density of liquid water).

namespace NEXIM.Core.Atmospheric.MonteCarlo;

/// <summary>
/// Cloud optical property model for a given wavelength.
///
/// Pre-computes Mie efficiency factors for liquid water droplets using
/// <see cref="MieCalculator"/> and provides fast look-up methods for bulk
/// cloud optical properties as a function of LWC and effective radius.
///
/// Instantiate once per wavelength; the constructor runs Mie calculations
/// at a small set of representative radii to enable rapid interpolation.
/// </summary>
public sealed class CloudModel
{
    // Water density in g/m³
    private const double RhoWater_gm3 = 1.0e6;

    // Pre-computed Mie tables for radius nodes 1–30 µm
    private static readonly double[] _rNodes =
        { 1.0, 2.0, 3.0, 5.0, 7.0, 10.0, 15.0, 20.0, 30.0 };

    private readonly double[] _qext;
    private readonly double[] _qsca;
    private readonly double[] _gArr;
    private readonly double   _wavelength_um;

    /// <summary>
    /// Build the cloud model for a given wavelength.
    /// </summary>
    /// <param name="wavelength_um">Wavelength in µm.</param>
    public CloudModel(double wavelength_um)
    {
        _wavelength_um = wavelength_um;
        int n = _rNodes.Length;
        _qext = new double[n];
        _qsca = new double[n];
        _gArr = new double[n];

        for (int i = 0; i < n; i++)
        {
            var mie = MieCalculator.ComputeForWater(_rNodes[i], wavelength_um);
            _qext[i] = mie.Qext;
            _qsca[i] = mie.Qsca;
            _gArr[i] = mie.AsymmetryG;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Bulk extinction coefficient in km⁻¹ for the given liquid water content
    /// and effective droplet radius.
    /// </summary>
    /// <param name="lwc_gm3">Liquid water content in g m⁻³.</param>
    /// <param name="rEff_um">Effective droplet radius in µm.</param>
    public double ExtinctionCoeff_km1(double lwc_gm3, double rEff_um)
    {
        if (lwc_gm3 <= 0.0) return 0.0;
        double qext = InterpolateQext(rEff_um);
        double r_m  = rEff_um * 1e-6;                  // µm → m
        double sigma_m1 = 0.75 * qext * lwc_gm3 / (RhoWater_gm3 * r_m);  // m⁻¹
        return sigma_m1 * 1000.0;                       // m⁻¹ → km⁻¹
    }

    /// <summary>
    /// Single-scatter albedo (SSA) for the given effective droplet radius.
    /// For liquid water clouds SSA is very close to 1 in the visible
    /// and drops at strongly absorbing mid-IR wavelengths.
    /// </summary>
    /// <param name="rEff_um">Effective droplet radius in µm.</param>
    public double Ssa(double rEff_um)
    {
        double qext = InterpolateQext(rEff_um);
        double qsca = InterpolateQsca(rEff_um);
        return (qext > 1e-30) ? Math.Clamp(qsca / qext, 0.0, 1.0) : 1.0;
    }

    /// <summary>
    /// Henyey-Greenstein asymmetry parameter g for the given effective radius.
    /// Typically 0.84–0.87 for visible wavelengths and cloud droplets.
    /// </summary>
    /// <param name="rEff_um">Effective droplet radius in µm.</param>
    public double AsymmetryG(double rEff_um) => InterpolateG(rEff_um);

    // ── Interpolation helpers ─────────────────────────────────────────────────

    private double InterpolateQext(double r)
    {
        int n = _rNodes.Length;
        if (r <= _rNodes[0])     return _qext[0];
        if (r >= _rNodes[n - 1]) return _qext[n - 1];
        int lo = BracketIndex(r);
        double t = (r - _rNodes[lo]) / (_rNodes[lo + 1] - _rNodes[lo]);
        return _qext[lo] + t * (_qext[lo + 1] - _qext[lo]);
    }

    private double InterpolateQsca(double r)
    {
        int n = _rNodes.Length;
        if (r <= _rNodes[0])     return _qsca[0];
        if (r >= _rNodes[n - 1]) return _qsca[n - 1];
        int lo = BracketIndex(r);
        double t = (r - _rNodes[lo]) / (_rNodes[lo + 1] - _rNodes[lo]);
        return _qsca[lo] + t * (_qsca[lo + 1] - _qsca[lo]);
    }

    private double InterpolateG(double r)
    {
        int n = _rNodes.Length;
        if (r <= _rNodes[0])     return _gArr[0];
        if (r >= _rNodes[n - 1]) return _gArr[n - 1];
        int lo = BracketIndex(r);
        double t = (r - _rNodes[lo]) / (_rNodes[lo + 1] - _rNodes[lo]);
        return _gArr[lo] + t * (_gArr[lo + 1] - _gArr[lo]);
    }

    private int BracketIndex(double r)
    {
        int lo = 0, hi = _rNodes.Length - 2;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_rNodes[mid + 1] <= r) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
