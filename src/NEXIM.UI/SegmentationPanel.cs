// NEXIM — Segmentation panel.
// Layout: NexTlp (2-col, proportional resize).

using NEXIM.Core.Segmentation;

namespace NEXIM.UI;

/// <summary>Segmentation algorithm identifier.</summary>
public enum SegAlgorithm { KMeans = 0, GMM = 1, DBSCAN = 2, SAM = 3 }

/// <summary>WinForms panel for segmentation algorithm selection and result visualisation.</summary>
public sealed class SegmentationPanel : Panel
{
    readonly CheckBox      chkEnabled;
    readonly ComboBox      cboAlgorithm;
    readonly NumericUpDown nudClusters, nudEps, nudMinPts, nudSamThresh;
    readonly Label         lblDesc, lblResult;
    readonly PictureBox    picMap;

    // SAM endmember section
    readonly Label   lblEmHeader;
    readonly ListBox lstEndmembers;
    readonly Button  btnAddEndmember, btnClearEndmembers;
    readonly List<Endmember> _endmembers = [];

    public SegmentationPanel()
    {
        AutoScroll = true;

        var tbl = new NexTlp(185);
        tbl.SuspendLayout();

        tbl.AddHeader("Spectral Segmentation");

        chkEnabled = new CheckBox
        {
            Text    = "Run segmentation after simulation",
            Checked = true,
            Anchor  = AnchorStyles.Left | AnchorStyles.Top,
            Margin  = new Padding(4, 4, 4, 6),
        };
        tbl.AddWide(chkEnabled);

        cboAlgorithm = tbl.AddCombo("Algorithm",
            ["K-means", "GMM", "DBSCAN", "SAM"], 0);
        cboAlgorithm.SelectedIndexChanged += (_, _) => UpdateVisibility();

        nudClusters  = tbl.AddNud("Number of clusters",    2, 64,    5,   0);
        nudEps       = tbl.AddNud("\u03b5 (spectral distance)", 0.001m, 10m, 0.2m, 3);
        nudMinPts    = tbl.AddNud("Min points",            1, 100,   5,   0);
        nudSamThresh = tbl.AddNud("Threshold (\u00b0)",         0.1m, 90m, 5.7m, 1);
        tbl.AddGap(6);

        lblEmHeader = new Label
        {
            Text      = "SAM Endmembers",
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 60, 120),
            AutoSize  = true,
            Margin    = new Padding(2, 6, 2, 2),
        };
        tbl.AddWide(lblEmHeader);

        lstEndmembers = new ListBox
        {
            Height  = 80,
            Anchor  = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Margin  = new Padding(4, 2, 8, 2),
        };
        tbl.AddWide(lstEndmembers);

        var btnFlow = new FlowLayoutPanel
        {
            AutoSize      = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin        = new Padding(4, 2, 4, 2),
        };
        btnAddEndmember    = new Button { Text = "Add endmember\u2026", AutoSize = true };
        btnClearEndmembers = new Button { Text = "Clear", AutoSize = true };
        btnAddEndmember.Click    += BtnAdd_Click;
        btnClearEndmembers.Click += (_, _) => { _endmembers.Clear(); lstEndmembers.Items.Clear(); };
        btnFlow.Controls.Add(btnAddEndmember);
        btnFlow.Controls.Add(btnClearEndmembers);
        tbl.AddWide(btnFlow);
        tbl.AddGap(4);

        lblDesc = tbl.AddInfoLabel("K-means clustering in spectral space (ML.NET + pure-C# fallback).");
        tbl.AddSeparator();

        tbl.AddHeader("Segment Map");

        lblResult = new Label
        {
            Text      = "No result yet.",
            ForeColor = SystemColors.GrayText,
            AutoSize  = true,
            Margin    = new Padding(4, 0, 4, 4),
        };
        tbl.AddWide(lblResult);

        picMap = new PictureBox
        {
            SizeMode    = PictureBoxSizeMode.Zoom,
            BackColor   = Color.FromArgb(15, 15, 15),
            BorderStyle = BorderStyle.FixedSingle,
            Height      = 220,
            Anchor      = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Margin      = new Padding(4, 2, 8, 8),
        };
        tbl.AddWide(picMap);

