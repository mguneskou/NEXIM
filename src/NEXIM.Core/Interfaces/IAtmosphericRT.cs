// NEXIM — Common interface for all atmospheric radiative transfer modes.
//
// Three implementations will be registered:
//   Mode 1 FAST      FastAtmosphericRT        <10 ms,      ±5–10%   pre-computed LUT
//   Mode 2 ACCURATE  AccurateAtmosphericRT    100–500 ms,  ±0.5–1%  CKD + DISORT 8-stream
//   Mode 3 FULL-PHYS MonteCarloAtmosphericRT  minutes,     ±0.1–0.5% Monte Carlo 3D + ILGPU

using NEXIM.Core.Models;

namespace NEXIM.Core.Interfaces;

/// <summary>
/// Contract that every atmospheric radiative transfer engine must satisfy.
/// All three modes (Fast LUT, Accurate CKD+DISORT, Full-Physics Monte Carlo)
/// implement this interface, allowing the simulation pipeline to swap engines
/// without changing downstream code.
/// </summary>
public interface IAtmosphericRT
{
    /// <summary>
    /// Human-readable name of this RT engine, used in log output and
    /// <see cref="RadianceResult.ModeName"/>.
    /// </summary>
    string ModeName { get; }

    /// <summary>
    /// Compute spectral upwelling radiance, path radiance, transmittance,
    /// and surface downwelling irradiance for the given scene configuration.
    /// </summary>
    /// <param name="input">Full description of the atmospheric state, geometry, surface reflectance.</param>
    /// <param name="cancellationToken">Allows long-running computations to be cancelled.</param>
    /// <returns>Spectral radiance components over the wavelength grid defined in <paramref name="input"/>.</returns>
    Task<RadianceResult> ComputeAsync(AtmosphericInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronous convenience wrapper around <see cref="ComputeAsync"/>.
    /// Prefer the async overload for long-running modes (Mode 2 and 3).
    /// </summary>
    RadianceResult Compute(AtmosphericInput input)
        => ComputeAsync(input, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Approximate compute time in milliseconds for the given input grid size,
    /// used by the UI to show progress estimates before starting a computation.
    /// Implementations may return 0 if unknown.
    /// </summary>
    double EstimateComputeTime_ms(AtmosphericInput input);
}
