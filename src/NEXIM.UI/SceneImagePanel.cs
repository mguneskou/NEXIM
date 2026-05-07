// NEXIM — Image Input panel.
// Layout:
//   TOP ROW  : "Source Image" GroupBox (left 50 %) | "Cluster Map Preview" GroupBox (right 50 %)
//   ROW 2    : "Unsupervised Classification" settings (NexTlp)
//   ROW 3    : "Spectral Library" source selector
//   ROW 4    : "Material Assignments" data grid
//
// Spectral library sources:
//   1. Embedded       — 23 curated spectra (always available)
//   2. USGS curated   — 26 hand-picked USGS splib07b spectra
//   3. USGS full      — all spectra in a chapter (hundreds; server crawl)
//   4. Custom CSV     — user-supplied file

using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NEXIM.Core.Rendering;
using NEXIM.Core.SpectralLibrary;

namespace NEXIM.UI;

/// <summary>
/// Panel for the "Image Input" tab.  Loads an RGB image, classifies it
/// into material clusters, and exposes the resulting <see cref="MaterialMap"/>.
/// </summary>
public sealed class SceneImagePanel : Panel
{
    // ── Image state ───────────────────────────────────────────────────────
    private int[]? _imageArgb;
    private int    _imageWidth, _imageHeight;

    // ── Library / material state ──────────────────────────────────────────
    private IReadOnlyList<SpectralSignature>? _library;
    private MaterialMap?                       _materialMap;

    // ── Controls: Source Image ────────────────────────────────────────────
    private TextBox    _txtImagePath   = null!;
    private Button     _btnBrowseImage = null!;
    private PictureBox _picPreview     = null!;

    // ── Controls: Cluster Map ─────────────────────────────────────────────
    private Label      _lblClusterStatus = null!;
    private PictureBox _picClusterMap    = null!;

    // ── Controls: Classification ──────────────────────────────────────────
    private ComboBox      _cboMethod     = null!;
    private NumericUpDown _nudClusters   = null!;
    private NumericUpDown _nudEps        = null!;
    private NumericUpDown _nudMinPts     = null!;
    private Button        _btnClassify   = null!;
    private Label         _lblClassifyStatus = null!;

    // ── Controls: Library ─────────────────────────────────────────────────
    private RadioButton _rdoEmbedded  = null!;
    private RadioButton _rdoUsgs      = null!;
    private RadioButton _rdoUsgsFull  = null!;
    private RadioButton _rdoCsv       = null!;
    private Button      _btnDownloadCurated  = null!;
    private Button      _btnDownloadFull     = null!;
    private Label       _lblDownloadStatus   = null!;
    private TextBox     _txtCsvPath          = null!;
    private Button      _btnBrowseCsv        = null!;

    // ── Controls: Assignments ─────────────────────────────────────────────
    private DataGridView _dgv       = null!;
    private Button       _btnRefresh = null!;

    // ── USGS cache directories ────────────────────────────────────────────
    private static readonly string UsgsCacheDir =
        Path.Combine("data", "spectral_library", "usgs_splib07b");

    private static readonly Font _normalFont = new Font("Segoe UI", 9f, FontStyle.Regular);

