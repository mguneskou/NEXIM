// NEXIM — Per-pixel material map.
//
// A MaterialMap stores a segmented label image alongside a spectral library
// assignment for each cluster.  Each pixel's reflectance spectrum is
// retrieved via GetReflectance(row, col, targetGrid_um).
//
// The RGB clustering is performed in pure C# to avoid any Windows-specific
// dependency in NEXIM.Core.  Image pixels are passed as a flat int[] ARGB array.
// (Each element: bits 24-31 = alpha, 16-23 = R, 8-15 = G, 0-7 = B.)
//
// Supported clustering methods: "kmeans" (default), "gmm", "dbscan".

using NEXIM.Core.Segmentation;
using NEXIM.Core.SpectralLibrary;

namespace NEXIM.Core.Rendering;

/// <summary>
/// Per-pixel spectral material assignment derived from an RGB image.
/// </summary>
public sealed class MaterialMap
{
    // ── Dimensions ────────────────────────────────────────────────────────
    public int Rows    { get; }
    public int Columns { get; }

    // ── Label map ─────────────────────────────────────────────────────────
    /// <summary>Flat label array [Rows × Columns]. Each value indexes into <see cref="Materials"/>.</summary>
    public int[] Labels { get; }

    // ── Material palette ──────────────────────────────────────────────────
    /// <summary>Per-cluster spectral signature (indexed by label).</summary>
    public SpectralSignature[] Materials { get; }

    /// <summary>
    /// Per-cluster RGB centroid colours (ARGB int, same indices as <see cref="Materials"/>).
    /// Useful for displaying a colour-coded preview.
    /// </summary>
    public int[] ClusterArgb { get; }

