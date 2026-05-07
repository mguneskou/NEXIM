// NEXIM — Atmosphere panel.
// Layout: NexTlp (2-col, proportional resize).

using NEXIM.Core.Atmospheric;
using NEXIM.Core.Models;

namespace NEXIM.UI;

/// <summary>Atmospheric RT mode identifier.</summary>
public enum AtmosphericMode { Fast = 0, Accurate = 1, FullPhysics = 2 }

/// <summary>Configuration produced by the Atmosphere panel.</summary>
public sealed class AtmosphereConfig
{
    public AtmosphericMode    Mode        { get; init; } = AtmosphericMode.Accurate;
    public double             StartWl_um  { get; init; } = 0.4;
    public double             EndWl_um    { get; init; } = 2.5;
    public double             StepWl_um   { get; init; } = 0.01;
    public StandardAtmosphere Atmosphere  { get; init; } = StandardAtmosphere.USStandard;
    public double             Altitude_km { get; init; } = 0.0;
    public double             Aot550      { get; init; } = 0.2;
}

/// <summary>WinForms panel for atmospheric model selection.</summary>
public sealed class AtmospherePanel : Panel
{
    readonly RadioButton   rdoFast, rdoAccurate, rdoFull;
    readonly Label         lblModeDesc;
    readonly NumericUpDown nudStart, nudEnd, nudStep;
    readonly ComboBox      cboAtm;
    readonly NumericUpDown nudAlt, nudAot;

    public AtmospherePanel()
    {
        AutoScroll = true;

        var tbl = new NexTlp(170);
        tbl.SuspendLayout();

        // ── RT Mode ───────────────────────────────────────────────────────
        tbl.AddHeader("Atmospheric RT Mode");

        rdoFast     = new RadioButton { Text = "Mode 1 — Fast LUT  (< 10 ms,  ±5–10%)" };
        rdoAccurate = new RadioButton { Text = "Mode 2 — Accurate CKD  (~ 300 ms, ±1%)", Checked = true };
        rdoFull     = new RadioButton { Text = "Mode 3 — Full Physics  (libRadtran, ~ 10 s, ±0.1%)" };

        tbl.AddWide(rdoFast);
        tbl.AddWide(rdoAccurate);
        tbl.AddWide(rdoFull);

        lblModeDesc = new Label
        {
            ForeColor = SystemColors.GrayText,
            AutoSize  = true,
            Margin    = new Padding(4, 0, 4, 4),
        };
        tbl.AddWide(lblModeDesc);
        tbl.AddGap();

        // ── Wavelength Grid ───────────────────────────────────────────────
        tbl.AddHeader("Spectral Range");
        nudStart = tbl.AddNud("Start λ (µm)", 0.25m, 14m,    0.4m,  3);
        nudEnd   = tbl.AddNud("End λ (µm)",   0.30m, 14m,    2.5m,  3);
        nudStep  = tbl.AddNud("Step λ (µm)",  0.001m, 1m,   0.01m,  4);
        tbl.AddGap();

        // ── Profile ───────────────────────────────────────────────────────
        tbl.AddHeader("Atmospheric Profile");
        cboAtm = tbl.AddCombo("Profile",
            ["US Standard 1976", "Tropical", "Mid-Lat Summer", "Mid-Lat Winter"], 0);
        nudAlt = tbl.AddNud("Surface altitude (km)", 0m,   5m,   0m,   2);
        nudAot = tbl.AddNud("AOT @ 550 nm",          0m,   2m,   0.2m, 3);

        tbl.ResumeLayout(true);
        Controls.Add(tbl);

        rdoFast.CheckedChanged     += (_, _) => UpdateModeDesc();
        rdoAccurate.CheckedChanged += (_, _) => UpdateModeDesc();
        rdoFull.CheckedChanged     += (_, _) => UpdateModeDesc();
        UpdateModeDesc();
    }

    public AtmosphereConfig GetConfig()
    {
        AtmosphericMode mode = rdoFast.Checked    ? AtmosphericMode.Fast
                             : rdoAccurate.Checked ? AtmosphericMode.Accurate
                             : AtmosphericMode.FullPhysics;

        StandardAtmosphere profile = cboAtm.SelectedIndex switch
        {
            1 => StandardAtmosphere.Tropical,
            2 => StandardAtmosphere.MidlatitudeSummer,
            3 => StandardAtmosphere.MidlatitudeWinter,
            _ => StandardAtmosphere.USStandard,
        };

        return new AtmosphereConfig
        {
            Mode        = mode,
            StartWl_um  = (double)nudStart.Value,
            EndWl_um    = (double)nudEnd.Value,
            StepWl_um   = (double)nudStep.Value,
            Atmosphere  = profile,
            Altitude_km = (double)nudAlt.Value,
            Aot550      = (double)nudAot.Value,
        };
    }

    void UpdateModeDesc()
    {
        lblModeDesc.Text = rdoFast.Checked
            ? "Pre-computed LUT: fast but requires running LutGen first."
            : rdoAccurate.Checked
                ? "Native C# CKD + DISORT 8-stream: MODTRAN-class accuracy, no external tools."
                : "libRadtran subprocess (WSL2 required): highest accuracy, ~10–30 s per run.";
    }
}
