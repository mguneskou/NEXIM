// NEXIM — Scene Setup panel.
// Layout: TableLayoutPanel (2-col: label 170px | control 100%).
// Resize the window → controls stretch proportionally.

using NEXIM.Core.Models;
using NEXIM.Core.Rendering;

namespace NEXIM.UI;

/// <summary>Configuration produced by the Scene Setup panel.</summary>
public sealed class SceneConfig
{
    public int    Rows              { get; init; } = 32;
    public int    Columns           { get; init; } = 32;
    public double PixelSize_m       { get; init; } = 5.0;
    public double SolarZenith_deg   { get; init; } = 30.0;
    public double SolarAzimuth_deg  { get; init; } = 135.0;
    public double ViewZenith_deg    { get; init; } = 0.0;
    public double ViewAzimuth_deg   { get; init; } = 0.0;
    public BrdfType DefaultBrdf     { get; init; } = BrdfType.Lambertian;
    public double Albedo            { get; init; } = 0.3;
    public double Roughness_rad     { get; init; } = 0.3;
}

/// <summary>
/// WinForms panel for scene geometry and material setup.
/// WinForms panel for scene geometry and material setup.
public sealed class SceneSetupPanel : Panel
{
    readonly NumericUpDown nudRows, nudCols, nudPixelSize;
    readonly NumericUpDown nudSza, nudSaa, nudVza, nudVaa;
    readonly ComboBox      cboBrdf;
    readonly NumericUpDown nudAlbedo, nudRoughness;

    public SceneSetupPanel()
    {
        AutoScroll = true;

        var tbl = new NexTlp(170);
        tbl.SuspendLayout();

        tbl.AddHeader("Scene Geometry");
        nudRows      = tbl.AddNud("Rows",           1,    512,   32,  0);
        nudCols      = tbl.AddNud("Columns",        1,    512,   32,  0);
        nudPixelSize = tbl.AddNud("Pixel size (m)", 0.1m, 1000m,  5m, 2);
        tbl.AddGap();

        tbl.AddHeader("Illumination Geometry");
        nudSza = tbl.AddNud("Solar zenith (°)",  0, 89,  30,  1);
        nudSaa = tbl.AddNud("Solar azimuth (°)", 0, 360, 135, 1);
        nudVza = tbl.AddNud("View zenith (°)",   0, 89,  0,   1);
        nudVaa = tbl.AddNud("View azimuth (°)",  0, 360, 0,   1);
        tbl.AddGap();

        tbl.AddHeader("Default Surface Material");
        cboBrdf      = tbl.AddCombo("BRDF type",
            ["Lambertian", "Oren-Nayar", "GGX", "Hapke"], 0);
        nudAlbedo    = tbl.AddNud("Albedo",    0m,   1m,   0.3m, 3);
        nudRoughness = tbl.AddNud("Roughness", 0m,   1.5m, 0.3m, 3);
        tbl.AddGap();

        var tip = tbl.AddInfoLabel(
            "Tip: load an RGB image on the Image Input tab to assign\n" +
            "per-pixel spectra from the spectral library.");
        tbl.SetColumnSpan(tip, 2);

        tbl.ResumeLayout(true);
        Controls.Add(tbl);
    }

    public SceneConfig GetConfig() => new()
    {
        Rows             = (int)nudRows.Value,
        Columns          = (int)nudCols.Value,
        PixelSize_m      = (double)nudPixelSize.Value,
        SolarZenith_deg  = (double)nudSza.Value,
        SolarAzimuth_deg = (double)nudSaa.Value,
        ViewZenith_deg   = (double)nudVza.Value,
        ViewAzimuth_deg  = (double)nudVaa.Value,
        DefaultBrdf      = (BrdfType)cboBrdf.SelectedIndex,
        Albedo           = (double)nudAlbedo.Value,
        Roughness_rad    = (double)nudRoughness.Value,
    };
}
