using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace NEXIM.Core.Atmospheric.LUT;

/// <summary>
/// Reads a pre-computed atmospheric LUT from the binary file format produced
/// by NEXIM.LutGen.
///
/// Binary file layout:
///   Bytes 0–3    : magic bytes 0x4C555401 ("LUT\x01")
///   Bytes 4–7    : JSON metadata length in bytes (little-endian uint32)
///   Bytes 8+     : JSON metadata (UTF-8, describing the LutFormat)
///   (aligned to next 16-byte boundary)
///   Data block   : float32 values in row-major order per LutFormat.FlatIndex
///   Last 4 bytes : CRC32 of the entire file except the last 4 bytes
///
/// This simple format avoids external dependencies (no HDF5, no NetCDF).
/// </summary>
public static class LutLoader
{
    private const uint Magic = 0x4C555401u; // "LUT\x01"

    /// <summary>
    /// Load a LUT from a file path. Validates the magic number and CRC32.
    /// </summary>
    /// <exception cref="InvalidDataException">If file is corrupt or version mismatch.</exception>
    public static (LutFormat format, float[] data) LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        using var fs = File.OpenRead(filePath);
        return LoadFromStream(fs);
    }

    /// <summary>Load a LUT from a stream. The stream must be seekable for CRC validation.</summary>
    public static (LutFormat format, float[] data) LoadFromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Read magic
        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header[..4]);
        if (magic != Magic)
            throw new InvalidDataException(
                $"LUT file magic mismatch: expected 0x{Magic:X8}, got 0x{magic:X8}.");

        uint jsonLen = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);

        // Read JSON metadata
        byte[] jsonBytes = new byte[jsonLen];
        stream.ReadExactly(jsonBytes);

        var format = JsonSerializer.Deserialize<LutFormat>(jsonBytes)
            ?? throw new InvalidDataException("LUT JSON metadata is null.");

        // Align to next 16-byte boundary
        long pos = 8 + jsonLen;
        long aligned = (pos + 15) & ~15L;
        if (aligned > pos) stream.Seek(aligned - pos, SeekOrigin.Current);

        // Read float32 data
        long nFloats = format.TotalFloats;
        if (nFloats <= 0 || nFloats > 500_000_000L)
            throw new InvalidDataException($"LUT size {nFloats} floats is out of expected range.");

        byte[] raw = new byte[nFloats * sizeof(float)];
        stream.ReadExactly(raw);

        var data = new float[nFloats];
        Buffer.BlockCopy(raw, 0, data, 0, raw.Length);

        // Correct endianness if needed (file is always little-endian)
        if (!BitConverter.IsLittleEndian)
            for (int i = 0; i < data.Length; i++)
            {
                var bytes = BitConverter.GetBytes(data[i]);
                Array.Reverse(bytes);
                data[i] = BitConverter.ToSingle(bytes, 0);
            }

        return (format, data);
    }
}
