using System;
using System.IO;
using System.Text.Json;

namespace NEXIM.Core.Atmospheric.LUT;

/// <summary>
/// Describes the axis parameters of the pre-computed LUT grid.
/// One instance is stored as JSON in the LUT header.
///
/// LUT axes (order fixed for memory layout):
///   0: Solar Zenith Angle (SZA) [degrees]
///   1: View Zenith Angle  (VZA) [degrees]
///   2: Aerosol Optical Depth at 550 nm (AOD)
///   3: Water Vapour Column (WVC) [g/cm²]
///   4: Wavelength [µm]
/// </summary>
public sealed class LutFormat
{
    /// <summary>Version of the LUT binary format (current = 1).</summary>
    public int Version { get; init; } = 1;

    /// <summary>Magic identifier string for format validation.</summary>
    public string Magic { get; init; } = "NEXIM_LUT";

    /// <summary>Solar zenith angle grid nodes [degrees], ascending.</summary>
    public double[] SzaNodes { get; init; } = [];

    /// <summary>View zenith angle grid nodes [degrees], ascending.</summary>
    public double[] VzaNodes { get; init; } = [];

    /// <summary>Aerosol optical depth grid nodes at 550 nm, ascending.</summary>
    public double[] AodNodes { get; init; } = [];

    /// <summary>Water vapour column grid nodes [g/cm²], ascending.</summary>
    public double[] WvcNodes { get; init; } = [];

    /// <summary>Wavelength grid nodes [µm], ascending.</summary>
    public double[] WavelengthNodes { get; init; } = [];

    /// <summary>
    /// Number of data fields stored per grid point.
    /// Field order: [0] Transmittance, [1] PathRadiance, [2] DownwellingIrradiance_normalised
    /// </summary>
    public int NFields { get; init; } = 3;

    /// <summary>Total number of float32 values in the data block.</summary>
    public long TotalFloats => (long)SzaNodes.Length * VzaNodes.Length * AodNodes.Length
                                   * WvcNodes.Length * WavelengthNodes.Length * NFields;

    /// <summary>
    /// Compute the flat index into the LUT data array for a given grid position.
    /// Field index is the innermost (fastest) dimension.
    /// </summary>
    public long FlatIndex(int iSza, int iVza, int iAod, int iWvc, int iWl, int iField)
    {
        long nWl     = WavelengthNodes.Length;
        long nWvc    = WvcNodes.Length;
        long nAod    = AodNodes.Length;
        long nVza    = VzaNodes.Length;
        long nFields = NFields;

        return ((((long)iSza * nVza + iVza) * nAod + iAod) * nWvc + iWvc) * nWl * nFields
               + (long)iWl * nFields + iField;
    }
}
