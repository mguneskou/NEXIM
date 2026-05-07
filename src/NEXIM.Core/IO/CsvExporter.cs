// NEXIM — CSV exporter.
// Writes per-pixel spectral radiance (or DN) data as comma-separated values.
// Intended for validation and quick inspection in Excel / Python / MATLAB.
//
// Output format:
//   Row,Column,Band,Wavelength_um,Value
//   0,0,0,0.4500,12.345
//   ...
//
// For large cubes a "band-averaged" summary mode is also provided that writes
// one row per spatial pixel: Row,Column,Band0,Band1,...,BandN-1

using System.Globalization;
using System.Text;

namespace NEXIM.Core.IO;

/// <summary>
/// Exports hyperspectral cube data to CSV files for validation.
/// </summary>
public static class CsvExporter
{
    // ── public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Write the full cube as a long-form CSV: one row per (row, band, column)
    /// sample.  Suitable for small cubes or when every sample is needed.
    /// </summary>
    /// <param name="path">Destination CSV file path.</param>
    /// <param name="cube">
    ///   BIL cube: cube[r * bands + b] is the float32 slice for row r, band b.
    /// </param>
    /// <param name="rows">Spatial row count.</param>
    /// <param name="bands">Spectral band count.</param>
    /// <param name="columns">Spatial column count.</param>
    /// <param name="wavelengths_um">Centre wavelengths µm (length == bands).</param>
    public static void ExportLongForm(
        string path,
        float[][] cube,
        int rows, int bands, int columns,
        double[] wavelengths_um)
    {
        Validate(cube, rows, bands, columns, wavelengths_um);

        var ci = CultureInfo.InvariantCulture;
        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        writer.WriteLine("Row,Column,Band,Wavelength_um,Value");

        for (int r = 0; r < rows; r++)
            for (int b = 0; b < bands; b++)
            {
                float[] slice = cube[r * bands + b];
                double  wl    = wavelengths_um[b];
                for (int c = 0; c < columns; c++)
                    writer.WriteLine(FormattableString.Invariant(
                        $"{r},{c},{b},{wl:F6},{slice[c]:G9}"));
            }
    }

    /// <summary>
    /// Write a wide-form CSV: one row per spatial pixel, with each band as a
    /// separate column.  Header: Row,Column,Band_0,...,Band_{N-1}
    /// </summary>
    public static void ExportWideForm(
        string path,
        float[][] cube,
        int rows, int bands, int columns,
        double[] wavelengths_um)
    {
        Validate(cube, rows, bands, columns, wavelengths_um);

        var ci = CultureInfo.InvariantCulture;
        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);

        // header
        var header = new StringBuilder("Row,Column");
        for (int b = 0; b < bands; b++)
            header.Append(ci, $",Band_{b}_{wavelengths_um[b]:F4}um");
        writer.WriteLine(header.ToString());

        // data rows
        var line = new StringBuilder(bands * 15 + 20);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                line.Clear();
                line.Append(r);
                line.Append(',');
                line.Append(c);
                for (int b = 0; b < bands; b++)
                {
                    line.Append(',');
                    line.Append(cube[r * bands + b][c].ToString("G9", ci));
                }
                writer.WriteLine(line.ToString());
            }
    }

    /// <summary>
    /// Write a spectrally-averaged summary CSV: one row per spatial pixel,
    /// columns = Row, Column, MeanValue.  Useful for a quick look at scene
    /// intensity without writing the full cube.
    /// </summary>
    public static void ExportSpectralMean(
        string path,
        float[][] cube,
        int rows, int bands, int columns)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(cube);
        if (rows <= 0 || bands <= 0 || columns <= 0)
            throw new ArgumentOutOfRangeException("rows/bands/columns must be > 0");
        if (cube.Length != rows * bands)
            throw new ArgumentException("cube must have rows*bands slices.");

        var ci = CultureInfo.InvariantCulture;
        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        writer.WriteLine("Row,Column,MeanValue");

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                double sum = 0.0;
                for (int b = 0; b < bands; b++)
                    sum += cube[r * bands + b][c];
                double mean = sum / bands;
                writer.WriteLine(FormattableString.Invariant($"{r},{c},{mean:G9}"));
            }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    static void Validate(float[][] cube, int rows, int bands, int columns,
                         double[] wavelengths_um)
    {
        ArgumentNullException.ThrowIfNull(cube);
        ArgumentNullException.ThrowIfNull(wavelengths_um);
        if (rows <= 0 || bands <= 0 || columns <= 0)
            throw new ArgumentOutOfRangeException("rows/bands/columns must be > 0");
        if (wavelengths_um.Length != bands)
            throw new ArgumentException("wavelengths_um.Length must equal bands.");
        if (cube.Length != rows * bands)
            throw new ArgumentException("cube must have rows*bands slices (cube[r*bands+b]).");
    }
}
