// NEXIM — Phase 11 unit tests: .nxi round-trip, ENVI export, CSV export.

using System.Text;
using NEXIM.Core.IO;
using NEXIM.Core.Models;

namespace NEXIM.Tests.IO;

// ─────────────────────────────────────────────────────────────────────────────
// Helpers shared across test classes
// ─────────────────────────────────────────────────────────────────────────────

file static class CubeFactory
{
    /// <summary>
    /// Build a BIL cube with cube[r*bands+b][c] = (r+1)*100 + (b+1)*10 + c.
    /// </summary>
    public static float[][] Make(int rows, int bands, int columns)
    {
        var cube = new float[rows * bands][];
        for (int r = 0; r < rows; r++)
            for (int b = 0; b < bands; b++)
            {
                var slice = new float[columns];
                for (int c = 0; c < columns; c++)
                    slice[c] = (r + 1) * 100f + (b + 1) * 10f + c;
                cube[r * bands + b] = slice;
            }
        return cube;
    }

    public static double[] Wavelengths(int bands, double start = 0.45, double step = 0.05)
        => Enumerable.Range(0, bands).Select(i => start + i * step).ToArray();
}

// ─────────────────────────────────────────────────────────────────────────────
// NxiWriter / NxiReader tests
// ─────────────────────────────────────────────────────────────────────────────

