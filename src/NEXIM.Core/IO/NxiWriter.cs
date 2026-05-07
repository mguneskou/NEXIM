// NEXIM — .nxi file writer.
// Format layout:
//   [0..63]   NxiHeader (64 bytes, LayoutKind.Sequential Pack=1 Size=64)
//   [64..N]   UTF-8 JSON metadata block (length-prefixed in header)
//   [N+1..]   float32 BIL interleaved hypercube  (rows × bands × columns)
//   [tail]    CRC32 (4 bytes, System.IO.Hashing.Crc32, big-endian per RFC 3720)
//
// BIL memory layout: for each row r, for each band b, write all columns.
// cube[r][b] is the float32 array of length Columns for that (row, band).

using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NEXIM.Core.Models;

namespace NEXIM.Core.IO;

/// <summary>
/// Metadata written into the JSON block of an .nxi file.
/// </summary>
public sealed class NxiMetadata
{
    public string SceneName    { get; init; } = string.Empty;
    public string CreatedUtc   { get; init; } = DateTime.UtcNow.ToString("O");
    public string Description  { get; init; } = string.Empty;
    /// <summary>Centre wavelengths in µm for each band.</summary>
    public double[] Wavelengths_um { get; init; } = Array.Empty<double>();
    /// <summary>Sensor-level FWHM values in µm (one per band), or empty.</summary>
    public double[] Fwhm_um        { get; init; } = Array.Empty<double>();
    /// <summary>Arbitrary key-value pairs for provenance.</summary>
    public Dictionary<string, string> Extras { get; init; } = new();
}

/// <summary>
/// Writes hyperspectral cubes to the .nxi binary format.
/// </summary>
public static class NxiWriter
{
    // ── public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Write a float32 BIL cube to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Destination file path (created or overwritten).</param>
    /// <param name="cube">
    ///   Jagged array [rows][bands][columns] — cube[r][b] is an array of
    ///   <paramref name="columns"/> float values for row r, band b.
    /// </param>
    /// <param name="rows">Spatial row count.</param>
    /// <param name="bands">Spectral band count.</param>
    /// <param name="columns">Spatial column count.</param>
    /// <param name="wavelengths_um">Centre wavelengths in µm (length == bands).</param>
    /// <param name="metadata">Optional JSON metadata. If null a minimal record is used.</param>
    public static void Write(
        string path,
        float[][] cube,   // [row * bands + band][columns]
        int rows, int bands, int columns,
        double[] wavelengths_um,
        NxiMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(cube);
        ArgumentNullException.ThrowIfNull(wavelengths_um);
        if (rows <= 0 || bands <= 0 || columns <= 0)
            throw new ArgumentOutOfRangeException("rows/bands/columns must be > 0");
        if (wavelengths_um.Length != bands)
            throw new ArgumentException("wavelengths_um.Length must equal bands.");
        if (cube.Length != rows * bands)
            throw new ArgumentException(
                "cube must have rows*bands slices (cube[r*bands+b] = row r, band b).");

        metadata ??= new NxiMetadata { Wavelengths_um = wavelengths_um };

        // ── build JSON metadata ──────────────────────────────────────────
        string json    = JsonSerializer.Serialize(metadata,
                            new JsonSerializerOptions { WriteIndented = false });
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        // ── layout offsets ───────────────────────────────────────────────
        const int headerSize  = 64;
        ulong jsonOffset      = headerSize;
        ulong dataOffset      = jsonOffset + (ulong)jsonBytes.Length;
        ulong dataLength      = (ulong)(rows * bands * columns) * sizeof(float);

        // ── fill header ──────────────────────────────────────────────────
        NxiHeader hdr = default;
        hdr.Magic                      = NxiHeader.ExpectedMagic;
        hdr.Version                    = NxiHeader.CurrentVersion;
        hdr.Columns                    = (uint)columns;
        hdr.Rows                       = (uint)rows;
        hdr.Bands                      = (uint)bands;
        hdr.InterleaveOrder            = 0; // BIL
        hdr.DataType                   = 0; // float32
        hdr.Band0Wavelength_um_x10000  = (uint)Math.Round(wavelengths_um[0] * 10_000.0);
        hdr.WavelengthSpacing_um_x10000 = bands > 1
            ? (uint)Math.Round((wavelengths_um[bands - 1] - wavelengths_um[0])
                               / (bands - 1) * 10_000.0)
            : 0u;
        hdr.JsonMetadataOffset  = jsonOffset;
        hdr.JsonMetadataLength  = (uint)jsonBytes.Length;
        hdr.DataOffset          = dataOffset;
        hdr.DataLength          = dataLength;
        hdr.Reserved1           = 0;
        hdr.Reserved2           = 0;

        // ── write with CRC streaming ────────────────────────────────────
        var crc          = new Crc32();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write,
                                          FileShare.None, 65536);

        WriteStruct(stream, crc, ref hdr);
        WriteBytes(stream, crc, jsonBytes);
        WriteFloatsBil(stream, crc, cube, rows, bands, columns);

        // append CRC32 (4-byte little-endian)
        Span<byte> crcBuf = stackalloc byte[4];
        crc.GetCurrentHash(crcBuf);
        stream.Write(crcBuf);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    static void WriteStruct<T>(Stream s, Crc32 crc, ref T value) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buf = new byte[size];
        MemoryMarshal.Write(buf, in value);
        crc.Append(buf);
        s.Write(buf);
    }

    static void WriteBytes(Stream s, Crc32 crc, byte[] data)
    {
        crc.Append(data);
        s.Write(data);
    }

    static void WriteFloatsBil(Stream s, Crc32 crc,
                                float[][] cube, int rows, int bands, int columns)
    {
        for (int r = 0; r < rows; r++)
            for (int b = 0; b < bands; b++)
            {
                float[] slice = cube[r * bands + b];
                var span = MemoryMarshal.AsBytes(slice.AsSpan(0, columns));
                crc.Append(span);
                s.Write(span);
            }
    }
}
