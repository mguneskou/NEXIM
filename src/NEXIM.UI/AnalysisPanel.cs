// NEXIM — AnalysisPanel.cs
// Analysis tab: load .nxi or ENVI .img, display RGB composite / single-band /
// spectral index / PCA / RX anomaly, and click-to-plot spectral profiles.
//
// Layout (TableLayoutPanel, 1 col × 4 rows):
//   Row 0 (36 px)  — file load bar
//   Row 1 (52 px)  — display-mode controls + render button
//   Row 2 (fill)   — SplitContainer: PictureBox (left) + OxyPlot (right)
//   Row 3 (22 px)  — status / metadata label

using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NEXIM.Core.Analysis;
using NEXIM.Core.IO;
using NEXIM.Core.Models;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace NEXIM.UI;

enum DisplayMode { RgbComposite, SingleBand, SpectralIndex, Pca, RxAnomaly }

public sealed class AnalysisPanel : UserControl
{
    // ── state ─────────────────────────────────────────────────────────────
    HypercubeData? _cube;
    int            _spectrumCount;

    // ── controls ──────────────────────────────────────────────────────────
    Button        _btnBrowse = null!;
    Label         _lblFile   = null!;

    ComboBox      _cmbMode  = null!;
    Panel         _pnlRgb   = null!;
    Panel         _pnlSingle = null!;
    Panel         _pnlIndex  = null!;
    Panel         _pnlPca    = null!;

    ComboBox      _cmbR = null!, _cmbG = null!, _cmbB = null!;
    ComboBox      _cmbSingle = null!;
    ComboBox      _cmbIdx    = null!;
    NumericUpDown _nudPca    = null!;

    Button        _btnRender       = null!;
    Button        _btnClearSpectra = null!;

    PictureBox    _pic       = null!;
    PlotView      _plotView  = null!;
    PlotModel     _plotModel = null!;
    Label         _lblInfo   = null!;

    // ── spectrum colour palette (MATLAB-style) ────────────────────────────
    static readonly OxyColor[] Palette =
    {
        OxyColor.FromRgb(0,   114, 189),
        OxyColor.FromRgb(217,  83,  25),
        OxyColor.FromRgb(237, 177,  32),
        OxyColor.FromRgb(126,  47, 142),
        OxyColor.FromRgb(119, 172,  48),
        OxyColor.FromRgb( 77, 190, 238),
        OxyColor.FromRgb(162,  20,  47),
        OxyColor.FromRgb(  0,   0,   0),
    };

    // ── constructor ───────────────────────────────────────────────────────
    public AnalysisPanel()
    {
        Font = new Font("Segoe UI", 9f, FontStyle.Regular);
        BuildLayout();
    }

    // ── public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Load a cube (e.g. from the simulation result) and render the default
    /// RGB composite.
    /// </summary>
    public void LoadCube(HypercubeData cube)
    {
        _cube = cube;
        _lblFile.Text = cube.FileName.Length > 0 ? cube.FileName : "(from simulation)";
        PopulateBandCombos();
        _ = RenderAsync();          // fire-and-forget; updates UI on UI thread
        UpdateStatusBar();
    }

    // ── layout ────────────────────────────────────────────────────────────

