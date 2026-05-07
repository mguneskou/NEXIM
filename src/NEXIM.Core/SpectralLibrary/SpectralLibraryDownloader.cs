// NEXIM — USGS Spectral Library v7 downloader.
//
// Downloads individual spectrum ASCII files from the USGS Spectroscopy Lab
// server.  The USGS Spectral Library Version 7 is in the public domain:
//   Kokaly, R. F., et al. (2017). USGS Spectral Library Version 7.
//   U.S. Geological Survey Data Series 1035. doi:10.3133/ds1035.
//
// Download URL template:
//   https://crustal.usgs.gov/speclab/data/chapter{chapter}/ASCII/{file}
//
// This class provides:
//   1. A curated catalog of ~40 high-value spectra with their chapter/file names.
//   2. An async method to download and parse individual or all curated spectra.
//   3. Progress reporting (number of spectra downloaded).
//
// Downloaded files are cached at the local cache path to avoid redundant downloads.

namespace NEXIM.Core.SpectralLibrary;

public static class SpectralLibraryDownloader
{
    private const string BaseUrl = "https://crustal.usgs.gov/speclab/data/";

    // ── Curated catalog ───────────────────────────────────────────────────
    // Format: (chapter, filename, suggested_name, category)
    // Chapter numbers follow USGS splib07 organisation:
    //   1=Minerals, 2=Soils, 3=Coatings, 4=Liquids, 5=Organics,
    //   6=Artificial, 7=Plants, 8=Mixtures

    public static readonly IReadOnlyList<CatalogEntry> CuratedCatalog =
        new List<CatalogEntry>
        {
            // Vegetation
            new("chapter7", "s07_Grass-dry_ASD.txt",        "Grass Dry",          "Vegetation"),
            new("chapter7", "s07_Grass-green_ASD.txt",      "Grass Green",        "Vegetation"),
            new("chapter7", "s07_Aspen_ASD.txt",            "Aspen Leaves",       "Vegetation"),
            new("chapter7", "s07_Conifer_ASD.txt",          "Conifer Needles",    "Vegetation"),
            new("chapter7", "s07_Corn-leaf_ASD.txt",        "Corn Leaf",          "Vegetation"),
            new("chapter7", "s07_Sage_ASD.txt",             "Sagebrush",          "Vegetation"),

            // Soils
            new("chapter2", "s07_Soil-sand-dry_ASD.txt",   "Sandy Soil Dry",     "Soil"),
            new("chapter2", "s07_Soil-sand-wet_ASD.txt",   "Sandy Soil Wet",     "Soil"),
            new("chapter2", "s07_Soil-clay_ASD.txt",       "Clay Soil",          "Soil"),
            new("chapter2", "s07_Soil-loam_ASD.txt",       "Loamy Soil",         "Soil"),
            new("chapter2", "s07_Soil-organic_ASD.txt",    "Organic Topsoil",    "Soil"),

            // Minerals
            new("chapter1", "s07_Gypsum_ASD.txt",          "Gypsum",             "Mineral"),
            new("chapter1", "s07_Kaolinite_ASD.txt",       "Kaolinite",          "Mineral"),
            new("chapter1", "s07_Goethite_ASD.txt",        "Goethite",           "Mineral"),
            new("chapter1", "s07_Calcite_ASD.txt",         "Calcite",            "Mineral"),
            new("chapter1", "s07_Quartz_ASD.txt",          "Quartz",             "Mineral"),
            new("chapter1", "s07_Dolomite_ASD.txt",        "Dolomite",           "Mineral"),

            // Artificial / Urban
            new("chapter6", "s07_Asphalt_ASD.txt",         "Asphalt",            "Urban"),
            new("chapter6", "s07_Concrete_ASD.txt",        "Concrete",           "Urban"),
            new("chapter6", "s07_Rooftop-metal_ASD.txt",   "Metal Roof",         "Urban"),
            new("chapter6", "s07_Rooftop-tile_ASD.txt",    "Roof Tile",          "Urban"),
            new("chapter6", "s07_Paint-white_ASD.txt",     "White Paint",        "Urban"),

            // Water / Liquids
            new("chapter4", "s07_Water-clear_ASD.txt",     "Clear Water",        "Water"),
            new("chapter4", "s07_Water-turbid_ASD.txt",    "Turbid Water",       "Water"),

            // Snow / Ice
            new("chapter1", "s07_Snow-fresh_ASD.txt",      "Fresh Snow",         "Snow/Ice"),
            new("chapter1", "s07_Snow-old_ASD.txt",        "Old Snow",           "Snow/Ice"),
        }.AsReadOnly();

