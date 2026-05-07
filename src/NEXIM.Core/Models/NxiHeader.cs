// NEXIM — .nxi binary file format header.
// The .nxi format is NEXIM's native output format:
//   [Binary header: NxiHeader struct] [JSON metadata block: UTF-8, length-prefixed]
//   [float32 BIL interleaved hypercube data] [CRC32 footer]
// This is the single struct that lives at byte offset 0 of every .nxi file.

using System.Runtime.InteropServices;

namespace NEXIM.Core.Models;

/// <summary>
/// Fixed-size binary header at the start of every .nxi file.
/// All multi-byte fields are little-endian.
/// Total size: 64 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 64)]
public struct NxiHeader
{
    /// <summary>Magic bytes: 0x4E 0x58 0x49 0x00 ("NXI\0").</summary>
    public uint Magic;

    /// <summary>Format version. Current: 1.</summary>
    public ushort Version;

    /// <summary>Number of spatial columns (samples).</summary>
    public uint Columns;

    /// <summary>Number of spatial rows (lines).</summary>
    public uint Rows;

    /// <summary>Number of spectral bands.</summary>
    public uint Bands;

    /// <summary>Interleave order. 0 = BIL (Band Interleaved by Line), 1 = BSQ, 2 = BIP.</summary>
    public byte InterleaveOrder;

    /// <summary>Data type. 0 = float32. Reserved for future float64 (1) and uint16 (2).</summary>
    public byte DataType;

    /// <summary>Centre wavelength of band 0 in µm × 10000 (stored as uint32).</summary>
    public uint Band0Wavelength_um_x10000;

    /// <summary>Wavelength spacing in µm × 10000 for uniform grids; 0 for non-uniform.</summary>
    public uint WavelengthSpacing_um_x10000;

    /// <summary>Byte offset from file start to the JSON metadata block.</summary>
    public ulong JsonMetadataOffset;

    /// <summary>Byte length of the UTF-8 JSON metadata block.</summary>
    public uint JsonMetadataLength;

    /// <summary>Byte offset from file start to the start of the float32 data cube.</summary>
    public ulong DataOffset;

    /// <summary>Total byte length of the float32 data cube.</summary>
    public ulong DataLength;

    /// <summary>Reserved for future use. Must be zero.</summary>
    public uint Reserved1;
    public uint Reserved2;

    /// <summary>Expected magic value: 0x004958_4E (little-endian "NXI\0").</summary>
    public const uint ExpectedMagic = 0x004E5849u;

    /// <summary>Current file format version.</summary>
    public const ushort CurrentVersion = 1;
}
