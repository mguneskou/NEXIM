// NEXIM — k-table library: loads and caches all gas species tables.
// Manages the full set of KDistributionTable objects needed by the CKD solver.

namespace NEXIM.Core.Atmospheric.CKD;

/// <summary>
/// Registry of all pre-computed k-distribution tables loaded from the
/// <c>data/hitran_kdist/</c> directory.
///
/// Tables are keyed by (species, spectral band index) and are lazily loaded
/// on first access. Thread-safe via locking on the internal dictionary.
///
/// k-table data is derived from HITRAN2020:
///   Gordon et al. (2022) JQSRT 277:107949. doi:10.1016/j.jqsrt.2021.107949
/// </summary>
public sealed class KTableLibrary
{
    private readonly string _dataDirectory;
    private readonly Dictionary<(GasSpecies, int), KDistributionTable> _cache = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Initialise the library pointing at the directory that contains .ktbl files.
    /// </summary>
    /// <param name="dataDirectory">
    /// Absolute or relative path to the directory containing .ktbl files.
    /// File naming convention: <c>{Species}_{BandIndex:D4}.ktbl</c>, e.g.
    /// <c>H2O_0001.ktbl</c>, <c>CO2_0001.ktbl</c>.
    /// </param>
    public KTableLibrary(string dataDirectory)
    {
        _dataDirectory = dataDirectory ?? string.Empty;
    }

    /// <summary>
    /// Get the k-distribution table for a specific gas species and spectral band.
    /// Loads and caches on first access.
    /// </summary>
    /// <param name="species">Gas species.</param>
    /// <param name="bandIndex">Zero-based spectral band index.</param>
    /// <returns>The loaded k-table.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the .ktbl file does not exist.</exception>
    public KDistributionTable GetTable(GasSpecies species, int bandIndex)
    {
        var key = (species, bandIndex);
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            string fileName = $"{species}_{bandIndex:D4}.ktbl";
            string filePath = Path.Combine(_dataDirectory, fileName);

            if (!File.Exists(filePath))
                throw new FileNotFoundException(
                    $"k-table not found for {species} band {bandIndex}. " +
                    $"Expected: {filePath}. " +
                    $"Run NEXIM.LutGen to generate k-tables from HITRAN data.", filePath);

            var table = KTableLoader.LoadFromFile(filePath);
            _cache[key] = table;
            return table;
        }
    }

    /// <summary>
    /// Pre-load all .ktbl files in the data directory into the cache.
    /// Useful at startup to surface any corrupt files before simulation begins.
    /// </summary>
    public void PreloadAll()
    {
        if (!Directory.Exists(_dataDirectory)) return;
        foreach (string file in Directory.EnumerateFiles(_dataDirectory, "*.ktbl"))
        {
            var table = KTableLoader.LoadFromFile(file);
            var key = (table.Species, DeriveIndexFromPath(file));
            lock (_lock) _cache.TryAdd(key, table);
        }
    }

    /// <summary>
    /// Returns <c>true</c> if a .ktbl file exists for the given species and band index.
    /// Does not load or cache the table.
    /// </summary>
    public bool HasTable(GasSpecies species, int bandIndex)
    {
        if (string.IsNullOrEmpty(_dataDirectory)) return false;
        lock (_lock)
        {
            if (_cache.ContainsKey((species, bandIndex))) return true;
        }
        string fileName = $"{species}_{bandIndex:D4}.ktbl";
        string filePath = Path.Combine(_dataDirectory, fileName);
        return File.Exists(filePath);
    }

    private static int DeriveIndexFromPath(string filePath)
    {
        // Filename: {Species}_{Index:D4}.ktbl
        string stem = Path.GetFileNameWithoutExtension(filePath);
        int underscorePos = stem.LastIndexOf('_');
        if (underscorePos >= 0 && int.TryParse(stem[(underscorePos + 1)..], out int idx))
            return idx;
        return 0;
    }
}
