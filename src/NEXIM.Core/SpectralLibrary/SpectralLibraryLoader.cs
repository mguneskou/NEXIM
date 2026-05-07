// NEXIM — Spectral library file loader.
//
// Supports two source formats:
//   1. Simple CSV — two-column (Wavelength_um, Reflectance) or multi-column
//      with header row "Wavelength_um,{name1},{name2},…"
//   2. USGS ASCII splib07 — the standard text format produced by the USGS
//      Spectroscopy Lab (Kokaly et al. 2017, doi:10.3133/ds1035).
//
// All loaded spectra are returned as SpectralSignature objects and can be
// combined with EmbeddedSpectralLibrary.GetAll() for a complete library.

using System.Globalization;

namespace NEXIM.Core.SpectralLibrary;

public static class SpectralLibraryLoader
{
    // ── CSV loader ────────────────────────────────────────────────────────

    /// <summary>
    /// Load spectra from a CSV file.
    ///
    /// Supported formats:
    /// • Single-spectrum: first column = wavelength, second = reflectance.
    ///   File name (without extension) is used as the material name.
    /// • Multi-spectrum: first column = wavelength, remaining columns =
    ///   spectra; column headers are used as names.
    /// </summary>
    public static List<SpectralSignature> LoadFromCsv(
        string filePath,
        string category = "Custom")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 2)
            throw new InvalidDataException($"CSV file has fewer than 2 lines: {filePath}");

        // Detect header
        string firstCell = lines[0].Split(',')[0].Trim();
        bool hasHeader = !double.TryParse(firstCell, NumberStyles.Any,
                                          CultureInfo.InvariantCulture, out _);

        string[] names;
        int dataStart;

        if (hasHeader)
        {
            var headers = lines[0].Split(',');
            names = headers.Skip(1).Select(h => h.Trim()).ToArray();
            dataStart = 1;
        }
        else
        {
            names = [Path.GetFileNameWithoutExtension(filePath)];
            dataStart = 0;
        }

        var wls = new List<double>();
        var reflColumns = new List<List<double>>();
        for (int c = 0; c < names.Length; c++) reflColumns.Add([]);

        foreach (string rawLine in lines.Skip(dataStart))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            var parts = line.Split(',');
            if (parts.Length < 2) continue;

            if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double wl))
                continue;
            wls.Add(wl);

            for (int c = 0; c < names.Length && c + 1 < parts.Length; c++)
            {
                if (double.TryParse(parts[c + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out double r))
                    reflColumns[c].Add(Math.Clamp(r, 0.0, 1.0));
                else
                    reflColumns[c].Add(0.0);
            }
        }

        double[] wlArr = [.. wls];
        var result = new List<SpectralSignature>();

        for (int c = 0; c < names.Length; c++)
        {
            if (reflColumns[c].Count == wlArr.Length)
                result.Add(new SpectralSignature
                {
                    Name           = names[c],
                    Category       = category,
                    Wavelengths_um = wlArr,
                    Reflectance    = [.. reflColumns[c]],
                    Citation       = $"Custom CSV: {Path.GetFileName(filePath)}",
                });
        }

        return result;
    }

    // ── USGS ASCII loader ─────────────────────────────────────────────────

    /// <summary>
    /// Parse a single USGS splib07 ASCII file (one spectrum per file).
    /// The format begins with a header block ending with a line of dashes,
    /// followed by wavelength/reflectance pairs.
    /// </summary>
    public static SpectralSignature LoadFromUsgsAscii(
        string filePath,
        string? categoryOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var lines = File.ReadAllLines(filePath);

        string name     = Path.GetFileNameWithoutExtension(filePath);
        string category = categoryOverride ?? "USGS";

        // Parse header for Name: and Description:
        foreach (string line in lines)
        {
            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            {
                name = line["Name:".Length..].Trim();
                break;
            }
        }

        // Find the start of data (after blank line or all-dash separator)
        int dataStart = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.All(c => c == '-') || (t.Length == 0 && i > 3))
            {
                dataStart = i + 1;
                break;
            }
        }

        var wls   = new List<double>();
        var refls = new List<double>();

        foreach (string rawLine in lines.Skip(dataStart))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(';')) continue;

            var parts = line.Split(new char[] { ' ', '\t' },
                                   StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double wl)) continue;
            if (!double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double r))  continue;

            // USGS uses -1.23 as a "bad value" flag
            if (r < -0.5) continue;

            wls.Add(wl);
            refls.Add(Math.Clamp(r, 0.0, 1.0));
        }

        if (wls.Count == 0)
            throw new InvalidDataException($"No valid data found in USGS file: {filePath}");

        return new SpectralSignature
        {
            Name           = name,
            Category       = category,
            Wavelengths_um = [.. wls],
            Reflectance    = [.. refls],
            Citation       = "USGS Spectral Library v7 (doi:10.3133/ds1035)",
        };
    }

    /// <summary>
    /// Load all USGS ASCII .txt files from a directory.
    /// </summary>
    public static List<SpectralSignature> LoadFromUsgsDirectory(
        string directoryPath,
        string? categoryOverride = null)
    {
        var result = new List<SpectralSignature>();
        foreach (string f in Directory.EnumerateFiles(directoryPath, "*.txt"))
        {
            try { result.Add(LoadFromUsgsAscii(f, categoryOverride)); }
            catch { /* skip malformed files */ }
        }
        return result;
    }
}
