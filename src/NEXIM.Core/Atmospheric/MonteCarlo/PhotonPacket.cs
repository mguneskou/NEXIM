// NEXIM — Monte Carlo photon packet state.
// Tracks position, direction cosines, statistical weight and lifecycle
// of a single photon propagating through the atmospheric volume.

namespace NEXIM.Core.Atmospheric.MonteCarlo;

/// <summary>
/// Lifetime status of a Monte Carlo photon packet.
/// </summary>
public enum PhotonStatus
{
    /// <summary>Photon is still propagating.</summary>
    Alive = 0,

    /// <summary>Photon has escaped through the top-of-atmosphere boundary
    /// in an upward direction (contributes to sensor radiance).</summary>
    Escaped = 1,

    /// <summary>Photon weight has been driven to zero by absorption or Russian roulette.</summary>
    Absorbed = 2,
}

/// <summary>
/// State of a single Monte Carlo photon packet.
///
/// Uses right-hand coordinates with Z pointing upward:
///   X = cross-track horizontal (km)
///   Y = along-track horizontal (km)
///   Z = altitude above MSL (km)
///
/// Direction cosines (U, V, W) satisfy U² + V² + W² = 1.
/// W > 0 means the photon travels upward; W &lt; 0 downward.
/// </summary>
public struct PhotonPacket
{
    // ── Position ────────────────────────────────────────────────────────────
    /// <summary>X position in km (cross-track horizontal).</summary>
    public double X;

    /// <summary>Y position in km (along-track horizontal).</summary>
    public double Y;

    /// <summary>Z altitude in km above MSL.</summary>
    public double Z;

    // ── Direction cosines ───────────────────────────────────────────────────
    /// <summary>Direction cosine along X.</summary>
    public double U;

    /// <summary>Direction cosine along Y.</summary>
    public double V;

    /// <summary>Direction cosine along Z (positive = upward).</summary>
    public double W;

    // ── Photon state ────────────────────────────────────────────────────────
    /// <summary>
    /// Statistical weight in [0, 1]. Initialised to 1; reduced by absorption events.
    /// Russian roulette is applied when weight drops below 1×10⁻³.
    /// </summary>
    public double Weight;

    /// <summary>Wavelength of this photon packet in µm.</summary>
    public double Wavelength_um;

    /// <summary>Current lifecycle status.</summary>
    public PhotonStatus Status;

    /// <summary>
    /// Number of scattering events undergone. Hard cap = 500 to prevent runaway loops.
    /// </summary>
    public int ScatterCount;

    // ── Convenience ─────────────────────────────────────────────────────────
    /// <summary>Returns <c>true</c> while the photon has not reached a terminal state.</summary>
    public readonly bool IsAlive => Status == PhotonStatus.Alive;

    // ── Factory ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Initialise a downward-travelling photon at the top-of-atmosphere
    /// propagating along the solar beam direction.
    /// </summary>
    /// <param name="toaZ_km">TOA altitude in km.</param>
    /// <param name="wavelength_um">Photon wavelength in µm.</param>
    /// <param name="sza_deg">Solar zenith angle in degrees (0 = overhead sun).</param>
    /// <param name="saa_deg">Solar azimuth angle in degrees (clockwise from North).</param>
    public static PhotonPacket FromSolar(
        double toaZ_km, double wavelength_um,
        double sza_deg, double saa_deg)
    {
        double szaRad = sza_deg * Math.PI / 180.0;
        double saaRad = saa_deg * Math.PI / 180.0;
        double sinZ   = Math.Sin(szaRad);
        double cosZ   = Math.Cos(szaRad);

        return new PhotonPacket
        {
            X             = 0.0,
            Y             = 0.0,
            Z             = toaZ_km,
            U             =  sinZ * Math.Cos(saaRad),   // horizontal components
            V             =  sinZ * Math.Sin(saaRad),
            W             = -cosZ,                        // downward (negative Z)
            Weight        = 1.0,
            Wavelength_um = wavelength_um,
            Status        = PhotonStatus.Alive,
            ScatterCount  = 0,
        };
    }
}
