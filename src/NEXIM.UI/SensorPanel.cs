// NEXIM — Sensor panel.
// Layout: NexTlp (2-col, proportional resize).

using NEXIM.Core.Sensor;

namespace NEXIM.UI;

/// <summary>Configuration produced by the Sensor panel.</summary>
public sealed class SensorConfig
{
    public bool   UseGaussianSrf          { get; init; } = false;
    public int    BandCount               { get; init; } = 50;
    public double StartWl_um              { get; init; } = 0.4;
    public double EndWl_um                { get; init; } = 2.5;
    public double FwhmFraction            { get; init; } = 0.7;
    public double PeakQe                  { get; init; } = 0.8;
    public double OpticsTransmittance     { get; init; } = 0.5;
    public double Ifov_mrad               { get; init; } = 1.0;
    public double Altitude_m              { get; init; } = 5000.0;
    public double PixelPitch_um           { get; init; } = 25.0;
    public double IntegrationTime_ms      { get; init; } = 5.0;
    public double FullWellCapacity_e      { get; init; } = 80_000.0;
    public int    AdcBits                 { get; init; } = 14;
    public double ReadNoise_e             { get; init; } = 50.0;
    public double DarkCurrentRate_ePerS   { get; init; } = 500.0;
}

/// <summary>WinForms panel for sensor model parameters.</summary>
public sealed class SensorPanel : Panel
{
    readonly RadioButton   rdoTopHat, rdoGaussian;
    readonly NumericUpDown nudBands, nudStart, nudEnd, nudFwhm, nudQe;
    readonly NumericUpDown nudOpticsTau, nudIfov, nudAlt;
    readonly NumericUpDown nudPitch, nudTint, nudFullWell, nudReadNoise, nudDark;
    readonly ComboBox      cboAdcBits;

    public SensorPanel()
    {
        AutoScroll = true;

        var tbl = new NexTlp(185);
        tbl.SuspendLayout();

        // ── Spectral Response Function ────────────────────────────────────
        tbl.AddHeader("Spectral Response Function");
        rdoTopHat   = new RadioButton { Text = "Uniform top-hat", Checked = true };
        rdoGaussian = new RadioButton { Text = "Gaussian" };
        tbl.AddWide(rdoTopHat);
        tbl.AddWide(rdoGaussian);
        nudBands = tbl.AddNud("Band count",       1,    512,   50,     0);
        nudStart = tbl.AddNud("Start λ (µm)",     0.25m, 14m,  0.4m,  3);
        nudEnd   = tbl.AddNud("End λ (µm)",       0.30m, 14m,  2.5m,  3);
        nudFwhm  = tbl.AddNud("FWHM fraction",    0.1m,  2.0m, 0.7m,  2);
        nudQe    = tbl.AddNud("Peak QE",          0.01m, 1m,   0.8m,  2);
        tbl.AddGap();

        // ── Optics ────────────────────────────────────────────────────────
        tbl.AddHeader("Optics");
        nudOpticsTau = tbl.AddNud("Transmittance",        0.01m,  1m,       0.5m,  2);
        nudIfov      = tbl.AddNud("IFOV (mrad)",           0.01m,  100m,    1.0m,  3);
        nudAlt       = tbl.AddNud("Platform altitude (m)", 100m,   100_000m, 5000m, 0);
        tbl.AddGap();

        // ── Detector Noise ────────────────────────────────────────────────
        tbl.AddHeader("Detector Noise");
        nudPitch     = tbl.AddNud("Pixel pitch (µm)",      1m,    200m,     25m,   1);
        nudTint      = tbl.AddNud("Integration time (ms)", 0.001m, 1000m,    5m,   3);
        nudFullWell  = tbl.AddNud("Full well (ke⁻)",       1m,    500m,     80m,   1);
        nudReadNoise = tbl.AddNud("Read noise (e⁻ rms)",   1m,    1000m,    50m,   1);
        nudDark      = tbl.AddNud("Dark current (e⁻/s)",   0m,    100_000m, 500m,  0);
        cboAdcBits   = tbl.AddCombo("ADC bits",
            ["8", "10", "12", "14", "16"], selectedIndex: 3);
        tbl.AddGap();

        tbl.ResumeLayout(true);
        Controls.Add(tbl);
    }

    public SensorConfig GetConfig() => new()
    {
        UseGaussianSrf        = rdoGaussian.Checked,
        BandCount             = (int)nudBands.Value,
        StartWl_um            = (double)nudStart.Value,
        EndWl_um              = (double)nudEnd.Value,
        FwhmFraction          = (double)nudFwhm.Value,
        PeakQe                = (double)nudQe.Value,
        OpticsTransmittance   = (double)nudOpticsTau.Value,
        Ifov_mrad             = (double)nudIfov.Value,
        Altitude_m            = (double)nudAlt.Value,
        PixelPitch_um         = (double)nudPitch.Value,
        IntegrationTime_ms    = (double)nudTint.Value,
        FullWellCapacity_e    = (double)nudFullWell.Value * 1000.0,  // ke⁻ → e⁻
        AdcBits               = int.Parse(cboAdcBits.Text),
        ReadNoise_e           = (double)nudReadNoise.Value,
        DarkCurrentRate_ePerS = (double)nudDark.Value,
    };

    public SensorModel BuildSensorModel()
    {
        var cfg     = GetConfig();
        double step = (cfg.EndWl_um - cfg.StartWl_um) / cfg.BandCount;
        double fwhm = step * cfg.FwhmFraction;

        SpectralResponseFunction srf = cfg.UseGaussianSrf
            ? SpectralResponseFunction.Gaussian(
                Enumerable.Range(0, cfg.BandCount)
                    .Select(i => cfg.StartWl_um + (i + 0.5) * step).ToArray(),
                fwhm, cfg.PeakQe)
            : SpectralResponseFunction.UniformTopHat(
                cfg.StartWl_um, cfg.EndWl_um, cfg.BandCount, cfg.PeakQe);

        var optics = new OpticsParameters
        {
            OpticsTransmittance = cfg.OpticsTransmittance,
            Ifov_rad            = cfg.Ifov_mrad * 1e-3,
            Altitude_m          = cfg.Altitude_m,
        };
        var noise = new NoiseParameters
        {
            PixelPitch_um         = cfg.PixelPitch_um,
            IntegrationTime_s     = cfg.IntegrationTime_ms * 1e-3,
            FullWellCapacity_e    = cfg.FullWellCapacity_e,
            AdcBits               = cfg.AdcBits,
            ReadNoise_e           = cfg.ReadNoise_e,
            DarkCurrentRate_ePerS = cfg.DarkCurrentRate_ePerS,
        };
        return new SensorModel(srf, optics, noise);
    }
}