    // ── Constructor ───────────────────────────────────────────────────────
    private MaterialMap(int rows, int cols, int[] labels,
                        SpectralSignature[] materials, int[] clusterArgb)
    {
        Rows        = rows;
        Columns     = cols;
        Labels      = labels;
        Materials   = materials;
        ClusterArgb = clusterArgb;
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Build a MaterialMap by clustering the RGB pixels and matching each
    /// cluster centroid to the nearest entry in <paramref name="library"/>.
    /// </summary>
    /// <param name="argbPixels">
    ///   Flat ARGB pixel array [rows × cols].
    ///   Each int: bits 24-31 = A, 16-23 = R, 8-15 = G, 0-7 = B.
    /// </param>
    /// <param name="imageWidth">Width of the pixel grid (= Columns).</param>
    /// <param name="imageHeight">Height of the pixel grid (= Rows).</param>
    /// <param name="library">Spectral signature library to match against.</param>
    /// <param name="clusterCount">Number of clusters for K-means/GMM.</param>
    /// <param name="method">"kmeans" | "gmm" | "dbscan".</param>
    /// <param name="dbscanEpsilon">Epsilon for DBSCAN (feature-space distance, 0–1).</param>
    /// <param name="dbscanMinPts">MinPts for DBSCAN.</param>
    public static MaterialMap FromArgbPixels(
        int[]                        argbPixels,
        int                          imageWidth,
        int                          imageHeight,
        IReadOnlyList<SpectralSignature> library,
        int                          clusterCount    = 6,
        string                       method          = "kmeans",
        double                       dbscanEpsilon   = 0.10,
        int                          dbscanMinPts    = 5)
    {
        ArgumentNullException.ThrowIfNull(argbPixels);
        ArgumentNullException.ThrowIfNull(library);

        if (imageWidth < 1 || imageHeight < 1)
            throw new ArgumentException("Image dimensions must be positive.");
        if (library.Count == 0)
            throw new ArgumentException("Library must not be empty.", nameof(library));

        int totalPixels = imageWidth * imageHeight;
        if (argbPixels.Length < totalPixels)
            throw new ArgumentException("argbPixels array is too short.", nameof(argbPixels));

        // ── Build feature vectors (R/255, G/255, B/255) ───────────────────
        var pixels = new PixelFeature[totalPixels];
        for (int idx = 0; idx < totalPixels; idx++)
        {
            int argb = argbPixels[idx];
            double r = ((argb >> 16) & 0xFF) / 255.0;
            double g = ((argb >>  8) & 0xFF) / 255.0;
            double b = (argb         & 0xFF) / 255.0;
            pixels[idx] = new PixelFeature
            {
                Row     = idx / imageWidth,
                Column  = idx % imageWidth,
                Features = [r, g, b],
            };
        }

        // ── Run segmentation ──────────────────────────────────────────────
        ISegmenter segmenter = method.ToLowerInvariant() switch
        {
            "gmm"    => new GmmSegmenter(k: clusterCount, maxIterations: 100),
            "dbscan" => new DbscanSegmenter(epsilon: dbscanEpsilon, minPoints: dbscanMinPts),
            _        => new KMeansSegmenter(k: clusterCount, maxIterations: 100),
        };

        var segResult = segmenter.Segment(pixels);

        // ── Compute cluster centroids from labels ─────────────────────────
        // Prefer centroids provided by the segmenter, else compute from labels.
        double[][] centroids;
        int effectiveK;

        if (segResult.Centroids != null && segResult.Centroids.Length > 0)
        {
            centroids  = segResult.Centroids;
            effectiveK = centroids.Length;
        }
        else
        {
            effectiveK = segResult.ClassCount;
            if (effectiveK <= 0) effectiveK = 1;

            centroids  = new double[effectiveK][];
            var counts = new int[effectiveK];

            for (int i = 0; i < effectiveK; i++) centroids[i] = new double[3];
            for (int idx = 0; idx < totalPixels; idx++)
            {
                int lbl = Math.Max(0, segResult.Labels[idx]); // clamp noise label -1 → 0
                if (lbl >= effectiveK) lbl = effectiveK - 1;
                centroids[lbl][0] += pixels[idx].Features[0];
                centroids[lbl][1] += pixels[idx].Features[1];
                centroids[lbl][2] += pixels[idx].Features[2];
                counts[lbl]++;
            }
            for (int k = 0; k < effectiveK; k++)
            {
                double cnt = Math.Max(1, counts[k]);
                centroids[k][0] /= cnt;
                centroids[k][1] /= cnt;
                centroids[k][2] /= cnt;
            }
        }

        // ── Convert centroids to RgbColor and find material match ─────────
        var centroidColors = new RgbColor[effectiveK];
        for (int k = 0; k < effectiveK; k++)
        {
            centroidColors[k] = new RgbColor(
                (byte)Math.Clamp((int)(centroids[k][0] * 255), 0, 255),
                (byte)Math.Clamp((int)(centroids[k][1] * 255), 0, 255),
                (byte)Math.Clamp((int)(centroids[k][2] * 255), 0, 255));
        }

        var materials = MaterialMapper.AssignMaterials(centroidColors, library);

        // ── Build int[] ARGB for cluster colours ──────────────────────────
        var clusterArgb = new int[effectiveK];
        for (int k = 0; k < effectiveK; k++)
        {
            var c = centroidColors[k];
            clusterArgb[k] = (255 << 24) | (c.R << 16) | (c.G << 8) | c.B;
        }

        // ── Map labels, clamping noise label -1 → 0 ──────────────────────
        var labels = new int[totalPixels];
        for (int idx = 0; idx < totalPixels; idx++)
        {
            int lbl = segResult.Labels[idx];
            labels[idx] = (lbl < 0 || lbl >= effectiveK) ? 0 : lbl;
        }

        return new MaterialMap(imageHeight, imageWidth, labels, materials, clusterArgb);
    }

    // ── Data access ───────────────────────────────────────────────────────

    /// <summary>
    /// Return the spectral reflectance for pixel (row, col), interpolated
    /// to <paramref name="targetWavelengths_um"/>.
    /// </summary>
    public double[] GetReflectance(int row, int col, double[] targetWavelengths_um)
    {
        int label = Labels[row * Columns + col];
        return Materials[label].InterpolateTo(targetWavelengths_um);
    }

    /// <summary>
    /// Get the label index for a specific pixel.
    /// </summary>
    public int GetLabel(int row, int col) => Labels[row * Columns + col];
}
