// NEXIM.LutGen — Binary .ktbl file writer.
//
// Produces files in the exact format expected by KTableLoader.ParseBytes.
//
// Binary layout (all fields little-endian):
//   0    4   Magic: 0x4B544C42 ("KTBL")
//   4    2   Version: 1
//   6    1   GasSpecies (byte)
//   7    4   BandCentre_um × 10000 (uint32)
//   11   4   BandWidth_um  × 10000 (uint32)
//   15   4   NPressureLevels (uint32)
//   19   4   NTempLevels (uint32)
//   23   4   NGPoints (uint32)
//   27   nP×8   PressureLevels_hPa (double[])
//   ...  nT×8   TemperatureOffsets_K (double[])
//   ...  nG×8   GPoints (double[])
//   ...  nG×8   GWeights (double[])
//   ...  nP×nT×nG×4   KValues (float[])
//   [last 4]   CRC32 of all preceding bytes

using System.Buffers.Binary;
using System.IO.Hashing;
using NEXIM.Core.Atmospheric.CKD;

namespace NEXIM.LutGen;

internal static class KtblWriter
{
    private const uint Magic   = 0x4B544C42u;  // "KTBL"
    private const ushort Version = 1;

    /// <summary>
    /// Write a k-distribution table to <paramref name="filePath"/>.
    /// </summary>
    public static void Write(
        string     filePath,
        GasSpecies species,
        double     bandCentre_um,
        double     bandWidth_um,
        double[]   pressureLevels_hPa,
        double[]   tempOffsets_K,
        double[]   gPoints,
        double[]   gWeights,
        float[]    kValues)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        int nP = pressureLevels_hPa.Length;
        int nT = tempOffsets_K.Length;
        int nG = gPoints.Length;
        int expectedK = nP * nT * nG;
        if (kValues.Length != expectedK)
            throw new ArgumentException(
                $"kValues length {kValues.Length} != expected {expectedK} (nP={nP}, nT={nT}, nG={nG}).");

        // ── Compute total byte count (excluding the trailing CRC field) ────
        int headerBytes = 4 + 2 + 1 + 4 + 4 + 4 + 4 + 4;                     // 27
        int axisBytes   = nP * 8 + nT * 8 + nG * 8 + nG * 8;
        int kBytes      = expectedK * 4;
        int totalNoCrc  = headerBytes + axisBytes + kBytes;

        byte[] buf = new byte[totalNoCrc + 4]; // +4 for CRC
        int pos = 0;

        WriteUInt32(buf, ref pos, Magic);
        WriteUInt16(buf, ref pos, Version);
        buf[pos++] = (byte)species;
        WriteUInt32(buf, ref pos, (uint)(bandCentre_um * 10000 + 0.5));
        WriteUInt32(buf, ref pos, (uint)(bandWidth_um  * 10000 + 0.5));
        WriteUInt32(buf, ref pos, (uint)nP);
        WriteUInt32(buf, ref pos, (uint)nT);
        WriteUInt32(buf, ref pos, (uint)nG);

        WriteDoubleArray(buf, ref pos, pressureLevels_hPa);
        WriteDoubleArray(buf, ref pos, tempOffsets_K);
        WriteDoubleArray(buf, ref pos, gPoints);
        WriteDoubleArray(buf, ref pos, gWeights);
        WriteFloatArray(buf, ref pos, kValues);

        // CRC32 over all bytes before the last 4
        uint crc = Crc32.HashToUInt32(buf.AsSpan(0, totalNoCrc));
        WriteUInt32(buf, ref pos, crc);

        File.WriteAllBytes(filePath, buf);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    static void WriteUInt32(byte[] buf, ref int pos, uint v)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos), v);
        pos += 4;
    }

    static void WriteUInt16(byte[] buf, ref int pos, ushort v)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(pos), v);
        pos += 2;
    }

    static void WriteDoubleArray(byte[] buf, ref int pos, double[] arr)
    {
        foreach (double d in arr)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(pos), BitConverter.DoubleToInt64Bits(d));
            pos += 8;
        }
    }

    static void WriteFloatArray(byte[] buf, ref int pos, float[] arr)
    {
        Buffer.BlockCopy(arr, 0, buf, pos, arr.Length * 4);
        pos += arr.Length * 4;
    }
}