public class NxiRoundTripTests : IDisposable
{
    readonly string _tmpDir;
    public NxiRoundTripTests() => _tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true); }

    string TmpFile(string name) { Directory.CreateDirectory(_tmpDir); return Path.Combine(_tmpDir, name); }

    // ── write + read ──────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_SmallCube_DataPreserved()
    {
        const int rows = 3, bands = 4, cols = 5;
        float[][] cube = CubeFactory.Make(rows, bands, cols);
        double[]  wl   = CubeFactory.Wavelengths(bands);
        string    path = TmpFile("test.nxi");

        NxiWriter.Write(path, cube, rows, bands, cols, wl);
        var result = NxiReader.Read(path);

        Assert.Equal(rows,  (int)result.Header.Rows);
        Assert.Equal(bands, (int)result.Header.Bands);
        Assert.Equal(cols,  (int)result.Header.Columns);

        for (int r = 0; r < rows; r++)
            for (int b = 0; b < bands; b++)
                for (int c = 0; c < cols; c++)
                    Assert.Equal(cube[r * bands + b][c],
                                 result.Cube[r * bands + b][c]);
    }

    [Fact]
    public void RoundTrip_Header_MagicAndVersion_Correct()
    {
        const int rows = 2, bands = 3, cols = 4;
        string path = TmpFile("hdr.nxi");
        NxiWriter.Write(path, CubeFactory.Make(rows, bands, cols), rows, bands, cols,
                        CubeFactory.Wavelengths(bands));
        var r = NxiReader.Read(path);
        Assert.Equal(NxiHeader.ExpectedMagic,   r.Header.Magic);
        Assert.Equal(NxiHeader.CurrentVersion,  r.Header.Version);
    }

    [Fact]
    public void RoundTrip_Metadata_WavelengthsPreserved()
    {
        const int bands = 5;
        double[] wl   = CubeFactory.Wavelengths(bands);
        var meta = new NxiMetadata
        {
            SceneName      = "TestScene",
            Wavelengths_um = wl,
            Description    = "unit test",
        };
        string path = TmpFile("meta.nxi");
        NxiWriter.Write(path, CubeFactory.Make(1, bands, 1), 1, bands, 1, wl, meta);

        var result = NxiReader.Read(path);
        Assert.Equal("TestScene", result.Metadata.SceneName);
        Assert.Equal(bands, result.Metadata.Wavelengths_um.Length);
        for (int b = 0; b < bands; b++)
            Assert.Equal(wl[b], result.Metadata.Wavelengths_um[b], 10);
    }

    [Fact]
    public void RoundTrip_CrcValidation_PassesForValidFile()
    {
        string path = TmpFile("crc_ok.nxi");
        // should not throw
        NxiWriter.Write(path, CubeFactory.Make(2, 2, 2), 2, 2, 2,
                        CubeFactory.Wavelengths(2));
        NxiReader.Read(path);  // throws on CRC mismatch
    }

    [Fact]
    public void Read_CorruptedFile_ThrowsInvalidDataException()
    {
        string path = TmpFile("corrupt.nxi");
        NxiWriter.Write(path, CubeFactory.Make(2, 2, 3), 2, 2, 3,
                        CubeFactory.Wavelengths(2));

        // Flip a byte in the data section
        byte[] bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        Assert.Throws<InvalidDataException>(() => NxiReader.Read(path));
    }

    [Fact]
    public void Read_BadMagic_ThrowsInvalidDataException()
    {
        string path = TmpFile("bad_magic.nxi");
        NxiWriter.Write(path, CubeFactory.Make(1, 1, 1), 1, 1, 1,
                        CubeFactory.Wavelengths(1));

        byte[] bytes = File.ReadAllBytes(path);
        bytes[0] = 0xAA; bytes[1] = 0xBB;  // corrupt magic
        File.WriteAllBytes(path, bytes);

        Assert.Throws<InvalidDataException>(() => NxiReader.Read(path));
    }

    [Fact]
    public void Write_LargerCube_RoundTripPreservesShape()
    {
        const int rows = 10, bands = 8, cols = 16;
        float[][] cube = CubeFactory.Make(rows, bands, cols);
        double[]  wl   = CubeFactory.Wavelengths(bands);
        string    path = TmpFile("large.nxi");

        NxiWriter.Write(path, cube, rows, bands, cols, wl);
        var result = NxiReader.Read(path);

        Assert.Equal(rows * bands, result.Cube.Length);
        Assert.Equal(cols,         result.Cube[0].Length);
    }

    [Fact]
    public void Write_NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => NxiWriter.Write(null!, CubeFactory.Make(1, 1, 1), 1, 1, 1,
                                  CubeFactory.Wavelengths(1)));
    }

    [Fact]
    public void Write_WavelengthsLengthMismatch_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => NxiWriter.Write(TmpFile("err.nxi"), CubeFactory.Make(1, 3, 1),
                                  1, 3, 1, CubeFactory.Wavelengths(2)));  // 2 ≠ 3
    }

    [Fact]
    public void ReadFromStream_ValidStream_ReturnsResult()
    {
        string path = TmpFile("stream.nxi");
        NxiWriter.Write(path, CubeFactory.Make(2, 3, 4), 2, 3, 4,
                        CubeFactory.Wavelengths(3));
        using var ms = new MemoryStream(File.ReadAllBytes(path));
        var result = NxiReader.ReadFromStream(ms);
        Assert.Equal(2u, result.Header.Rows);
        Assert.Equal(3u, result.Header.Bands);
        Assert.Equal(4u, result.Header.Columns);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// EnviExporter tests
// ─────────────────────────────────────────────────────────────────────────────

public class EnviExporterTests : IDisposable
{
    readonly string _tmpDir;
    public EnviExporterTests() => _tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true); }

    string TmpFile(string name) { Directory.CreateDirectory(_tmpDir); return Path.Combine(_tmpDir, name); }

    [Fact]
    public void Export_HeaderContainsCorrectDimensions()
    {
        const int rows = 3, bands = 4, cols = 5;
        string hdr = TmpFile("test.hdr");
        EnviExporter.Export(hdr, CubeFactory.Make(rows, bands, cols),
                            rows, bands, cols, CubeFactory.Wavelengths(bands));

        var h = EnviExporter.ReadHeader(hdr);
        Assert.Equal(cols,  h.Samples);
        Assert.Equal(rows,  h.Lines);
        Assert.Equal(bands, h.Bands);
    }

    [Fact]
    public void Export_HeaderDataTypeIsFloat32()
    {
        string hdr = TmpFile("dtype.hdr");
        EnviExporter.Export(hdr, CubeFactory.Make(2, 3, 4), 2, 3, 4,
                            CubeFactory.Wavelengths(3));
        Assert.Equal(4, EnviExporter.ReadHeader(hdr).DataType);  // 4 = float32
    }

    [Fact]
    public void Export_HeaderInterleaveIsBil()
    {
        string hdr = TmpFile("il.hdr");
        EnviExporter.Export(hdr, CubeFactory.Make(2, 3, 4), 2, 3, 4,
                            CubeFactory.Wavelengths(3));
        Assert.Equal("bil", EnviExporter.ReadHeader(hdr).Interleave);
    }

    [Fact]
    public void Export_ImgFileSize_CorrectByteCount()
    {
        const int rows = 3, bands = 4, cols = 5;
        string hdr = TmpFile("size.hdr");
        string img = TmpFile("size.img");
        EnviExporter.Export(hdr, CubeFactory.Make(rows, bands, cols),
                            rows, bands, cols, CubeFactory.Wavelengths(bands), imgPath: img);

        long expected = (long)rows * bands * cols * sizeof(float);
        Assert.Equal(expected, new FileInfo(img).Length);
    }

    [Fact]
    public void Export_ImgData_RoundTripPreservesValues()
    {
        const int rows = 2, bands = 3, cols = 4;
        float[][] cube = CubeFactory.Make(rows, bands, cols);
        string hdr = TmpFile("rt.hdr");
        string img = TmpFile("rt.img");
        EnviExporter.Export(hdr, cube, rows, bands, cols,
                            CubeFactory.Wavelengths(bands), imgPath: img);

        // Read raw bytes and compare
        byte[] bytes = File.ReadAllBytes(img);
        int idx = 0;
        for (int r = 0; r < rows; r++)
            for (int b = 0; b < bands; b++)
                for (int c = 0; c < cols; c++)
                {
                    float stored = BitConverter.ToSingle(bytes, idx);
                    Assert.Equal(cube[r * bands + b][c], stored);
                    idx += 4;
                }
    }

    [Fact]
    public void Export_WavelengthsInNmAppearInHeader()
    {
        double[] wl  = { 0.45, 0.55, 0.65 };
        string   hdr = TmpFile("wl.hdr");
        EnviExporter.Export(hdr, CubeFactory.Make(1, 3, 1), 1, 3, 1, wl);
        string content = File.ReadAllText(hdr);
        Assert.Contains("450", content);   // 0.45 µm = 450 nm
        Assert.Contains("550", content);
        Assert.Contains("650", content);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// CsvExporter tests
// ─────────────────────────────────────────────────────────────────────────────

public class CsvExporterTests : IDisposable
{
    readonly string _tmpDir;
    public CsvExporterTests() => _tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, true); }

    string TmpFile(string name) { Directory.CreateDirectory(_tmpDir); return Path.Combine(_tmpDir, name); }

    [Fact]
    public void ExportLongForm_RowCountIsRowsTimesBandsByColumns()
    {
        const int rows = 2, bands = 3, cols = 4;
        string path = TmpFile("long.csv");
        CsvExporter.ExportLongForm(path, CubeFactory.Make(rows, bands, cols),
                                   rows, bands, cols, CubeFactory.Wavelengths(bands));

        var lines = File.ReadAllLines(path);
        // 1 header + rows*bands*cols data rows
        Assert.Equal(1 + rows * bands * cols, lines.Length);
    }

    [Fact]
    public void ExportLongForm_HeaderLineIsCorrect()
    {
        string path = TmpFile("lhdr.csv");
        CsvExporter.ExportLongForm(path, CubeFactory.Make(1, 1, 1), 1, 1, 1,
                                   CubeFactory.Wavelengths(1));
        Assert.Equal("Row,Column,Band,Wavelength_um,Value", File.ReadAllLines(path)[0]);
    }

    [Fact]
    public void ExportLongForm_ValuesUseInvariantCulture()
    {
        // Value 1.5 should appear as "1.5", not "1,5"
        var cube = new float[][] { new[] { 1.5f } };
        string path = TmpFile("inv.csv");
        CsvExporter.ExportLongForm(path, cube, 1, 1, 1, new[] { 0.55 });
        string content = File.ReadAllText(path);
        Assert.Contains("1.5", content);
    }

    [Fact]
    public void ExportWideForm_RowCountIsRowsTimesColumns()
    {
        const int rows = 3, bands = 4, cols = 5;
        string path = TmpFile("wide.csv");
        CsvExporter.ExportWideForm(path, CubeFactory.Make(rows, bands, cols),
                                   rows, bands, cols, CubeFactory.Wavelengths(bands));

        var lines = File.ReadAllLines(path);
        Assert.Equal(1 + rows * cols, lines.Length);
    }

    [Fact]
    public void ExportWideForm_ColumnCountIsBandsPlusTwo()
    {
        const int rows = 2, bands = 5, cols = 3;
        string path = TmpFile("widecols.csv");
        CsvExporter.ExportWideForm(path, CubeFactory.Make(rows, bands, cols),
                                   rows, bands, cols, CubeFactory.Wavelengths(bands));

        var lines   = File.ReadAllLines(path);
        int nCols   = lines[1].Split(',').Length;
        Assert.Equal(2 + bands, nCols);  // Row, Column, Band_0..Band_{N-1}
    }

    [Fact]
    public void ExportSpectralMean_RowCountIsRowsTimesColumns()
    {
        const int rows = 4, bands = 5, cols = 6;
        string path = TmpFile("mean.csv");
        CsvExporter.ExportSpectralMean(path, CubeFactory.Make(rows, bands, cols),
                                       rows, bands, cols);
        var lines = File.ReadAllLines(path);
        Assert.Equal(1 + rows * cols, lines.Length);
    }

    [Fact]
    public void ExportSpectralMean_MeanValue_Correct()
    {
        // Cube: row 0, col 0 has values across bands = 110, 120, 130 → mean = 120
        const int bands = 3, cols = 1;
        float[][] cube = CubeFactory.Make(1, bands, cols);
        // cube[0*3+0][0]=110, cube[0*3+1][0]=120, cube[0*3+2][0]=130
        string path = TmpFile("meanval.csv");
        CsvExporter.ExportSpectralMean(path, cube, 1, bands, cols);
        string[] lines = File.ReadAllLines(path);
        // Data line: "0,0,120"
        Assert.Contains("120", lines[1]);
    }

    [Fact]
    public void ExportLongForm_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => CsvExporter.ExportLongForm(null!, CubeFactory.Make(1, 1, 1),
                                             1, 1, 1, CubeFactory.Wavelengths(1)));
    }
}
