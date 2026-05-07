// NEXIM — Standard atmosphere profiles.
// Six standard atmosphere profiles from:
//   Anderson et al. (1986) AFGL Atmospheric Constituent Profiles (0–120 km).
//   Air Force Geophysics Laboratory Technical Report AFGL-TR-86-0110, DTIC ADA175173.
// These are mandatory for reproducibility in any atmospheric RT code.

namespace NEXIM.Core.Models;

/// <summary>
/// Enumeration of the six standard atmosphere profiles defined in
/// Anderson et al. (1986) AFGL-TR-86-0110.
/// </summary>
public enum StandardAtmosphere
{
    /// <summary>U.S. Standard Atmosphere (1976). Mid-latitude annual mean.</summary>
    USStandard,

    /// <summary>Tropical (15°N annual mean). High water vapour, warm temperatures.</summary>
    Tropical,

    /// <summary>Midlatitude Summer (45°N July). Warm, moderate water vapour.</summary>
    MidlatitudeSummer,

    /// <summary>Midlatitude Winter (45°N January). Cold, dry conditions.</summary>
    MidlatitudeWinter,

    /// <summary>Subarctic Summer (60°N July). Cool, moderate water vapour.</summary>
    SubarcticSummer,

    /// <summary>Subarctic Winter (60°N January). Very cold and dry.</summary>
    SubarcticWinter,
}

/// <summary>
/// A single atmospheric layer defined by its altitude boundaries and
/// constituent concentrations.
/// </summary>
public sealed class AtmosphericLayer
{
    /// <summary>Altitude of the layer base in km above mean sea level.</summary>
    public double AltitudeBase_km { get; init; }

    /// <summary>Altitude of the layer top in km above mean sea level.</summary>
    public double AltitudeTop_km { get; init; }

    /// <summary>Mid-layer pressure in hPa (millibars).</summary>
    public double Pressure_hPa { get; init; }

    /// <summary>Mid-layer temperature in Kelvin.</summary>
    public double Temperature_K { get; init; }

    /// <summary>Water vapour column density in g/cm².</summary>
    public double H2O_g_cm2 { get; init; }

    /// <summary>Ozone column density in atm·cm.</summary>
    public double O3_atm_cm { get; init; }

    /// <summary>CO₂ volume mixing ratio (VMR), dimensionless. Default: 421 ppm (2024 value).</summary>
    public double CO2_VMR { get; init; } = 421e-6;

    /// <summary>CH₄ volume mixing ratio (VMR), dimensionless. Default: 1.9 ppm.</summary>
    public double CH4_VMR { get; init; } = 1.9e-6;
}

/// <summary>
/// Atmospheric profile used as input to all three RT modes.
/// Either selects a standard profile or specifies custom layer data.
/// </summary>
public sealed class AtmosphericProfile
{
    /// <summary>
    /// Standard atmosphere preset. <c>null</c> when using a custom layer profile.
    /// </summary>
    public StandardAtmosphere? Standard { get; private init; }

    /// <summary>
    /// Custom layer data. <c>null</c> when using a standard preset.
    /// Layers must be ordered from surface to top-of-atmosphere, ascending altitude.
    /// </summary>
    public AtmosphericLayer[]? CustomLayers { get; private init; }

    /// <summary>Ground elevation above mean sea level in km.</summary>
    public double GroundElevation_km { get; init; } = 0.0;

    private AtmosphericProfile() { }

    /// <summary>Create a profile using one of the six AFGL standard atmospheres.</summary>
    public static AtmosphericProfile FromStandard(StandardAtmosphere standard,
        double groundElevation_km = 0.0)
        => new() { Standard = standard, GroundElevation_km = groundElevation_km };

    /// <summary>Create a profile from user-specified layer data.</summary>
    /// <param name="layers">Layers ordered surface-to-TOA, ascending altitude.</param>
    public static AtmosphericProfile FromCustomLayers(AtmosphericLayer[] layers,
        double groundElevation_km = 0.0)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Length == 0)
            throw new ArgumentException("At least one atmospheric layer is required.", nameof(layers));
        return new() { CustomLayers = layers, GroundElevation_km = groundElevation_km };
    }

    /// <summary>
    /// Returns <c>true</c> if this profile uses a standard atmosphere preset.
    /// </summary>
    public bool IsStandard => Standard.HasValue;
}
