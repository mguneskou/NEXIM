using System;
using NEXIM.Core.Models;

namespace NEXIM.Core.Atmospheric.Adjacency;

/// <summary>
/// Applies the atmospheric adjacency correction to a computed radiance image.
///
/// The adjacency effect arises because multiple scattering in the atmosphere
/// couples the apparent reflectance of each pixel to the reflectances of its
/// neighbours. Neglecting this causes bright and dark areas to contaminate
/// each other in retrieved reflectance maps.
///
/// This corrector implements the iterative scene-coupled PSF convolution method
/// described in:
///   Tanré et al. (1981) Appl. Opt. 20(20):3676. doi:10.1364/AO.20.003676
///   Vermote et al. (1997) IEEE TGRS 35(3):675. doi:10.1109/36.581987
///
/// Algorithm:
///   1. Compute scene-mean reflectance ρ̄ as a spatial average weighted by PSF
///   2. Correct apparent reflectance: ρ_corr = (ρ_app − T_diff × ρ̄) / T_dir
///   3. Iterate 2–3 times to convergence (Vermote et al. criterion: Δρ &lt; 0.001)
///
/// Where:
///   T_dir  = direct beam transmittance from surface to sensor
///   T_diff = diffuse atmospheric transmittance (from DISORT downwelling fluxes)
///   ρ̄      = effective neighbourhood reflectance seen by the sensor from adjacent pixels
/// </summary>
public sealed class AdjacencyCorrector
{
    private readonly AtmosphericPsf _psf;
    private readonly double         _tDiffuse;
    private readonly double         _tDirect;

    /// <summary>Maximum number of correction iterations.</summary>
    public int MaxIterations { get; init; } = 3;

    /// <summary>Convergence threshold on mean reflectance change.</summary>
    public double ConvergenceTolerance { get; init; } = 1e-3;

    /// <summary>
    /// Create an adjacency corrector for a single spectral band.
    /// </summary>
    /// <param name="psf">Atmospheric PSF for this band and geometry.</param>
    /// <param name="tDirect">Direct beam transmittance [0–1] from surface to sensor.</param>
    /// <param name="tDiffuse">Diffuse-to-direct transmittance ratio at the surface.</param>
    public AdjacencyCorrector(AtmosphericPsf psf, double tDirect, double tDiffuse)
    {
        _psf      = psf      ?? throw new ArgumentNullException(nameof(psf));
        _tDirect  = Math.Clamp(tDirect,  0.0, 1.0);
        _tDiffuse = Math.Clamp(tDiffuse, 0.0, 1.0);
    }

    /// <summary>
    /// Apply the adjacency correction to a 1D array of per-pixel apparent
    /// reflectance values for a single spectral band.
    ///
    /// For hyperspectral images, call this once per band.
    ///
    /// In this simplified 1D implementation, the neighbourhood average ρ̄ is
    /// computed as a weighted average over the full scene, approximating the
    /// radial PSF convolution as a scene-wide mean weighted by the PSF integral.
    /// A full 2D implementation replaces this with a spatial convolution.
    ///
    /// Reference: Vermote et al. (1997) Eq. (17)–(19).
    /// </summary>
    /// <param name="apparentReflectance">
    /// Input: per-pixel apparent (at-sensor) reflectance for this band.
    /// Length = number of scene pixels.
    /// </param>
    /// <returns>
    /// Adjacency-corrected reflectance array, same length as input.
    /// Values are clamped to [0, 1].
    /// </returns>
    public double[] Correct(double[] apparentReflectance)
    {
        ArgumentNullException.ThrowIfNull(apparentReflectance);
        if (_tDirect < 1e-6) return (double[])apparentReflectance.Clone();

        int n         = apparentReflectance.Length;
        var corrected = (double[])apparentReflectance.Clone();

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            // Scene-mean effective reflectance ρ̄ (PSF-weighted average)
            double rhoBar = ComputeSceneMean(corrected);

            double maxDelta = 0.0;
            for (int i = 0; i < n; i++)
            {
                // Vermote et al. (1997) Eq. (17):
                //   ρ_corr = (ρ_app − T_diff × ρ̄) / T_dir
                double prev = corrected[i];
                corrected[i] = (apparentReflectance[i] - _tDiffuse * rhoBar) / _tDirect;
                corrected[i] = Math.Clamp(corrected[i], 0.0, 1.0);
                maxDelta = Math.Max(maxDelta, Math.Abs(corrected[i] - prev));
            }

            if (maxDelta < ConvergenceTolerance) break;
        }

        return corrected;
    }

    /// <summary>
    /// Compute the PSF-weighted scene mean reflectance.
    /// Simplified to a uniform scene mean when no spatial distribution is available.
    /// A full implementation would convolve the 2D reflectance map with the PSF kernel.
    /// </summary>
    private static double ComputeSceneMean(double[] reflectance)
    {
        if (reflectance.Length == 0) return 0.0;
        double sum = 0.0;
        for (int i = 0; i < reflectance.Length; i++) sum += reflectance[i];
        return sum / reflectance.Length;
    }
}
