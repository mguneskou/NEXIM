// NEXIM — EnviImporter.cs
// Reads ENVI standard .hdr + .img (or .hdr + .img pair by path) into a
// HypercubeData object.  Supports BIL float32 files as written by
// EnviExporter.  Wavelengths in the .hdr must be in nanometres; they are
// converted to µm on load.

using System.Globalization;
using System.Runtime.InteropServices;
using NEXIM.Core.Models;

namespace NEXIM.Core.IO;

/// <summary>
/// Reads ENVI standard hyperspectral files (.hdr + .img) into
/// <see cref="HypercubeData"/>.
/// </summary>
public static class EnviImporter
{
    /// <summary>
    /// Load a cube from an ENVI file pair.
    /// <paramref name="path"/> may point to either the .hdr or the .img file.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Thrown when the header is incomplete, the data type is not float32, or
    /// the interleave order is not BIL.
    /// </exception>
    public static HypercubeData Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string ext     = Path.GetExtension(path);
        string hdrPath = ext.Equals(".hdr", StringComparison.OrdinalIgnoreCase)
                         ? path
                         : Path.ChangeExtension(path, ".hdr");
        string imgPath = Path.ChangeExtension(hdrPath, ".img");

        if (!File.Exists(hdrPath))
            throw new FileNotFoundException("ENVI header not found.", hdrPath);
        if (!File.Exists(imgPath))
            throw new FileNotFoundException("ENVI image data not found.", imgPath);

        // ── parse header ─────────────────────────────────────────────────
        var fields = ParseHeader(hdrPath);

        if (!fields.TryGetValue("samples",   out string? sv) || !int.TryParse(sv, out int cols))
            throw new InvalidDataException("ENVI header missing 'samples'.");
        if (!fields.TryGetValue("lines",     out string? lv) || !int.TryParse(lv, out int rows))
            throw new InvalidDataException("ENVI header missing 'lines'.");
        if (!fields.TryGetValue("bands",     out string? bv) || !int.TryParse(bv, out int bands))
            throw new InvalidDataException("ENVI header missing 'bands'.");

        if (fields.TryGetValue("data type", out string? dt) && dt != "4")
            throw new InvalidDataException(
                $"Only float32 (data type = 4) is supported; header reports {dt}.");

        if (fields.TryGetValue("interleave", out string? il) &&
            !il.Equals("bil", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Only BIL interleave is supported; header reports '{il}'.");

        // wavelength units — assume Nanometers unless explicitly µm/micrometers
        bool wlInUm = fields.TryGetValue("wavelength units", out string? wlu) &&
                      (wlu.Contains("micron", StringComparison.OrdinalIgnoreCase) ||
                       wlu.Contains("µm",     StringComparison.OrdinalIgnoreCase) ||
                       wlu.Equals("um",       StringComparison.OrdinalIgnoreCase));

        double[] wavelengths_um = Array.Empty<double>();
        if (fields.TryGetValue("wavelength", out string? wlVal))
            wavelengths_um = ParseDoubleList(wlVal, wlInUm ? 1.0 : 1.0e-3);

        if (wavelengths_um.Length != bands)
        {
            // fallback: synthesise uniform 0.40–2.50 µm grid
            wavelengths_um = new double[bands];
            double step = bands > 1 ? 2.10 / (bands - 1) : 0.05;
            for (int b = 0; b < bands; b++)
                wavelengths_um[b] = 0.40 + b * step;
        }

        // ── read binary data ──────────────────────────────────────────────
        float[][] cube = ReadBil(imgPath, rows, bands, cols);

        return new HypercubeData
        {
            Cube           = cube,
            Wavelengths_um = wavelengths_um,
            Rows           = rows,
            Bands          = bands,
            Columns        = cols,
            FileName       = Path.GetFileName(hdrPath),
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Parse an ENVI .hdr file into a key→value dictionary.
    /// Multi-line brace blocks (e.g. wavelength = { ... }) are concatenated.
    /// </summary>
    static Dictionary<string, string> ParseHeader(string hdrPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var raw    = File.ReadAllLines(hdrPath);

        var joined = new List<string>(raw.Length);
        var sb     = new System.Text.StringBuilder();
        bool inBlock = false;

        foreach (string rawLine in raw)
        {
            string line = rawLine.Trim();
            if (line.Equals("ENVI", StringComparison.OrdinalIgnoreCase)) continue;

            if (inBlock)
            {
                sb.Append(' ').Append(line);
                if (line.Contains('}')) { inBlock = false; joined.Add(sb.ToString()); sb.Clear(); }
            }
            else if (line.Contains('{') && !line.Contains('}'))
            {
                inBlock = true;
                sb.Append(line);
            }
            else
            {
                joined.Add(line);
            }
        }

        foreach (string line in joined)
        {
            int eq = line.IndexOf('=');
            if (eq < 1) continue;
            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim();
            // strip outer braces if present
            if (val.StartsWith('{') && val.EndsWith('}'))
                val = val[1..^1].Trim();
            result[key] = val;
        }

        return result;
    }

    /// <summary>
    /// Parse a comma-separated list of doubles and multiply each by
    /// <paramref name="scale"/> (use 1e-3 to convert nm → µm).
    /// </summary>
    static double[] ParseDoubleList(string csv, double scale)
    {
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                    StringSplitOptions.TrimEntries);
        var result = new double[parts.Length];
        var ci     = CultureInfo.InvariantCulture;
        for (int i = 0; i < parts.Length; i++)
            result[i] = double.Parse(parts[i], ci) * scale;
        return result;
    }

    /// <summary>
    /// Read a BIL float32 binary file into the cube[r*bands+b][col] layout.
    /// </summary>
    static float[][] ReadBil(string imgPath, int rows, int bands, int cols)
    {
        long expected = (long)rows * bands * cols * sizeof(float);
        long actual   = new FileInfo(imgPath).Length;
        if (actual < expected)
            throw new InvalidDataException(
                $"ENVI image is too small: expected {expected} bytes, found {actual}.");

        var cube = new float[rows * bands][];
        using var fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read,
                                      FileShare.Read, 65536);
        var buf = new byte[cols * sizeof(float)];

        for (int r = 0; r < rows; r++)
        for (int b = 0; b < bands; b++)
        {
            fs.ReadExactly(buf);
            var slice = new float[cols];
            Buffer.BlockCopy(buf, 0, slice, 0, buf.Length);
            cube[r * bands + b] = slice;
        }

        return cube;
    }
}
