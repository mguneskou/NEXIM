namespace NEXIM.UI;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // ── Controls ─────────────────────────────────────────────────────
        tabControl        = new TabControl();
        tabPageImageInput = new TabPage("Image Input");
        tabPageScene      = new TabPage("Scene");
        tabPageAtm        = new TabPage("Atmosphere");
        tabPageSensor     = new TabPage("Sensor");
        tabPageSeg        = new TabPage("Segmentation");
        btnRun           = new Button();
        btnExport        = new Button();
        progressBar      = new ProgressBar();
        statusStrip      = new StatusStrip();
        lblStatus        = new ToolStripStatusLabel();
        toolTip          = new ToolTip(components);

        // ── tabControl ────────────────────────────────────────────────────
        tabControl.Dock = DockStyle.Fill;
        tabControl.TabPages.AddRange(new TabPage[]
            { tabPageImageInput, tabPageScene, tabPageAtm, tabPageSensor, tabPageSeg });

        // ── buttons ───────────────────────────────────────────────────────
        btnRun.Text     = "▶  Run Simulation";
        btnRun.Size     = new Size(160, 32);
        btnRun.Location = new Point(8, 8);
        btnRun.Anchor   = AnchorStyles.Left | AnchorStyles.Bottom;
        toolTip.SetToolTip(btnRun, "Run the full simulation pipeline");

        btnExport.Text     = "⬇  Export…";
        btnExport.Size     = new Size(110, 32);
        btnExport.Location = new Point(176, 8);
        btnExport.Anchor   = AnchorStyles.Left | AnchorStyles.Bottom;
        toolTip.SetToolTip(btnExport, "Export last cube as .nxi / ENVI / CSV");

        // ── progress bar ──────────────────────────────────────────────────
        progressBar.Minimum = 0;
        progressBar.Maximum = 100;
        progressBar.Value   = 0;
        progressBar.Size    = new Size(300, 20);
        progressBar.Location = new Point(294, 14);
        progressBar.Anchor  = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        // ── status strip ──────────────────────────────────────────────────
        lblStatus.Text    = "Ready";
        lblStatus.Spring  = true;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        statusStrip.Items.Add(lblStatus);
        statusStrip.SizingGrip = false;

        // ── bottom panel ──────────────────────────────────────────────────
        var bottomPanel = new Panel
        {
            Height = 52,
            Dock   = DockStyle.Bottom,
        };
        bottomPanel.Controls.Add(btnRun);
        bottomPanel.Controls.Add(btnExport);
        bottomPanel.Controls.Add(progressBar);

        // ── form ──────────────────────────────────────────────────────────
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        ClientSize          = new Size(960, 660);
        MinimumSize         = new Size(880, 580);
        Controls.Add(tabControl);
        Controls.Add(bottomPanel);
        Controls.Add(statusStrip);
        Text = "NEXIM";
    }

    #endregion

    TabControl           tabControl;
    TabPage              tabPageImageInput, tabPageScene, tabPageAtm, tabPageSensor, tabPageSeg;
    Button               btnRun, btnExport;
    ProgressBar          progressBar;
    StatusStrip          statusStrip;
    ToolStripStatusLabel lblStatus;
    ToolTip              toolTip;
}
