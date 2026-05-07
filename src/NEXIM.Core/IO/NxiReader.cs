// NEXIM — .nxi file reader.
// Reads the header, validates magic + version + CRC32, deserialises JSON
// metadata, and loads the float32 BIL data cube into memory.

using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NEXIM.Core.Models;

namespace NEXIM.Core.IO;

/// <summary>
/// Result returned by <see cref="NxiReader.Read"/>.
/// </summary>
public sealed class NxiReadResult
{
    /// <summary>Parsed file header.</summary>
    public NxiHeader Header { get; init; }

    /// <summary>Deserialised JSON metadata block.</summary>
    public NxiMetadata Metadata { get; init; } = new();

    /// <summary>
    /// BIL data cube.  cube[r * Bands + b] is the float32 array of length
    /// Columns for spatial row r and spectral band b.
    /// </summary>
    public float[][] Cube { get; init; } = Array.Empty<float[]>();
}

/// <summary>
/// Reads hyperspectral cubes from the .nxi binary format.
/// </summary>
public static class NxiReader
{
    // ── public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Read an .nxi file and return its header, metadata, and data cube.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when magic bytes, version, or CRC32 do not match.
    /// </exception>
    public static NxiReadResult Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                          FileShare.Read, 65536);
        return ReadFromStream(stream);
    }

    /// <summary>
    /// Read from an arbitrary <see cref="Stream"/> (must be seekable).
    /// </summary>
    public static NxiReadResult ReadFromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("Stream must be readable and seekable.");

        long fileLength = stream.Length;

        // ── header ───────────────────────────────────────────────────────
        const int headerSize = 64;
        Span<byte> hdrBuf = stackalloc byte[headerSize];
        ReadExact(stream, hdrBuf);
        NxiHeader hdr = MemoryMarshal.Read<NxiHeader>(hdrBuf);

        if (hdr.Magic != NxiHeader.ExpectedMagic)
            throw new InvalidDataException(
                $"Not a valid .nxi file: bad magic 0x{hdr.Magic:X8}.");
        if (hdr.Version != NxiHeader.CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported .nxi version {hdr.Version}; expected {NxiHeader.CurrentVersion}.");
        if (hdr.InterleaveOrder != 0)
            throw new InvalidDataException(
                $"Only BIL (interleave=0) is supported; got {hdr.InterleaveOrder}.");
        if (hdr.DataType != 0)
            throw new InvalidDataException(
                $"Only float32 (dataType=0) is supported; got {hdr.DataType}.");

        int rows    = (int)hdr.Rows;
        int bands   = (int)hdr.Bands;
        int columns = (int)hdr.Columns;

        if (rows <= 0 || bands <= 0 || columns <= 0)
            throw new InvalidDataException("Header contains zero-dimension value.");

        // ── CRC32 validation — done first so corrupt files fail fast ──────
        long crcPayloadLength = fileLength - 4;
        if (crcPayloadLength < headerSize)
            throw new InvalidDataException("File too small to contain a CRC.");

        stream.Seek(0, SeekOrigin.Begin);
        uint computedCrc = ComputeCrc32(stream, crcPayloadLength);

        stream.Seek(fileLength - 4, SeekOrigin.Begin);
        Span<byte> storedBuf = stackalloc byte[4];
        ReadExact(stream, storedBuf);
        uint storedCrc = MemoryMarshal.Read<uint>(storedBuf);

        if (computedCrc != storedCrc)
            throw new InvalidDataException(
                $"CRC32 mismatch: computed 0x{computedCrc:X8}, stored 0x{storedCrc:X8}.");

        // ── JSON metadata ─────────────────────────────────────────────────
        if ((long)hdr.JsonMetadataOffset + hdr.JsonMetadataLength > fileLength)
            throw new InvalidDataException("JSON metadata region extends beyond file end.");

        stream.Seek((long)hdr.JsonMetadataOffset, SeekOrigin.Begin);
        byte[] jsonBuf = new byte[hdr.JsonMetadataLength];
        ReadExact(stream, jsonBuf);
        NxiMetadata meta;
        try
        {
            meta = JsonSerializer.Deserialize<NxiMetadata>(
                Encoding.UTF8.GetString(jsonBuf)) ?? new NxiMetadata();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Failed to parse .nxi JSON metadata.", ex);
        }

        // ── data cube ────────────────────────────────────────────────────
        ulong expectedDataLen = (ulong)(rows * bands * columns) * sizeof(float);
        if (hdr.DataLength != expectedDataLen)
            throw new InvalidDataException(
                $"DataLength mismatch: header says {hdr.DataLength}, expected {expectedDataLen}.");
        if ((long)hdr.DataOffset + (long)hdr.DataLength > fileLength - 4)
            throw new InvalidDataException("Data region extends beyond file end (minus CRC).");

        stream.Seek((long)hdr.DataOffset, SeekOrigin.Begin);

        float[][] cube = new float[rows * bands][];
        for (int r = 0; r < rows; r++)
            for (int b = 0; b < bands; b++)
            {
                float[] slice = new float[columns];
                ReadExact(stream, MemoryMarshal.AsBytes(slice.AsSpan()));
                cube[r * bands + b] = slice;
            }

        return new NxiReadResult { Header = hdr, Metadata = meta, Cube = cube };
    }

    // ── helpers ────────────────────────────────────────────────────────────

    static void ReadExact(Stream s, Span<byte> buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = s.Read(buf[total..]);
            if (n == 0) throw new EndOfStreamException("Unexpected end of .nxi stream.");
            total += n;
        }
    }

    static uint ComputeCrc32(Stream s, long length)
    {
        var crc  = new Crc32();
        var buf  = new byte[65536];
        long remaining = length;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buf.Length, remaining);
            int n      = s.Read(buf, 0, toRead);
            if (n == 0) break;
            crc.Append(buf.AsSpan(0, n));
            remaining -= n;
        }
        Span<byte> hashBuf = stackalloc byte[4];
        crc.GetCurrentHash(hashBuf);
        return MemoryMarshal.Read<uint>(hashBuf);
    }
}
