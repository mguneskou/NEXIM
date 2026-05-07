// NEXIM — ENVI file exporter.
// Writes a standard ENVI flat-binary (.img) data file and an ASCII header
// (.hdr) file that can be opened directly by ENVI, GDAL, and most remote
// sensing tools.
//
// ENVI BIL layout written here:
//   For each row r:
//     For each band b:
//       float32[columns]   — host byte order (little-endian on x86/x64)
//
// Reference: ENVI Header Format — Harris Geospatial Solutions documentation
//   https://www.nv5geospatialsoftware.com/docs/ENVIHeaderFiles.html

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace NEXIM.Core.IO;

/// <summary>
/// Exports hyperspectral cubes to ENVI .hdr + .img format.
/// </summary>
public static class EnviExporter
{
    // ── public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Write an ENVI dataset to <paramref name="hdrPath"/> and a companion
    /// .img data file derived from <paramref name="hdrPath"/> by stripping the
    /// extension (or optionally specified via <paramref name="imgPath"/>).
    /// </summary>
    /// <param name="hdrPath">Path for the ENVI header file (.hdr).</param>
    /// <param name="cube">
    ///   BIL cube: cube[r * bands + b] is the float32 row–band slice.
    /// </param>
    /// <param name="rows">Spatial row count.</param>
    /// <param name="bands">Number of spectral bands.</param>
    /// <param name="columns">Spatial column count.</param>
    /// <param name="wavelengths_um">
    ///   Centre wavelengths in µm (length == bands).
    ///   Written to the header in nanometres (×1000).
    /// </param>
    /// <param name="description">Optional dataset description string.</param>
    /// <param name="imgPath">
    ///   Optional explicit path for the binary .img file.
    ///   Defaults to <paramref name="hdrPath"/> with extension replaced by .img.
    /// </param>
    public static void Export(
        string hdrPath,
        float[][] cube,
        int rows, int bands, int columns,
        double[] wavelengths_um,
        string? description = null,
        string? imgPath     = null)
    {
        ArgumentNullException.ThrowIfNull(hdrPath);
        ArgumentNullException.ThrowIfNull(cube);
        ArgumentNullException.ThrowIfNull(wavelengths_um);
        if (rows <= 0 || bands <= 0 || columns <= 0)
            throw new ArgumentOutOfRangeException("rows/bands/columns must be > 0");
        if (wavelengths_um.Length != bands)
            throw new ArgumentException("wavelengths_um.Length must equal bands.");
        if (cube.Length != rows * bands)
            throw new ArgumentException(
                "cube must have rows*bands slices (cube[r*bands+b]).");

        imgPath ??= Path.ChangeExtension(hdrPath, ".img");

        // ── write binary data file ───────────────────────────────────────
        using (var img = new FileStream(imgPath, FileMode.Create, FileAccess.Write,
                                        FileShare.None, 65536))
        {
            for (int r = 0; r < rows; r++)
                for (int b = 0; b < bands; b++)
                {
                    float[] slice = cube[r * bands + b];
                    img.Write(MemoryMarshal.AsBytes(slice.AsSpan(0, columns)));
                }
        }

        // ── write header file ────────────────────────────────────────────
        var sb  = new StringBuilder(512);
        var ci  = CultureInfo.InvariantCulture;

        // ENVI data type 4 = float32.  Byte order 0 = host/little-endian.
        sb.AppendLine("ENVI");
        sb.AppendLine($"description = {{ {description ?? "NEXIM export"} }}");
        sb.AppendLine($"samples = {columns}");
        sb.AppendLine($"lines   = {rows}");
        sb.AppendLine($"bands   = {bands}");
        sb.AppendLine("header offset = 0");
        sb.AppendLine($"file type = ENVI Standard");
        sb.AppendLine("data type = 4");          // float32
        sb.AppendLine("interleave = bil");
        sb.AppendLine("byte order = 0");          // little-endian
        sb.AppendLine($"data ignore value = {{}}");
        sb.AppendLine($"wavelength units = Nanometers");

        // Wavelength list in nm
        sb.Append("wavelength = {");
        for (int b = 0; b < bands; b++)
        {
            sb.Append(ci, $"{wavelengths_um[b] * 1000.0:F4}");
            if (b < bands - 1) sb.Append(", ");
        }
        sb.AppendLine("}");

        // Band names
        sb.Append("band names = {");
        for (int b = 0; b < bands; b++)
        {
            sb.Append(ci, $"Band {b + 1} ({wavelengths_um[b] * 1000.0:F1} nm)");
            if (b < bands - 1) sb.Append(", ");
        }
        sb.AppendLine("}");

        File.WriteAllText(hdrPath, sb.ToString(), Encoding.ASCII);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Read an ENVI .hdr file and return basic dimension metadata only.
    /// Useful for validation in tests without depending on external libraries.
    /// </summary>
    public static EnviHeader ReadHeader(string hdrPath)
    {
        ArgumentNullException.ThrowIfNull(hdrPath);
        var result = new EnviHeader();
        foreach (string raw in File.ReadAllLines(hdrPath))
        {
            string line = raw.Trim();
            if (TryParsePair(line, "samples", out string sv))
                result.Samples = int.Parse(sv, CultureInfo.InvariantCulture);
            else if (TryParsePair(line, "lines", out string lv))
                result.Lines = int.Parse(lv, CultureInfo.InvariantCulture);
            else if (TryParsePair(line, "bands", out string bv))
                result.Bands = int.Parse(bv, CultureInfo.InvariantCulture);
            else if (TryParsePair(line, "data type", out string dt))
                result.DataType = int.Parse(dt, CultureInfo.InvariantCulture);
            else if (TryParsePair(line, "interleave", out string il))
                result.Interleave = il;
            else if (TryParsePair(line, "byte order", out string bo))
                result.ByteOrder = int.Parse(bo, CultureInfo.InvariantCulture);
        }
        return result;
    }

    static bool TryParsePair(string line, string key, out string value)
    {
        // Pattern: "key = value" or "key=value"
        int eq = line.IndexOf('=');
        if (eq < 0) { value = ""; return false; }
        if (!line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
        { value = ""; return false; }
        value = line[(eq + 1)..].Trim().TrimStart('{').TrimEnd('}').Trim();
        return true;
    }
}

/// <summary>
/// Parsed fields from an ENVI .hdr file.
/// </summary>
public sealed class EnviHeader
{
    public int    Samples    { get; set; }
    public int    Lines      { get; set; }
    public int    Bands      { get; set; }
    public int    DataType   { get; set; }
    public string Interleave { get; set; } = string.Empty;
    public int    ByteOrder  { get; set; }
}