    // ─────────────────────────────────────────────────────────────────────
    public SceneImagePanel()
    {
        AutoScroll = true;
        Font       = _normalFont;       // panel-wide override; groups inherit

        var outer = new TableLayoutPanel
        {
            Dock         = DockStyle.Top,
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 1,
            Padding      = new Padding(6, 6, 6, 10),
            GrowStyle    = TableLayoutPanelGrowStyle.AddRows,
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── ROW 1: side-by-side image panels ─────────────────────────────
        var topRow = new TableLayoutPanel
        {
            Dock         = DockStyle.Fill,
            ColumnCount  = 2,
            RowCount     = 1,
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin       = new Padding(0, 0, 0, 4),
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        topRow.Controls.Add(BuildSourceImageBox(), 0, 0);
        topRow.Controls.Add(BuildClusterMapBox(),  1, 0);
        outer.Controls.Add(topRow);

        // ── ROW 2: classification settings ───────────────────────────────
        outer.Controls.Add(MakeSection("Unsupervised Classification",
            s => s.Controls.Add(BuildClassificationTlp())));

        // ── ROW 3: spectral library selector ─────────────────────────────
        outer.Controls.Add(MakeSection("Spectral Library",
            s => s.Controls.Add(BuildLibraryTlp())));

        // ── ROW 4: material assignments grid ─────────────────────────────
        outer.Controls.Add(MakeSection("Material Assignments",
            s => s.Controls.Add(BuildAssignmentsPanel())));

        Controls.Add(outer);
        UpdateClusterControls();
        UpdateLibraryControls();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public MaterialMap? GetMaterialMap() => _materialMap;
    public bool         HasMaterialMap   => _materialMap is not null;

    // ── Section builders ──────────────────────────────────────────────────

    private GroupBox BuildSourceImageBox()
    {
        var grp = MakeSection("Source Image", s =>
        {
            // path row
            var pathRow = new TableLayoutPanel
            {
                Dock         = DockStyle.Fill,
                ColumnCount  = 2,
                AutoSize     = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin       = new Padding(4, 4, 4, 2),
            };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

            _txtImagePath   = new TextBox { ReadOnly = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Font   = _normalFont };
            _btnBrowseImage = new Button  { Text = "Browse…", AutoSize = true,
                Anchor = AnchorStyles.Right | AnchorStyles.Top };
            _btnBrowseImage.Click += BtnBrowseImage_Click;
            pathRow.Controls.Add(_txtImagePath,   0, 0);
            pathRow.Controls.Add(_btnBrowseImage, 1, 0);

            // preview image
            _picPreview = new PictureBox
            {
                Height      = 180,
                SizeMode    = PictureBoxSizeMode.Zoom,
                BackColor   = Color.FromArgb(20, 20, 20),
                BorderStyle = BorderStyle.FixedSingle,
                Dock        = DockStyle.Fill,
                Margin      = new Padding(4, 4, 4, 4),
            };

            var vstack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            vstack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            vstack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            vstack.RowStyles.Add(new RowStyle(SizeType.Absolute, 188));
            vstack.Controls.Add(pathRow,     0, 0);
            vstack.Controls.Add(_picPreview, 0, 1);
            s.Controls.Add(vstack);
        });
        return grp;
    }

    private GroupBox BuildClusterMapBox()
    {
        var grp = MakeSection("Cluster Map Preview", s =>
        {
            _lblClusterStatus = new Label
            {
                Text      = "Classify an image to see clusters.",
                ForeColor = SystemColors.GrayText,
                AutoSize  = true,
                Margin    = new Padding(6, 4, 6, 2),
                Font      = _normalFont,
            };

            _picClusterMap = new PictureBox
            {
                Height      = 180,
                SizeMode    = PictureBoxSizeMode.Zoom,
                BackColor   = Color.FromArgb(20, 20, 20),
                BorderStyle = BorderStyle.FixedSingle,
                Dock        = DockStyle.Fill,
                Margin      = new Padding(4, 4, 4, 4),
            };

            var vstack = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            vstack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            vstack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            vstack.RowStyles.Add(new RowStyle(SizeType.Absolute, 188));
            vstack.Controls.Add(_lblClusterStatus, 0, 0);
            vstack.Controls.Add(_picClusterMap,    0, 1);
            s.Controls.Add(vstack);
        });
        return grp;
    }

    private NexTlp BuildClassificationTlp()
    {
        var tbl = new NexTlp(155);
        tbl.SuspendLayout();

        _cboMethod = tbl.AddCombo("Method",
            ["K-means", "GMM (Gaussian Mixture)", "DBSCAN"], 0);
        _cboMethod.SelectedIndexChanged += (_, _) => UpdateClusterControls();

        _nudClusters = tbl.AddNud("Clusters",           2, 64, 6, 0);
        _nudEps      = tbl.AddNud("\u03b5 (distance)", 0.01m, 1m, 0.10m, 3);
        _nudMinPts   = tbl.AddNud("Min points",         1, 200, 10, 0);
        tbl.AddGap(4);

        var btnRow = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(4, 2, 4, 2),
        };
        _btnClassify = new Button { Text = "\u25b6  Classify Image", AutoSize = true };
        _btnClassify.Click += BtnClassify_Click;
        btnRow.Controls.Add(_btnClassify);
        tbl.AddWide(btnRow);

        _lblClassifyStatus = tbl.AddInfoLabel("Load an image, then click Classify.");

        tbl.ResumeLayout(true);
        return tbl;
    }

    private NexTlp BuildLibraryTlp()
    {
        var tbl = new NexTlp(155);
        tbl.SuspendLayout();

        _rdoEmbedded = new RadioButton
        {
            Text = "Embedded  (23 curated spectra — always available)",
            Checked = true, AutoSize = true, Margin = new Padding(4, 3, 4, 2),
        };
        _rdoUsgs = new RadioButton
        {
            Text = "USGS splib07b curated  (download ~26 hand-picked spectra)",
            AutoSize = true, Margin = new Padding(4, 2, 4, 2),
        };
        _rdoUsgsFull = new RadioButton
        {
            Text = "USGS splib07b full  (crawl server — hundreds of spectra per chapter)",
            AutoSize = true, Margin = new Padding(4, 2, 4, 2),
        };
        _rdoCsv = new RadioButton
        {
            Text = "Custom CSV file", AutoSize = true, Margin = new Padding(4, 2, 4, 6),
        };
        tbl.AddWide(_rdoEmbedded);
        tbl.AddWide(_rdoUsgs);
        tbl.AddWide(_rdoUsgsFull);
        tbl.AddWide(_rdoCsv);

        // curated download row
        var btnRowC = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(4, 2, 4, 2),
        };
        _btnDownloadCurated = new Button { Text = "\u2b07  Download Curated (~26)", AutoSize = true };
        _btnDownloadCurated.Click += BtnDownloadCurated_Click;
        btnRowC.Controls.Add(_btnDownloadCurated);
        tbl.AddWide(btnRowC);

        // full download row
        var btnRowF = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(4, 2, 4, 2),
        };
        _btnDownloadFull = new Button
        {
            Text = "\u2b07  Download Full USGS Library\u2026", AutoSize = true,
        };
        _btnDownloadFull.Click += BtnDownloadFull_Click;
        btnRowF.Controls.Add(_btnDownloadFull);
        tbl.AddWide(btnRowF);

        _lblDownloadStatus = tbl.AddInfoLabel(GetUsgsStatusText());
        tbl.AddGap(2);

        // CSV row
        var csvRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4, 2, 4, 2),
        };
        csvRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        csvRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        _txtCsvPath   = new TextBox { ReadOnly = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Font   = _normalFont };
        _btnBrowseCsv = new Button { Text = "Browse…", AutoSize = true };
        _btnBrowseCsv.Click += BtnBrowseCsv_Click;
        csvRow.Controls.Add(_txtCsvPath,   0, 0);
        csvRow.Controls.Add(_btnBrowseCsv, 1, 0);
        tbl.AddWide(csvRow);

        tbl.ResumeLayout(true);

        _rdoEmbedded .CheckedChanged += (_, _) => UpdateLibraryControls();
        _rdoUsgs     .CheckedChanged += (_, _) => UpdateLibraryControls();
        _rdoUsgsFull .CheckedChanged += (_, _) => UpdateLibraryControls();
        _rdoCsv      .CheckedChanged += (_, _) => UpdateLibraryControls();
        return tbl;
    }

    private Panel BuildAssignmentsPanel()
    {
        _dgv = new DataGridView
        {
            Dock                   = DockStyle.Fill,
            Height                 = 190,
            ReadOnly               = true,
            AllowUserToAddRows     = false,
            AllowUserToDeleteRows  = false,
            RowHeadersVisible      = false,
            SelectionMode          = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode    = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor        = SystemColors.Window,
            BorderStyle            = BorderStyle.None,
            CellBorderStyle        = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor              = Color.FromArgb(220, 220, 220),
            Font                   = _normalFont,
        };
        _dgv.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "#",               FillWeight = 6,  MinimumWidth = 36 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Centroid",        FillWeight = 14, MinimumWidth = 80 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Matched Spectrum",FillWeight = 48, MinimumWidth = 140 });
        _dgv.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Category",        FillWeight = 20, MinimumWidth = 80 });
        _dgv.CellPainting += Dgv_CellPainting;

        _btnRefresh = new Button
        {
            Text = "\u21ba  Refresh Assignments", AutoSize = true,
            Margin = new Padding(4, 4, 4, 4),
        };
        _btnRefresh.Click += (_, _) => RefreshAssignments();

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(_dgv,        0, 0);
        panel.Controls.Add(_btnRefresh, 0, 1);
        return panel;
    }

    // ── Event handlers ─────────────────────────────────────────────────────

    void BtnBrowseImage_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Open RGB Image",
            Filter = "Image files|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.gif|All files|*.*",
        };
        if (dlg.ShowDialog() == DialogResult.OK) LoadImageFile(dlg.FileName);
    }

    void BtnClassify_Click(object? sender, EventArgs e)
    {
        if (_imageArgb is null)
        {
            _lblClassifyStatus.Text = "No image loaded — use Browse first.";
            return;
        }

        _btnClassify.Enabled          = false;
        _lblClassifyStatus.Text       = "Classifying\u2026";
        _lblClassifyStatus.ForeColor  = Color.DarkOrange;
        Application.DoEvents();

        try
        {
            BuildLibrary();
            if (_library is null || _library.Count == 0)
            {
                _lblClassifyStatus.Text = "Library is empty — download or select a library first.";
                return;
            }

            string method = _cboMethod.SelectedIndex switch
            {
                1 => "gmm",
                2 => "dbscan",
                _ => "kmeans",
            };

            _materialMap = MaterialMap.FromArgbPixels(
                _imageArgb, _imageWidth, _imageHeight,
                _library,
                clusterCount:  (int)_nudClusters.Value,
                method:        method,
                dbscanEpsilon: (double)_nudEps.Value,
                dbscanMinPts:  (int)_nudMinPts.Value);

            int k = _materialMap.Materials.Length;
            _lblClassifyStatus.Text      = $"{k} cluster(s) found and matched.";
            _lblClassifyStatus.ForeColor = Color.DarkGreen;
            _lblClusterStatus.Text       = $"{k} cluster(s) — centroid colours shown.";
            _lblClusterStatus.ForeColor  = SystemColors.ControlText;

            RenderClusterMap();
            UpdateAssignmentsGrid();
        }
        catch (Exception ex)
        {
            _lblClassifyStatus.Text      = $"Error: {ex.Message}";
            _lblClassifyStatus.ForeColor = Color.DarkRed;
        }
        finally
        {
            _btnClassify.Enabled = true;
        }
    }

    async void BtnDownloadCurated_Click(object? sender, EventArgs e)
    {
        _btnDownloadCurated.Enabled  = false;
        _btnDownloadFull.Enabled     = false;
        _lblDownloadStatus.Text      = "Downloading curated list\u2026";
        _lblDownloadStatus.ForeColor = Color.DarkOrange;

        try
        {
            var progress = new Progress<(int done, int total, string name)>(p =>
            {
                _lblDownloadStatus.Text = $"Curated: {p.done}/{p.total} \u2014 {p.name}";
                Application.DoEvents();
            });
            var sigs = await SpectralLibraryDownloader.DownloadAllAsync(UsgsCacheDir, progress);
            _lblDownloadStatus.Text      = sigs.Count > 0
                ? $"{sigs.Count} spectra cached in {UsgsCacheDir}"
                : "Download failed \u2014 check your internet connection.";
            _lblDownloadStatus.ForeColor = sigs.Count > 0 ? Color.DarkGreen : Color.DarkRed;
        }
        catch (Exception ex)
        {
            _lblDownloadStatus.Text      = $"Error: {ex.Message}";
            _lblDownloadStatus.ForeColor = Color.DarkRed;
        }
        finally
        {
            _btnDownloadCurated.Enabled = true;
            _btnDownloadFull.Enabled    = true;
        }
    }

    async void BtnDownloadFull_Click(object? sender, EventArgs e)
    {
        // Let user choose chapters
        using var dlg = new ChapterSelectDialog();
        if (dlg.ShowDialog() != DialogResult.OK) return;
        var chapters = dlg.SelectedChapters;
        if (chapters.Count == 0) return;

        _btnDownloadCurated.Enabled  = false;
        _btnDownloadFull.Enabled     = false;
        _lblDownloadStatus.Text      = "Discovering server files\u2026";
        _lblDownloadStatus.ForeColor = Color.DarkOrange;

        try
        {
            var progress = new Progress<(int done, int total, string name)>(p =>
            {
                _lblDownloadStatus.Text = p.total > 0
                    ? $"Chapter {p.name}: {p.done}/{p.total} files"
                    : $"Crawling {p.name}\u2026";
                Application.DoEvents();
            });

            int total = 0;
            foreach (var (ch, cat) in chapters)
            {
                var sigs = await SpectralLibraryDownloader.DiscoverAndDownloadAsync(
                    ch, cat, UsgsCacheDir, progress);
                total += sigs.Count;
            }

            int cached = Directory.Exists(UsgsCacheDir)
                ? Directory.GetFiles(UsgsCacheDir, "*.txt").Length : 0;
            _lblDownloadStatus.Text      = $"{cached} spectra total in cache (+{total} new).";
            _lblDownloadStatus.ForeColor = Color.DarkGreen;
        }
        catch (Exception ex)
        {
            _lblDownloadStatus.Text      = $"Error: {ex.Message}";
            _lblDownloadStatus.ForeColor = Color.DarkRed;
        }
        finally
        {
            _btnDownloadCurated.Enabled = true;
            _btnDownloadFull.Enabled    = true;
        }
    }

    void BtnBrowseCsv_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Open Spectral Library CSV",
            Filter = "CSV files|*.csv|All files|*.*",
        };
        if (dlg.ShowDialog() == DialogResult.OK) _txtCsvPath.Text = dlg.FileName;
    }

    void RefreshAssignments()
    {
        if (_materialMap is not null) UpdateAssignmentsGrid();
    }

    // ── Private helpers ────────────────────────────────────────────────────

    void LoadImageFile(string path)
    {
        try
        {
            using var bmp = new Bitmap(path);
            _imageWidth  = bmp.Width;
            _imageHeight = bmp.Height;

            var rect    = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            _imageArgb  = new int[bmp.Width * bmp.Height];
            Marshal.Copy(bmpData.Scan0, _imageArgb, 0, _imageArgb.Length);
            bmp.UnlockBits(bmpData);

            _picPreview.Image?.Dispose();
            _picPreview.Image = new Bitmap(bmp);     // copy for display
            _picPreview.Refresh();

            _txtImagePath.Text           = path;
            _materialMap                 = null;
            _lblClassifyStatus.Text      = $"Loaded {bmp.Width}\u00d7{bmp.Height} px. Press Classify.";
            _lblClassifyStatus.ForeColor = SystemColors.ControlText;
            _lblClusterStatus.Text       = "Classify an image to see clusters.";
            _lblClusterStatus.ForeColor  = SystemColors.GrayText;
            _picClusterMap.Image?.Dispose();
            _picClusterMap.Image = null;
            _picClusterMap.Refresh();
            _dgv.Rows.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot load image:\n{ex.Message}", "Image error",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void BuildLibrary()
    {
        if (_rdoUsgs.Checked || _rdoUsgsFull.Checked)
        {
            var cached = SpectralLibraryDownloader.LoadFromCache(UsgsCacheDir);
            _library = cached.Count > 0
                ? (IReadOnlyList<SpectralSignature>)cached
                : EmbeddedSpectralLibrary.GetAll();
        }
        else if (_rdoCsv.Checked && !string.IsNullOrWhiteSpace(_txtCsvPath.Text))
        {
            try   { _library = SpectralLibraryLoader.LoadFromCsv(_txtCsvPath.Text); }
            catch { _library = EmbeddedSpectralLibrary.GetAll(); }
        }
        else
        {
            _library = EmbeddedSpectralLibrary.GetAll();
        }
    }

    void RenderClusterMap()
    {
        if (_materialMap is null) return;

        // Build cluster-colour bitmap: each pixel = centroid colour of its label
        var bmp  = new Bitmap(_imageWidth, _imageHeight, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, _imageWidth, _imageHeight),
                                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[_imageWidth * _imageHeight];
            int k      = _materialMap.ClusterArgb.Length;
            for (int i = 0; i < pixels.Length; i++)
            {
                int lbl  = _materialMap.GetLabel(i / _imageWidth, i % _imageWidth);
                pixels[i] = _materialMap.ClusterArgb[lbl % k];
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        _picClusterMap.Image?.Dispose();
        _picClusterMap.Image = bmp;
        _picClusterMap.Invalidate();
        _picClusterMap.Update();
    }

    void UpdateAssignmentsGrid()
    {
        if (_materialMap is null) return;
        _dgv.Rows.Clear();
        int k = _materialMap.Materials.Length;
        for (int i = 0; i < k; i++)
        {
            var mat  = _materialMap.Materials[i];
            int argb = _materialMap.ClusterArgb[i];
            int rr   = (argb >> 16) & 0xFF;
            int gg   = (argb >>  8) & 0xFF;
            int bb   =  argb        & 0xFF;
            _dgv.Rows.Add(i + 1, $"#{rr:X2}{gg:X2}{bb:X2}", mat.Name, mat.Category);
            _dgv.Rows[i].Tag = argb;
        }
    }

    void Dgv_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.ColumnIndex != 1 || e.RowIndex < 0) return;
        if (_dgv.Rows[e.RowIndex].Tag is not int argb) return;

        e.PaintBackground(e.ClipBounds, true);

        int rr = (argb >> 16) & 0xFF;
        int gg = (argb >>  8) & 0xFF;
        int bb =  argb        & 0xFF;

        using var brush    = new SolidBrush(Color.FromArgb(rr, gg, bb));
        var swatchRect     = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + 4,
                                           18, e.CellBounds.Height - 9);
        e.Graphics!.FillRectangle(brush, swatchRect);
        e.Graphics.DrawRectangle(Pens.Gray, swatchRect);

        var textRect = new Rectangle(swatchRect.Right + 4, e.CellBounds.Y,
            e.CellBounds.Width - swatchRect.Width - 12, e.CellBounds.Height);
        TextRenderer.DrawText(e.Graphics, e.FormattedValue?.ToString(), _dgv.Font,
            textRect, _dgv.DefaultCellStyle.ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        e.Handled = true;
    }

    void UpdateClusterControls()
    {
        bool dbscan      = _cboMethod?.SelectedIndex == 2;
        _nudClusters.Visible = !dbscan;
        _nudEps.Visible      =  dbscan;
        _nudMinPts.Visible   =  dbscan;
    }

    void UpdateLibraryControls()
    {
        bool usgs     = _rdoUsgs?.Checked    ?? false;
        bool usgsFull = _rdoUsgsFull?.Checked ?? false;
        bool csv      = _rdoCsv?.Checked     ?? false;

        _btnDownloadCurated .Visible = usgs || usgsFull;
        _btnDownloadFull    .Visible = usgs || usgsFull;
        _lblDownloadStatus  .Visible = usgs || usgsFull;
        _txtCsvPath         .Visible = csv;
        _btnBrowseCsv       .Visible = csv;
    }

    static string GetUsgsStatusText()
    {
        if (!Directory.Exists(UsgsCacheDir)) return "Not downloaded yet.";
        int n = Directory.GetFiles(UsgsCacheDir, "*.txt").Length;
        return n > 0 ? $"{n} spectra cached in {UsgsCacheDir}" : "Cache directory empty.";
    }

    // ── Section GroupBox factory ───────────────────────────────────────────

    private static GroupBox MakeSection(string title, Action<GroupBox> populate)
    {
        var grp = new GroupBox
        {
            Text         = title,
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock         = DockStyle.Fill,
            Margin       = new Padding(4, 4, 4, 2),
            Font         = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor    = Color.FromArgb(0, 60, 120),
            Padding      = new Padding(4, 18, 4, 4),
        };
        populate(grp);
        return grp;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Dialog that lets the user select which USGS splib07b chapters to crawl.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class ChapterSelectDialog : Form
{
    private readonly CheckedListBox _clb;

    // (chapter-id, suggested-category-name)
    private static readonly (string ch, string cat)[] AllChapters =
    [
        ("chapter1", "Mineral"),
        ("chapter2", "Soil"),
        ("chapter3", "Coating"),
        ("chapter4", "Liquid"),
        ("chapter5", "Organic"),
        ("chapter6", "Urban"),
        ("chapter7", "Vegetation"),
        ("chapter8", "Mixture"),
    ];

    public IReadOnlyList<(string ch, string cat)> SelectedChapters =>
        _clb.CheckedIndices.Cast<int>().Select(i => AllChapters[i]).ToList();

    public ChapterSelectDialog()
    {
        Text            = "Select USGS Chapters to Download";
        ClientSize      = new Size(380, 310);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;
        Font            = new Font("Segoe UI", 9f);

        var info = new Label
        {
            Text     = "Each chapter may contain dozens to hundreds of spectra.\n" +
                       "Downloads are cached; already-cached files are skipped.",
            AutoSize = false,
            Width    = 360, Height = 44,
            Location = new Point(10, 10),
        };

        _clb = new CheckedListBox
        {
            Location      = new Point(10, 62),
            Size          = new Size(360, 180),
            CheckOnClick  = true,
        };
        foreach (var (ch, cat) in AllChapters)
            _clb.Items.Add($"{cat}  ({ch})", false);

        var btnAll = new Button { Text = "Select All",  Location = new Point(10,  254), AutoSize = true };
        var btnOk  = new Button { Text = "Download",    Location = new Point(200, 254), DialogResult = DialogResult.OK,     AutoSize = true };
        var btnCl  = new Button { Text = "Cancel",      Location = new Point(290, 254), DialogResult = DialogResult.Cancel, AutoSize = true };
        btnAll.Click += (_, _) =>
        {
            for (int i = 0; i < _clb.Items.Count; i++) _clb.SetItemChecked(i, true);
        };
        Controls.AddRange([info, _clb, btnAll, btnOk, btnCl]);
        AcceptButton = btnOk;
        CancelButton = btnCl;
    }
}