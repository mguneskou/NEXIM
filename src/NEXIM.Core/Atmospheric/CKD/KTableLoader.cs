// NEXIM — Binary k-table loader with CRC32 validation.
//
// Binary .ktbl file format (all fields little-endian):
//   Offset  Size  Field
//   0       4     Magic: 0x4B544C42 ("KTBL")
//   4       2     Version: 1
//   6       1     GasSpecies (enum byte)
//   7       4     BandCentre_um × 10000 (uint32)
//   11      4     BandWidth_um × 10000 (uint32)
//   15      4     NPressureLevels (uint32)
//   19      4     NTempLevels (uint32)
//   23      4     NGPoints (uint32)
//   27      (NPressureLevels × 8) bytes — double[] PressureLevels_hPa
//   ...     (NTempLevels × 8) bytes     — double[] TemperatureOffsets_K
//   ...     (NGPoints × 8) bytes        — double[] GPoints
//   ...     (NGPoints × 8) bytes        — double[] GWeights
//   ...     (NPressureLevels × NTempLevels × NGPoints × 4) bytes — float[] KValues
//   [last 4 bytes]: CRC32 of all preceding bytes

using System.IO.Hashing;

namespace NEXIM.Core.Atmospheric.CKD;

/// <summary>
/// Loads pre-computed k-distribution tables from the binary .ktbl format.
/// k-tables are derived from the HITRAN2020 spectroscopic database:
///   Gordon et al. (2022) JQSRT 277:107949. doi:10.1016/j.jqsrt.2021.107949
/// </summary>
public static class KTableLoader
{
    private const uint ExpectedMagic = 0x4B544C42u; // "KTBL"
    private const ushort SupportedVersion = 1;

    /// <summary>
    /// Load a single k-distribution table from a .ktbl file.
    /// </summary>
    /// <param name="filePath">Absolute path to the .ktbl binary file.</param>
    /// <returns>Loaded and validated <see cref="KDistributionTable"/>.</returns>
    /// <exception cref="InvalidDataException">Thrown on magic mismatch, version mismatch, or CRC32 failure.</exception>
    public static KDistributionTable LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        byte[] data = File.ReadAllBytes(filePath);
        return ParseBytes(data, filePath);
    }

    /// <summary>
    /// Load a single k-distribution table from an embedded resource stream.
    /// </summary>
    /// <param name="stream">Stream containing the .ktbl binary data.</param>
    /// <param name="sourceLabel">Label used in exception messages.</param>
    public static KDistributionTable LoadFromStream(Stream stream, string sourceLabel = "<stream>")
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ParseBytes(ms.ToArray(), sourceLabel);
    }

    private static KDistributionTable ParseBytes(byte[] data, string sourceLabel)
    {
        if (data.Length < 32)
            throw new InvalidDataException($"[{sourceLabel}] File too small to be a valid .ktbl ({data.Length} bytes).");

        // --- CRC32 check: last 4 bytes are the CRC of everything before them ---
        uint storedCrc = ReadUInt32(data, data.Length - 4);
        uint computedCrc = Crc32.HashToUInt32(data.AsSpan(0, data.Length - 4));
        if (storedCrc != computedCrc)
            throw new InvalidDataException(
                $"[{sourceLabel}] CRC32 mismatch: stored 0x{storedCrc:X8}, computed 0x{computedCrc:X8}.");

        int pos = 0;

        // Magic
        uint magic = ReadUInt32(data, pos); pos += 4;
        if (magic != ExpectedMagic)
            throw new InvalidDataException(
                $"[{sourceLabel}] Invalid magic 0x{magic:X8}; expected 0x{ExpectedMagic:X8}.");

        // Version
        ushort version = ReadUInt16(data, pos); pos += 2;
        if (version != SupportedVersion)
            throw new InvalidDataException(
                $"[{sourceLabel}] Unsupported version {version}; expected {SupportedVersion}.");

        var species = (GasSpecies)data[pos]; pos += 1;

        double bandCentre_um = ReadUInt32(data, pos) / 10000.0; pos += 4;
        double bandWidth_um  = ReadUInt32(data, pos) / 10000.0; pos += 4;

        int nPressure = (int)ReadUInt32(data, pos); pos += 4;
        int nTemp     = (int)ReadUInt32(data, pos); pos += 4;
        int nG        = (int)ReadUInt32(data, pos); pos += 4;

        double[] pressureLevels = ReadDoubleArray(data, ref pos, nPressure);
        double[] tempOffsets    = ReadDoubleArray(data, ref pos, nTemp);
        double[] gPoints        = ReadDoubleArray(data, ref pos, nG);
        double[] gWeights       = ReadDoubleArray(data, ref pos, nG);

        int nKValues = nPressure * nTemp * nG;
        float[] kValues = ReadFloatArray(data, ref pos, nKValues);

        return new KDistributionTable
        {
            Species               = species,
            BandCentre_um         = bandCentre_um,
            BandWidth_um          = bandWidth_um,
            PressureLevels_hPa    = pressureLevels,
            TemperatureOffsets_K  = tempOffsets,
            GPoints               = gPoints,
            GWeights              = gWeights,
            KValues               = kValues,
        };
    }

    private static double[] ReadDoubleArray(byte[] data, ref int pos, int count)
    {
        var arr = new double[count];
        for (int i = 0; i < count; i++)
        {
            arr[i] = BitConverter.ToDouble(data, pos);
            pos += 8;
        }
        return arr;
    }

    private static float[] ReadFloatArray(byte[] data, ref int pos, int count)
    {
        var arr = new float[count];
        Buffer.BlockCopy(data, pos, arr, 0, count * 4);
        pos += count * 4;
        return arr;
    }

    private static uint ReadUInt32(byte[] data, int pos) =>
        BitConverter.ToUInt32(data, pos);

    private static ushort ReadUInt16(byte[] data, int pos) =>
        BitConverter.ToUInt16(data, pos);
}