        tbl.ResumeLayout(true);
        Controls.Add(tbl);
        UpdateVisibility();
    }

    public bool SegmentationEnabled => chkEnabled.Checked;

    public SegmentationResult RunSegmentation(IReadOnlyList<PixelFeature> pixels)
    {
        if ((SegAlgorithm)cboAlgorithm.SelectedIndex == SegAlgorithm.SAM)
        {
            var endmembers = _endmembers.Count > 0
                    ? (IReadOnlyList<Endmember>)_endmembers
                    : BuildDefaultEndmembers(pixels);
            var sam = new SamPropagator(endmembers,
                (double)nudSamThresh.Value * Math.PI / 180.0);
            return SamPropagator.ToSegmentationResult(sam.Classify(pixels), endmembers.Count);
        }

        ISegmenter segmenter = (SegAlgorithm)cboAlgorithm.SelectedIndex switch
        {
            SegAlgorithm.KMeans => new KMeansSegmenter((int)nudClusters.Value),
            SegAlgorithm.GMM    => new GmmSegmenter((int)nudClusters.Value),
            SegAlgorithm.DBSCAN => new DbscanSegmenter(
                                       (double)nudEps.Value,
                                       (int)nudMinPts.Value),
            _ => new KMeansSegmenter(5),
        };
        return segmenter.Segment(pixels);
    }

    public void DisplayResult(SegmentationResult result, int rows, int cols)
    {
        var palette = BuildPalette(result.ClassCount);
        var bmp     = new Bitmap(cols, rows);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int idx   = r * cols + c;
                int label = idx < result.Labels.Length ? result.Labels[idx] : -1;
                bmp.SetPixel(c, r, label < 0 ? Color.Black : palette[label % palette.Length]);
            }
        picMap.Image?.Dispose();
        picMap.Image   = bmp;
        lblResult.Text = $"{result.ClassCount} segment(s) \u2014 {result.Labels.Length} pixels";
    }

    void UpdateVisibility()
    {
        var alg = (SegAlgorithm)cboAlgorithm.SelectedIndex;
        nudClusters.Visible        = alg is SegAlgorithm.KMeans or SegAlgorithm.GMM;
        nudEps.Visible             = alg == SegAlgorithm.DBSCAN;
        nudMinPts.Visible          = alg == SegAlgorithm.DBSCAN;
        nudSamThresh.Visible       = alg == SegAlgorithm.SAM;
        lblEmHeader.Visible        = alg == SegAlgorithm.SAM;
        lstEndmembers.Visible      = alg == SegAlgorithm.SAM;
        btnAddEndmember.Visible    = alg == SegAlgorithm.SAM;
        btnClearEndmembers.Visible = alg == SegAlgorithm.SAM;

        lblDesc.Text = alg switch
        {
            SegAlgorithm.KMeans => "Lloyd K-means (ML.NET + pure-C# fallback).",
            SegAlgorithm.GMM    => "Diagonal-covariance EM-GMM, initialised with K-means.",
            SegAlgorithm.DBSCAN => "Density-based clustering in spectral space (\u03b5, minPts).",
            SegAlgorithm.SAM    => "Spectral Angle Mapper; add endmembers or use auto-pick.",
            _ => string.Empty,
        };
    }

    void BtnAdd_Click(object? sender, EventArgs e)
    {
        using var dlg = new EndmemberDialog();
        if (dlg.ShowDialog() == DialogResult.OK && dlg.Endmember is not null)
        {
            _endmembers.Add(dlg.Endmember);
            lstEndmembers.Items.Add(dlg.Endmember.Name);
        }
    }

    static IReadOnlyList<Endmember> BuildDefaultEndmembers(IReadOnlyList<PixelFeature> pixels)
    {
        int k = Math.Clamp(Math.Min(5, pixels.Count), 2, pixels.Count);
        var km = new KMeansSegmenter(k).Segment(pixels);
        if (km.Centroids is null) return [];
        return km.Centroids.Select((c, i) => new Endmember { Name = $"EM-{i}", Spectrum = c }).ToList();
    }

    static Color[] BuildPalette(int n)
    {
        if (n <= 0) n = 1;
        return Enumerable.Range(0, n)
            .Select(i => HsvToColor((float)i / n, 0.8f, 0.9f))
            .ToArray();
    }

    static Color HsvToColor(float h, float s, float v)
    {
        int   hi = (int)(h * 6) % 6;
        float f  = h * 6 - (int)(h * 6);
        float p  = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        var (r, g, b) = hi switch
        {
            0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t),
            3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q),
        };
        return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}

// ── Simple dialog to add a named SAM endmember ─────────────────────────────

internal sealed class EndmemberDialog : Form
{
    readonly TextBox txtName;
    readonly TextBox txtSpectrum;

    public Endmember? Endmember { get; private set; }

    public EndmemberDialog()
    {
        Text            = "Add SAM Endmember";
        ClientSize      = new Size(420, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;

        Controls.Add(new Label { Text = "Name:",                  Location = new Point(10, 14), AutoSize = true });
        Controls.Add(new Label { Text = "Spectrum (CSV values):", Location = new Point(10, 54), AutoSize = true });

        txtName     = new TextBox { Location = new Point(10, 33), Width = 390 };
        txtSpectrum = new TextBox { Location = new Point(10, 73), Width = 390, Height = 55, Multiline = true };

        Controls.Add(txtName);
        Controls.Add(txtSpectrum);

        var btnOk = new Button { Text = "OK",     Location = new Point(10,  145), DialogResult = DialogResult.OK,     AutoSize = true };
        var btnCl = new Button { Text = "Cancel", Location = new Point(100, 145), DialogResult = DialogResult.Cancel, AutoSize = true };
        btnOk.Click += BtnOk_Click;
        Controls.Add(btnOk);
        Controls.Add(btnCl);
        AcceptButton = btnOk;
        CancelButton = btnCl;
    }

    void BtnOk_Click(object? sender, EventArgs e)
    {
        try
        {
            double[] spectrum = txtSpectrum.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(double.Parse)
                .ToArray();

            if (spectrum.Length == 0)
                throw new FormatException("Spectrum must have at least one value.");

            Endmember = new Endmember
            {
                Name     = string.IsNullOrWhiteSpace(txtName.Text) ? "Endmember" : txtName.Text.Trim(),
                Spectrum = spectrum,
            };
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Invalid input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }
}