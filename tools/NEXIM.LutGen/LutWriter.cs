// NEXIM.LutGen — Binary .lut file writer.
//
// Produces files in the exact format expected by LutLoader.LoadFromStream.
//
// Binary layout:
//   Bytes 0-3   : magic 0x4C555401 ("LUT\x01")
//   Bytes 4-7   : JSON metadata length (uint32 LE)
//   Bytes 8+    : JSON metadata (UTF-8, LutFormat)
//   (padded to next 16-byte boundary)
//   Data block  : float32 values, row-major per LutFormat.FlatIndex
//   Last 4 bytes: CRC32 of everything before it

using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using NEXIM.Core.Atmospheric.LUT;

namespace NEXIM.LutGen;

internal static class LutWriter
{
    private const uint Magic = 0x4C555401u; // "LUT\x01"

    /// <summary>
    /// Serialise and write a pre-computed LUT to <paramref name="filePath"/>.
    /// </summary>
    public static void Write(string filePath, LutFormat format, float[] data)
    {
        if (data.Length != format.TotalFloats)
            throw new ArgumentException(
                $"data length {data.Length} != expected TotalFloats {format.TotalFloats}.");

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(format);
        uint   jsonLen   = (uint)jsonBytes.Length;

        // Compute padding to align data to next 16-byte boundary after header+JSON
        int headerSize  = 8 + (int)jsonLen;
        int padding     = (16 - (headerSize % 16)) % 16;

        int dataBytes   = data.Length * sizeof(float);
        int totalNoCrc  = headerSize + padding + dataBytes;

        byte[] buf = new byte[totalNoCrc + 4];
        int pos = 0;

        // Magic + JSON length
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos), Magic); pos += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos), jsonLen); pos += 4;

        // JSON metadata
        jsonBytes.CopyTo(buf, pos); pos += (int)jsonLen;

        // Padding
        pos += padding;

        // Float32 data (little-endian — on LE platforms this is a direct copy)
        if (BitConverter.IsLittleEndian)
        {
            Buffer.BlockCopy(data, 0, buf, pos, dataBytes);
        }
        else
        {
            for (int i = 0; i < data.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos + i * 4), data[i]);
            }
        }
        pos += dataBytes;

        // CRC32
        uint crc = Crc32.HashToUInt32(buf.AsSpan(0, totalNoCrc));
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos), crc);

        File.WriteAllBytes(filePath, buf);
    }
}