    // ── Download methods ──────────────────────────────────────────────────

    /// <summary>
    /// Download all curated catalog entries to <paramref name="cacheDir"/>.
    /// Already-cached files are skipped.
    /// Returns the list of successfully loaded signatures.
    /// </summary>
    public static async Task<List<SpectralSignature>> DownloadAllAsync(
        string cacheDir,
        IProgress<(int done, int total, string name)>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var result = new List<SpectralSignature>();
        int total  = CuratedCatalog.Count;
        int done   = 0;

        foreach (var entry in CuratedCatalog)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((done, total, entry.SuggestedName));

            string localPath = Path.Combine(cacheDir, entry.FileName);
            try
            {
                if (!File.Exists(localPath))
                {
                    string url = $"{BaseUrl}{entry.Chapter}/ASCII/{entry.FileName}";
                    byte[] bytes = await http.GetByteArrayAsync(url, ct);
                    await File.WriteAllBytesAsync(localPath, bytes, ct);
                }

                var sig = SpectralLibraryLoader.LoadFromUsgsAscii(localPath, entry.Category);
                // Override the USGS internal name with the friendlier catalog name
                sig = new SpectralSignature
                {
                    Name           = entry.SuggestedName,
                    Category       = sig.Category,
                    Wavelengths_um = sig.Wavelengths_um,
                    Reflectance    = sig.Reflectance,
                    Citation       = sig.Citation,
                };
                result.Add(sig);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                // Network or parse failure — skip entry, continue with rest
            }

            done++;
        }