    void BuildLayout()
    {
        var tbl = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 4,
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));

        tbl.Controls.Add(BuildFileRow(),    0, 0);
        tbl.Controls.Add(BuildControlRow(), 0, 1);
        tbl.Controls.Add(BuildMainArea(),   0, 2);

        _lblInfo = new Label
        {
            Dock      = DockStyle.Fill,
            Text      = "Load a .nxi or .img file to begin",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding   = new Padding(6, 0, 0, 0),
            Font      = new Font("Segoe UI", 8.5f, FontStyle.Regular),
        };
        tbl.Controls.Add(_lblInfo, 0, 3);

        Controls.Add(tbl);
    }

    Panel BuildFileRow()
    {
        _btnBrowse = new Button
        {
            Text     = "Browse…",
            Width    = 80,
            Height   = 26,
            Location = new Point(6, 5),
            Anchor   = AnchorStyles.Left | AnchorStyles.Top,
        };
        _btnBrowse.Click += BtnBrowse_Click;

        _lblFile = new Label
        {
            Text         = "No file loaded",
            Location     = new Point(92, 9),
            Height       = 18,
            AutoSize     = false,
            AutoEllipsis = true,
            Anchor       = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };

        var pnl = new Panel { Dock = DockStyle.Fill };
        pnl.Controls.AddRange(new Control[] { _btnBrowse, _lblFile });
        pnl.Resize += (_, _) => _lblFile.Width = pnl.ClientSize.Width - 96;
        return pnl;
    }

    Panel BuildControlRow()
    {
        var flow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            AutoSize      = false,
            Padding       = new Padding(4, 10, 4, 0),
        };

        // Display mode label + combo
        flow.Controls.Add(MakeLabel("Display:"));
        _cmbMode = MakeCombo(148);
        _cmbMode.Items.AddRange(new object[]
        {
            "RGB Composite", "Single Band", "Spectral Index", "PCA (PC1–3)", "RX Anomaly",
        });
        _cmbMode.SelectedIndex = 0;
        _cmbMode.SelectedIndexChanged += CmbMode_Changed;
        flow.Controls.Add(_cmbMode);

        // ── RGB sub-panel ─────────────────────────────────────────────────
        _cmbR      = MakeCombo(90);
        _cmbG      = MakeCombo(90);
        _cmbB      = MakeCombo(90);
        _pnlRgb    = new Panel { Width = 350, Height = 30 };

        var lblR   = new Label { Text = "R:", Location = new Point(  2, 5), AutoSize = true };
        var lblG   = new Label { Text = "G:", Location = new Point(118, 5), AutoSize = true };
        var lblB   = new Label { Text = "B:", Location = new Point(234, 5), AutoSize = true };
        _cmbR.Location = new Point( 18, 2);
        _cmbG.Location = new Point(134, 2);
        _cmbB.Location = new Point(250, 2);
        _pnlRgb.Controls.AddRange(new Control[] { lblR, lblG, lblB, _cmbR, _cmbG, _cmbB });
        flow.Controls.Add(_pnlRgb);

        // ── Single band sub-panel ─────────────────────────────────────────
        _cmbSingle  = MakeCombo(110);
        _pnlSingle  = new Panel { Width = 155, Height = 30, Visible = false };
        _pnlSingle.Controls.Add(new Label { Text = "Band:", Location = new Point(0, 5), AutoSize = true });
        _cmbSingle.Location = new Point(44, 2);
        _pnlSingle.Controls.Add(_cmbSingle);
        flow.Controls.Add(_pnlSingle);

        // ── Spectral index sub-panel ──────────────────────────────────────
        _cmbIdx    = MakeCombo(90);
        _cmbIdx.Items.AddRange(new object[] { "NDVI", "NDWI", "NDRE", "NDBI", "SAVI", "EVI", "CAI" });
        _cmbIdx.SelectedIndex = 0;
        _pnlIndex  = new Panel { Width = 148, Height = 30, Visible = false };
        _pnlIndex.Controls.Add(new Label { Text = "Index:", Location = new Point(0, 5), AutoSize = true });
        _cmbIdx.Location = new Point(48, 2);
        _pnlIndex.Controls.Add(_cmbIdx);
        flow.Controls.Add(_pnlIndex);

        // ── PCA sub-panel ─────────────────────────────────────────────────
        _nudPca   = new NumericUpDown
        {
            Width = 48, Minimum = 2, Maximum = 12, Value = 5,
            Location = new Point(34, 2),
        };
        _pnlPca   = new Panel { Width = 90, Height = 30, Visible = false };
        _pnlPca.Controls.Add(new Label { Text = "PCs:", Location = new Point(0, 5), AutoSize = true });
        _pnlPca.Controls.Add(_nudPca);
        flow.Controls.Add(_pnlPca);

        // ── Render + Clear buttons ────────────────────────────────────────
        _btnRender = new Button { Text = "▶ Render", Width = 84, Height = 26 };
        _btnRender.Click += (_, _) => _ = RenderAsync();
        flow.Controls.Add(_btnRender);

        _btnClearSpectra = new Button { Text = "✕ Clear Spectra", Width = 112, Height = 26 };
        _btnClearSpectra.Click += BtnClear_Click;
        flow.Controls.Add(_btnClearSpectra);

        var pnl = new Panel { Dock = DockStyle.Fill };
        pnl.Controls.Add(flow);
        return pnl;
    }

    SplitContainer BuildMainArea()
    {
        _pic = new PictureBox
        {
            Dock     = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            Cursor   = Cursors.Cross,
        };
        _pic.MouseClick += Pic_MouseClick;

        _plotModel = new PlotModel
        {
            Title         = "Spectral Profile",
            Background    = OxyColors.White,
            TitleFontSize = 11,
        };
        InitSpectralAxes();

        _plotView = new PlotView { Model = _plotModel, Dock = DockStyle.Fill };

        var split = new SplitContainer
        {
            Dock              = DockStyle.Fill,
            Orientation       = Orientation.Vertical,
            SplitterDistance  = 620,
        };
        split.Panel1.Controls.Add(_pic);
        split.Panel2.Controls.Add(_plotView);
        return split;
    }

    // ── Mode switching ────────────────────────────────────────────────────

    void CmbMode_Changed(object? sender, EventArgs e)
    {
        var mode       = (DisplayMode)_cmbMode.SelectedIndex;
        _pnlRgb.Visible    = mode == DisplayMode.RgbComposite;
        _pnlSingle.Visible = mode == DisplayMode.SingleBand;
        _pnlIndex.Visible  = mode == DisplayMode.SpectralIndex;
        _pnlPca.Visible    = mode == DisplayMode.Pca;
    }

    // ── Event handlers ────────────────────────────────────────────────────

    void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Open hyperspectral cube",
            Filter = "NEXIM native (*.nxi)|*.nxi|ENVI image (*.img)|*.img" +
                     "|ENVI header (*.hdr)|*.hdr|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _ = LoadFileAsync(dlg.FileName);
    }

    void BtnClear_Click(object? sender, EventArgs e)
    {
        _plotModel.Series.Clear();
        _spectrumCount = 0;
        InitSpectralAxes();
        _plotView.InvalidatePlot(false);
    }

    void Pic_MouseClick(object? sender, MouseEventArgs e)
    {
        if (_cube == null) return;
        var (col, row) = ClickToImageCoords(e.X, e.Y);
        if (col < 0) return;
        AddSpectrumSeries(row, col, _cube.GetSpectrum(row, col));
    }

    // ── File load ─────────────────────────────────────────────────────────

    async Task LoadFileAsync(string path)
    {
        _btnBrowse.Enabled = false;
        _btnRender.Enabled = false;
        _lblInfo.Text = "Loading…";
        try
        {
            HypercubeData cube = await Task.Run(() => LoadCubeFromPath(path));
            LoadCube(cube);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Load error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            _lblInfo.Text = "Load failed";
        }
        finally
        {
            _btnBrowse.Enabled = true;
            _btnRender.Enabled = true;
        }
    }

    static HypercubeData LoadCubeFromPath(string path)
    {
        if (path.EndsWith(".nxi", StringComparison.OrdinalIgnoreCase))
        {
            var result = NxiReader.Read(path);
            double[] wl = result.Metadata.Wavelengths_um.Length > 0
                ? result.Metadata.Wavelengths_um
                : ReconstructWavelengths(result.Header);
            return new HypercubeData
            {
                Cube           = result.Cube,
                Wavelengths_um = wl,
                Rows           = (int)result.Header.Rows,
                Bands          = (int)result.Header.Bands,
                Columns        = (int)result.Header.Columns,
                FileName       = Path.GetFileName(path),
            };
        }
        return EnviImporter.Load(path);
    }

    // ── Render ────────────────────────────────────────────────────────────

    async Task RenderAsync()
    {
        if (_cube == null) return;
        _btnRender.Enabled = false;
        _lblInfo.Text = "Rendering…";
        try
        {
            var mode = (DisplayMode)_cmbMode.SelectedIndex;
            Bitmap bmp = mode switch
            {
                DisplayMode.SingleBand   => RenderSingleBand(),
                DisplayMode.SpectralIndex => await Task.Run(RenderIndex),
                DisplayMode.Pca           => await Task.Run(RenderPca),
                DisplayMode.RxAnomaly     => await Task.Run(RenderRx),
                _                         => RenderRgb(),
            };
            var old = _pic.Image;
            _pic.Image = bmp;
            old?.Dispose();
            UpdateStatusBar(mode);
        }
        catch (Exception ex)
        {
            _lblInfo.Text = $"Render error: {ex.Message}";
        }
        finally
        {
            _btnRender.Enabled = true;
        }
    }

    Bitmap RenderRgb()
    {
        int rIdx = BandIndex(_cmbR, 0.650);
        int gIdx = BandIndex(_cmbG, 0.550);
        int bIdx = BandIndex(_cmbB, 0.450);
        return ToRgbBitmap(_cube!.GetBand(rIdx),
                           _cube.GetBand(gIdx),
                           _cube.GetBand(bIdx),
                           _cube.Rows, _cube.Columns);
    }

    Bitmap RenderSingleBand()
    {
        int idx = _cmbSingle.SelectedIndex < 0 ? 0 : _cmbSingle.SelectedIndex;
        return ToFalseColorBitmap(_cube!.GetBand(idx), _cube.Rows, _cube.Columns);
    }

    Bitmap RenderIndex()
    {
        var type   = (SpectralIndex)_cmbIdx.SelectedIndex;
        var values = SpectralAnalyser.ComputeIndex(_cube!, type);
        return ToFalseColorBitmap(values, _cube!.Rows, _cube.Columns);
    }

    Bitmap RenderPca()
    {
        int comps = (int)_nudPca.Value;
        var pca   = SpectralAnalyser.ComputePCA(_cube!, comps);

        // PC1/2/3 → R/G/B composite
        var pc1 = ExtractPcBand(pca, 0);
        var pc2 = comps > 1 ? ExtractPcBand(pca, 1) : pc1;
        var pc3 = comps > 2 ? ExtractPcBand(pca, 2) : pc1;

        ShowScreePlot(pca);          // updates OxyPlot on UI thread
        return ToRgbBitmap(pc1, pc2, pc3, _cube!.Rows, _cube.Columns);
    }

    float[] ExtractPcBand(PcaResult pca, int k)
    {
        int rows = _cube!.Rows, cols = _cube.Columns, K = pca.Components;
        var band = new float[rows * cols];
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
            band[r * cols + c] = pca.PcBands[r * K + k][c];
        return band;
    }

    Bitmap RenderRx()
    {
        var scores = SpectralAnalyser.ComputeRxAnomaly(_cube!);
        return ToFalseColorBitmap(scores, _cube!.Rows, _cube.Columns);
    }

    // ── OxyPlot helpers ───────────────────────────────────────────────────

    void InitSpectralAxes()
    {
        _plotModel.Axes.Clear();
        _plotModel.Title = "Spectral Profile";
        _plotModel.Axes.Add(new LinearAxis
        {
            Position             = AxisPosition.Bottom,
            Title                = "Wavelength (µm)",
            MajorGridlineStyle   = LineStyle.Dot,
            MajorGridlineColor   = OxyColor.FromRgb(210, 210, 210),
        });
        _plotModel.Axes.Add(new LinearAxis
        {
            Position             = AxisPosition.Left,
            Title                = "Value",
            MajorGridlineStyle   = LineStyle.Dot,
            MajorGridlineColor   = OxyColor.FromRgb(210, 210, 210),
        });
    }

    void AddSpectrumSeries(int row, int col, float[] spectrum)
    {
        // If we are showing a scree plot, reset to spectral mode first
        if (_plotModel.Title != "Spectral Profile")
        {
            _plotModel.Series.Clear();
            _spectrumCount = 0;
            InitSpectralAxes();
        }

        var series = new LineSeries
        {
            Title           = $"({row},{col})",
            Color           = Palette[_spectrumCount % Palette.Length],
            StrokeThickness = 1.5,
        };
        for (int b = 0; b < _cube!.Bands; b++)
            series.Points.Add(new DataPoint(_cube.Wavelengths_um[b], spectrum[b]));

        _plotModel.Series.Add(series);
        _plotModel.InvalidatePlot(true);
        _spectrumCount++;
    }

    void ShowScreePlot(PcaResult pca)
    {
        if (InvokeRequired) { Invoke(() => ShowScreePlot(pca)); return; }

        _plotModel.Series.Clear();
        _plotModel.Axes.Clear();
        _plotModel.Title = "PCA Scree Plot";

        _plotModel.Axes.Add(new LinearAxis
        {
            Position          = AxisPosition.Bottom,
            Title             = "Principal Component",
            Minimum           = 0.5,
            MinimumMajorStep  = 1,
            MajorGridlineStyle = LineStyle.Dot,
        });
        _plotModel.Axes.Add(new LinearAxis
        {
            Position           = AxisPosition.Left,
            Title              = "% Variance Explained",
            Minimum            = 0,
            MajorGridlineStyle = LineStyle.Dot,
        });

        var series = new LineSeries
        {
            Title           = "Explained",
            Color           = OxyColor.FromRgb(0, 114, 189),
            MarkerType      = MarkerType.Circle,
            MarkerSize      = 6,
            MarkerFill      = OxyColor.FromRgb(0, 114, 189),
            StrokeThickness = 2,
        };
        for (int k = 0; k < pca.Components; k++)
            series.Points.Add(new DataPoint(k + 1, pca.Explained[k] * 100.0));

        _plotModel.Series.Add(series);
        _plotView.InvalidatePlot(true);

        // Update info label with eigenvalues
        var info = string.Join("  ", Enumerable.Range(0, pca.Components)
            .Select(k => $"PC{k + 1}: {pca.Explained[k] * 100.0:F1}%"));
        _lblInfo.Text = info;
    }

    // ── Bitmap rendering ──────────────────────────────────────────────────

    static Bitmap ToFalseColorBitmap(float[] values, int rows, int cols)
    {
        float vmin = float.MaxValue, vmax = float.MinValue;
        foreach (float v in values)
            if (float.IsFinite(v)) { if (v < vmin) vmin = v; if (v > vmax) vmax = v; }
        float range = Math.Max(vmax - vmin, 1e-6f);

        var bmp = new Bitmap(cols, rows, PixelFormat.Format32bppArgb);
        var bd  = bmp.LockBits(new Rectangle(0, 0, cols, rows),
                                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        int    stride = Math.Abs(bd.Stride);
        byte[] buf    = new byte[stride * rows];

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float t = float.IsFinite(values[r * cols + c])
                      ? Math.Clamp((values[r * cols + c] - vmin) / range, 0f, 1f)
                      : 0f;
            var (R, G, B) = Viridis(t);
            int off = r * stride + c * 4;
            buf[off]     = B;
            buf[off + 1] = G;
            buf[off + 2] = R;
            buf[off + 3] = 255;
        }

        Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        bmp.UnlockBits(bd);
        return bmp;
    }

    static Bitmap ToRgbBitmap(float[] rBand, float[] gBand, float[] bBand,
                               int rows, int cols)
    {
        float rLo = Perc(rBand, 0.02f), rHi = Perc(rBand, 0.98f);
        float gLo = Perc(gBand, 0.02f), gHi = Perc(gBand, 0.98f);
        float bLo = Perc(bBand, 0.02f), bHi = Perc(bBand, 0.98f);

        var bmp = new Bitmap(cols, rows, PixelFormat.Format32bppArgb);
        var bd  = bmp.LockBits(new Rectangle(0, 0, cols, rows),
                                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        int    stride = Math.Abs(bd.Stride);
        byte[] buf    = new byte[stride * rows];

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            int i   = r * cols + c;
            int off = r * stride + c * 4;
            buf[off]     = Stretch(bBand[i], bLo, bHi);
            buf[off + 1] = Stretch(gBand[i], gLo, gHi);
            buf[off + 2] = Stretch(rBand[i], rLo, rHi);
            buf[off + 3] = 255;
        }

        Marshal.Copy(buf, 0, bd.Scan0, buf.Length);
        bmp.UnlockBits(bd);
        return bmp;
    }

    static byte Stretch(float v, float lo, float hi)
    {
        float range = hi - lo;
        // Uniform-value band: return mid-gray so it shows as visible gray
        // rather than collapsing to black.
        return range < 1e-6f
               ? (byte)128
               : (byte)(Math.Clamp((v - lo) / range, 0f, 1f) * 255f);
    }

    /// <summary>Percentile-based stretch (fraction in [0,1]).</summary>
    static float Perc(float[] arr, float fraction)
    {
        float[] s = Array.FindAll(arr, float.IsFinite);
        Array.Sort(s);
        if (s.Length == 0) return fraction < 0.5f ? 0f : 1f;
        return s[Math.Clamp((int)(s.Length * fraction), 0, s.Length - 1)];
    }

    // Viridis colormap — 8 control points from matplotlib
    static (byte R, byte G, byte B) Viridis(float t)
    {
        ReadOnlySpan<byte> Rs = stackalloc byte[] { 68,  71,  59,  44,  30,  33,  94, 253 };
        ReadOnlySpan<byte> Gs = stackalloc byte[] {  1,  44,  81, 113, 148, 170, 201, 231 };
        ReadOnlySpan<byte> Bs = stackalloc byte[] { 84, 122, 139, 142, 137, 121,  97,  37 };

        float pos = t * 7f;
        int   lo  = Math.Clamp((int)pos, 0, 6);
        int   hi  = lo + 1;
        float f   = pos - lo;
        return ((byte)(Rs[lo] + f * (Rs[hi] - Rs[lo])),
                (byte)(Gs[lo] + f * (Gs[hi] - Gs[lo])),
                (byte)(Bs[lo] + f * (Bs[hi] - Bs[lo])));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    void PopulateBandCombos()
    {
        if (_cube == null) return;
        string[] items = Enumerable.Range(0, _cube.Bands)
            .Select(b => $"Band {b + 1}  ({_cube.Wavelengths_um[b] * 1000.0:F0} nm)")
            .ToArray();

        foreach (var cb in new[] { _cmbR, _cmbG, _cmbB, _cmbSingle })
        {
            cb.Items.Clear();
            cb.Items.AddRange(items);
        }
        // Smart R/G/B defaults: use VIS bands when the cube covers the
        // visible range, otherwise spread evenly across the actual range.
        double wMinUm = _cube.Wavelengths_um.Min();
        double wMaxUm = _cube.Wavelengths_um.Max();
        bool coversVis = wMinUm < 0.60 && wMaxUm > 0.60;
        int rDef, gDef, bDef;
        if (coversVis)
        {
            rDef = _cube.FindBand(0.650);
            gDef = _cube.FindBand(0.550);
            bDef = _cube.FindBand(0.450);
        }
        else
        {
            rDef = Math.Min((int)(_cube.Bands * 0.75), _cube.Bands - 1);
            gDef = Math.Min((int)(_cube.Bands * 0.50), _cube.Bands - 1);
            bDef = Math.Max((int)(_cube.Bands * 0.25), 0);
        }
        // If all three resolve to the same band (very few bands), spread them.
        if (rDef == gDef && gDef == bDef && _cube.Bands > 1)
        {
            rDef = _cube.Bands - 1;
            gDef = _cube.Bands / 2;
            bDef = 0;
        }
        _cmbR.SelectedIndex      = rDef;
        _cmbG.SelectedIndex      = gDef;
        _cmbB.SelectedIndex      = bDef;
        _cmbSingle.SelectedIndex = 0;

        // PCA component cap
        _nudPca.Maximum = _cube.Bands;
        _nudPca.Value   = Math.Min(5, _cube.Bands);
    }

    int BandIndex(ComboBox cb, double fallbackWl_um)
        => cb.SelectedIndex >= 0 ? cb.SelectedIndex : _cube!.FindBand(fallbackWl_um);

    (int col, int row) ClickToImageCoords(int px, int py)
    {
        if (_cube == null || _pic.Image == null) return (-1, -1);
        int   imgW = _cube.Columns, imgH = _cube.Rows;
        int   pbW  = _pic.ClientSize.Width, pbH = _pic.ClientSize.Height;
        float scale = Math.Min((float)pbW / imgW, (float)pbH / imgH);
        float offX  = (pbW - imgW * scale) / 2f;
        float offY  = (pbH - imgH * scale) / 2f;
        int   col   = (int)((px - offX) / scale);
        int   row   = (int)((py - offY) / scale);
        if (col < 0 || col >= imgW || row < 0 || row >= imgH) return (-1, -1);
        return (col, row);
    }

    void UpdateStatusBar(DisplayMode? mode = null)
    {
        if (_cube == null) return;
        double wMin = _cube.Wavelengths_um.Min() * 1000;
        double wMax = _cube.Wavelengths_um.Max() * 1000;
        string modeTxt = mode.HasValue
            ? $"  |  {_cmbMode.Items[(int)mode.Value]}"
            : string.Empty;
        _lblInfo.Text = $"{_cube.Rows}×{_cube.Columns} px  |  {_cube.Bands} bands" +
                        $"  |  {wMin:F0}–{wMax:F0} nm{modeTxt}  |  {_cube.FileName}";
    }

    static double[] ReconstructWavelengths(NxiHeader hdr)
    {
        double band0   = hdr.Band0Wavelength_um_x10000 / 10000.0;
        double spacing = hdr.WavelengthSpacing_um_x10000 > 0
                         ? hdr.WavelengthSpacing_um_x10000 / 10000.0
                         : 0.05;
        return Enumerable.Range(0, (int)hdr.Bands)
            .Select(b => band0 + b * spacing)
            .ToArray();
    }

    static Label MakeLabel(string text)
        => new Label { Text = text, AutoSize = true, Padding = new Padding(0, 3, 2, 0) };

    static ComboBox MakeCombo(int width)
        => new ComboBox { Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
}
