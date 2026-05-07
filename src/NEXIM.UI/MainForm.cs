// NEXIM — Main application window.
// Five-tab layout:
//   Tab 1: Image Input   — RGB image, clustering, spectral library, material map
//   Tab 2: Scene Setup   — geometry, materials, atmosphere mode
//   Tab 3: Atmosphere    — RT mode, profile, spectral range
//   Tab 4: Sensor        — SRF, noise, optics parameters
//   Tab 5: Segmentation  — algorithm selection and SAM endmember library
// Bottom: progress bar + status strip + Run / Export buttons.

using NEXIM.Core.IO;
using NEXIM.Core.Models;
using NEXIM.Core.Segmentation;

namespace NEXIM.UI;

public partial class MainForm : Form
{
    // ── child panels ──────────────────────────────────────────────────────
    readonly SceneImagePanel    _imagePanel;
    readonly SceneSetupPanel    _scenePanel;
    readonly AtmospherePanel    _atmPanel;
    readonly SensorPanel        _sensorPanel;
    readonly SegmentationPanel  _segPanel;
    readonly AnalysisPanel      _analysisPanel;

    // ── simulation state ──────────────────────────────────────────────────
    SimulationRunner?  _runner;
    float[][]?         _lastRadCube;    // BIL float32 cube (rows×bands slices)
    double[]?          _lastWavelengths;
    int                _lastRows, _lastBands, _lastCols;

    public MainForm()
    {
        InitializeComponent();
        Text = "NEXIM — Hyperspectral Scene Simulator";

        // Instantiate panels
        _imagePanel    = new SceneImagePanel   { Dock = DockStyle.Fill };
        _scenePanel    = new SceneSetupPanel   { Dock = DockStyle.Fill };
        _atmPanel      = new AtmospherePanel   { Dock = DockStyle.Fill };
        _sensorPanel   = new SensorPanel       { Dock = DockStyle.Fill };
        _segPanel      = new SegmentationPanel { Dock = DockStyle.Fill };
        _analysisPanel = new AnalysisPanel     { Dock = DockStyle.Fill };

        // Wire up tabs
        tabPageImageInput.Controls.Add(_imagePanel);
        tabPageScene.Controls.Add(_scenePanel);
        tabPageAtm.Controls.Add(_atmPanel);
        tabPageSensor.Controls.Add(_sensorPanel);
        tabPageSeg.Controls.Add(_segPanel);
        tabPageAnalysis.Controls.Add(_analysisPanel);

        // Wire buttons
        btnRun.Click    += BtnRun_Click;
        btnExport.Click += BtnExport_Click;
        btnExport.Enabled = false;
    }

    // ── Run button ────────────────────────────────────────────────────────

    async void BtnRun_Click(object? sender, EventArgs e)
    {
        btnRun.Enabled    = false;
        btnExport.Enabled = false;
        progressBar.Value = 0;
        lblStatus.Text    = "Initialising…";

        try
        {
            var progress = new Progress<(int pct, string msg)>(p =>
            {
                progressBar.Value = Math.Clamp(p.pct, 0, 100);
                lblStatus.Text    = p.msg;
            });

            _runner = new SimulationRunner(
                _scenePanel.GetConfig(),
                _atmPanel.GetConfig(),
                _sensorPanel.GetConfig(),
                _imagePanel.GetMaterialMap());

            var result = await _runner.RunAsync(progress);

            _lastRadCube     = result.RadianceCube;
            _lastWavelengths = result.Wavelengths_um;
            _lastRows        = result.Rows;
            _lastBands       = result.Bands;
            _lastCols        = result.Columns;

            // Push result into Analysis tab automatically
            _analysisPanel.LoadCube(new NEXIM.Core.Models.HypercubeData
            {
                Cube           = result.RadianceCube,
                Wavelengths_um = result.Wavelengths_um,
                Rows           = result.Rows,
                Bands          = result.Bands,
                Columns        = result.Columns,
                FileName       = "(simulation result)",
            });

            string modeNote = _imagePanel.HasMaterialMap
                ? $", image-driven ({_imagePanel.GetMaterialMap()!.Materials.Length} materials)"
                : string.Empty;
            lblStatus.Text    = $"Done — {result.Rows}×{result.Columns} px, " +
                                $"{result.Bands} bands, {result.ElapsedMs:0} ms{modeNote}";
            progressBar.Value = 100;
            btnExport.Enabled = true;

            // Run segmentation if requested
            if (_segPanel.SegmentationEnabled)
                RunSegmentation(result);
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
            MessageBox.Show(ex.Message, "Simulation error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnRun.Enabled = true;
        }
    }

    // ── Export button ─────────────────────────────────────────────────────

    void BtnExport_Click(object? sender, EventArgs e)
    {
        if (_lastRadCube is null || _lastWavelengths is null) return;

        using var dlg = new SaveFileDialog
        {
            Title  = "Export hyperspectral cube",
            Filter = "NEXIM native (*.nxi)|*.nxi|ENVI image (*.img)|*.img|CSV long-form (*.csv)|*.csv",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            string path = dlg.FileName;
            int    fi   = dlg.FilterIndex;  // 1-based

            if (fi == 1)
            {
                var meta = new NxiMetadata
                {
                    SceneName      = "NEXIM export",
                    Wavelengths_um = _lastWavelengths,
                    CreatedUtc     = DateTime.UtcNow.ToString("O"),
                };
                NxiWriter.Write(path, _lastRadCube, _lastRows, _lastBands,
                                _lastCols, _lastWavelengths, meta);
            }
            else if (fi == 2)
            {
                string hdr = Path.ChangeExtension(path, ".hdr");
                string img = path;
                EnviExporter.Export(hdr, _lastRadCube, _lastRows, _lastBands,
                                    _lastCols, _lastWavelengths, imgPath: img);
            }
            else
            {
                CsvExporter.ExportLongForm(path, _lastRadCube, _lastRows,
                                           _lastBands, _lastCols, _lastWavelengths);
            }

            lblStatus.Text = $"Exported → {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Segmentation ──────────────────────────────────────────────────────

    void RunSegmentation(SimulationResult result)
    {
        try
        {
            var pixels = FeatureExtractor.FromBilCube(
                result.RadianceCube, result.Rows, result.Bands, result.Columns);

            var segResult = _segPanel.RunSegmentation(pixels);
            _segPanel.DisplayResult(segResult, result.Rows, result.Columns);
            lblStatus.Text += $" | {segResult.ClassCount} segments";
        }
        catch (Exception ex)
        {
            lblStatus.Text += $" | Segmentation error: {ex.Message}";
        }
    }
}