        progress?.Report((done, total, "Done"));
        return result;
    }

    /// <summary>
    /// Download a single spectrum by catalog entry.
    /// Returns null if the download or parse fails.
    /// </summary>
    public static async Task<SpectralSignature?> DownloadOneAsync(
        CatalogEntry entry,
        string cacheDir,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        string localPath = Path.Combine(cacheDir, entry.FileName);
        try
        {
            if (!File.Exists(localPath))
            {
                string url = $"{BaseUrl}{entry.Chapter}/ASCII/{entry.FileName}";
                byte[] bytes = await http.GetByteArrayAsync(url, ct);
                await File.WriteAllBytesAsync(localPath, bytes, ct);
            }
            return SpectralLibraryLoader.LoadFromUsgsAscii(localPath, entry.Category);
        }
        catch { return null; }
    }

    // ── Chapter-discovery download ────────────────────────────────────────

    /// <summary>
    /// Crawls the USGS chapter directory index to discover all available ASCII
    /// spectrum files, then downloads each one that is not already cached.
    /// Returns all successfully loaded signatures from this chapter.
    /// </summary>
    /// <param name="chapter">Chapter identifier, e.g. "chapter1" … "chapter8".</param>
    /// <param name="category">Category tag applied to every spectrum in the chapter.</param>
    /// <param name="cacheDir">Local directory for caching downloaded files.</param>
    /// <param name="progress">
    ///   Reports <c>(done, total, name)</c>; total = 0 while crawling the index.
    /// </param>
    public static async Task<List<SpectralSignature>> DiscoverAndDownloadAsync(
        string chapter,
        string category,
        string cacheDir,
        IProgress<(int done, int total, string name)>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // ── 1. Fetch directory listing ────────────────────────────────────
        progress?.Report((0, 0, chapter));
        string indexUrl = $"{BaseUrl}{chapter}/ASCII/";
        string html;
        try   { html = await http.GetStringAsync(indexUrl, ct); }
        catch { return []; }

        // Parse relative .txt hrefs from Apache-style directory index
        var matches = System.Text.RegularExpressions.Regex.Matches(
            html, @"href=""([^""/?#][^""]*?\.txt)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var filenames = matches.Cast<System.Text.RegularExpressions.Match>()
            .Select(m => m.Groups[1].Value.TrimStart('/'))
            .Where(f => !f.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ── 2. Download each file ─────────────────────────────────────────
        var result = new List<SpectralSignature>();
        int total  = filenames.Count;
        int done   = 0;

        foreach (var filename in filenames)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((done, total, filename));

            string localPath = Path.Combine(cacheDir, filename);
            try
            {
                if (!File.Exists(localPath))
                {
                    string url    = $"{BaseUrl}{chapter}/ASCII/{filename}";
                    byte[] bytes  = await http.GetByteArrayAsync(url, ct);
                    await File.WriteAllBytesAsync(localPath, bytes, ct);
                }
                var sig = SpectralLibraryLoader.LoadFromUsgsAscii(localPath, category);
                result.Add(sig);
            }
            catch { /* parse or network failure — skip */ }

            done++;
        }

        progress?.Report((done, total, "Done"));
        return result;
    }

    /// <summary>
    /// Load all previously cached USGS spectra from <paramref name="cacheDir"/>.
    /// Unlike the old curated-only version, this reads every .txt file present.
    /// </summary>
    public static List<SpectralSignature> LoadFromCache(string cacheDir)
    {
        if (!Directory.Exists(cacheDir)) return [];

        var result = new List<SpectralSignature>();

        // First pass: load any file that matches a curated entry (preserves friendly name)
        var curatedByFile = CuratedCatalog.ToDictionary(e => e.FileName, StringComparer.OrdinalIgnoreCase);
        var loaded        = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in CuratedCatalog)
        {
            string localPath = Path.Combine(cacheDir, entry.FileName);
            if (!File.Exists(localPath)) continue;
            try
            {
                var sig = SpectralLibraryLoader.LoadFromUsgsAscii(localPath, entry.Category);
                result.Add(new SpectralSignature
                {
                    Name           = entry.SuggestedName,
                    Category       = sig.Category,
                    Wavelengths_um = sig.Wavelengths_um,
                    Reflectance    = sig.Reflectance,
                    Citation       = sig.Citation,
                });
                loaded.Add(entry.FileName);
            }
            catch { /* skip */ }
        }

        // Second pass: load remaining .txt files not already loaded above
        foreach (var file in Directory.GetFiles(cacheDir, "*.txt"))
        {
            string filename = Path.GetFileName(file);
            if (loaded.Contains(filename)) continue;

            try
            {
                string cat = GuessCategory(filename);
                var sig    = SpectralLibraryLoader.LoadFromUsgsAscii(file, cat);
                result.Add(sig);
                loaded.Add(filename);
            }
            catch { /* skip */ }
        }

        return result;
    }

    // ── Category heuristic for filenames not in curated catalog ──────────

    private static string GuessCategory(string filename)
    {
        string f = filename.ToLowerInvariant();
        if (f.Contains("veg")  || f.Contains("grass") || f.Contains("tree")  ||
            f.Contains("leaf") || f.Contains("plant") || f.Contains("crop")  ||
            f.Contains("chapt7"))                          return "Vegetation";
        if (f.Contains("soil") || f.Contains("loam")  || f.Contains("chapt2")) return "Soil";
        if (f.Contains("water")|| f.Contains("snow")  || f.Contains("chapt4")) return "Water";
        if (f.Contains("urb")  || f.Contains("asphalt")|| f.Contains("concrete") ||
            f.Contains("chapt6"))                          return "Urban";
        if (f.Contains("chapt8"))                          return "Mixture";
        if (f.Contains("chapt3"))                          return "Coating";
        if (f.Contains("chapt5"))                          return "Organic";
        return "Mineral";  // chapter1 default and fallback
    }

    // ── Catalog entry ─────────────────────────────────────────────────────

    public sealed record CatalogEntry(
        string Chapter,
        string FileName,
        string SuggestedName,
        string Category);
}
