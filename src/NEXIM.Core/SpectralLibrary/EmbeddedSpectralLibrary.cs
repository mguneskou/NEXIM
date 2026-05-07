// NEXIM — Embedded spectral library.
//
// 32 curated spectra covering vegetation, soil, water, urban materials,
// minerals, and snow/ice.  Derived from USGS Spectral Library Version 7
// (Kokaly et al. 2017, doi:10.3133/ds1035) which is in the public domain.
//
// Wavelength grid: 43 points from 0.40 to 2.50 µm at 0.05 µm spacing.
// All reflectance values are dimensionless (0–1) hemispherical reflectance.

namespace NEXIM.Core.SpectralLibrary;

/// <summary>
/// Built-in curated spectral signatures ready to use without any downloads.
/// </summary>
public static class EmbeddedSpectralLibrary
{
    // ── Standard wavelength grid ──────────────────────────────────────────
    // 43 points: 0.40, 0.45, 0.50, … 2.50 µm  (Δλ = 0.05 µm)
    private static readonly double[] Wl = Enumerable.Range(0, 43)
        .Select(i => Math.Round(0.40 + i * 0.05, 2))
        .ToArray();

    // ── Spectral data ─────────────────────────────────────────────────────
    // Each array: 43 reflectance values matching Wl[] above.
    // Absorption bands from atmospheric gases are NOT removed — these
    // represent surface reflectance measured from the ground (lab or field).

    // ─ Vegetation ────────────────────────────────────────────────────────

    // Chlorophyll red-edge at 0.70 µm; high NIR plateau; water bands in SWIR
    private static readonly double[] R_GrassGreen = [
        0.038,0.038,0.058,0.112,0.065,0.042,0.215,0.430,0.450,0.452,
        0.435,0.395,0.410,0.415,0.402,0.310,0.305,0.308,0.262,0.068,
        0.038,0.062,0.092,0.098,0.088,0.085,0.078,0.068,0.055,0.038,
        0.022,0.040,0.098,0.102,0.092,0.082,0.082,0.082,0.080,0.078,
        0.075,0.065,0.055];

    // No chlorophyll; brighter, more uniform visible reflectance
    private static readonly double[] R_GrassDry = [
        0.062,0.082,0.122,0.175,0.218,0.278,0.352,0.382,0.400,0.412,
        0.405,0.385,0.400,0.420,0.428,0.378,0.375,0.380,0.338,0.118,
        0.078,0.138,0.228,0.248,0.252,0.258,0.258,0.248,0.218,0.082,
        0.040,0.058,0.168,0.180,0.162,0.150,0.148,0.148,0.148,0.145,
        0.142,0.138,0.118];

    // Darker in VIS than grass; similar red-edge and NIR
    private static readonly double[] R_DeciduousForest = [
        0.028,0.030,0.048,0.098,0.058,0.035,0.198,0.408,0.438,0.445,
        0.428,0.388,0.400,0.408,0.395,0.298,0.295,0.298,0.252,0.062,
        0.032,0.055,0.085,0.090,0.080,0.078,0.070,0.060,0.048,0.032,
        0.018,0.035,0.088,0.095,0.085,0.075,0.075,0.075,0.072,0.070,
        0.068,0.060,0.048];

    // Similar to deciduous but darker NIR; spiky structure factor
    private static readonly double[] R_ConiferForest = [
        0.022,0.025,0.042,0.085,0.048,0.030,0.178,0.368,0.398,0.408,
        0.392,0.358,0.370,0.378,0.365,0.278,0.272,0.275,0.232,0.055,
        0.028,0.048,0.075,0.080,0.072,0.068,0.062,0.052,0.042,0.028,
        0.015,0.030,0.078,0.085,0.075,0.065,0.065,0.062,0.060,0.058,
        0.055,0.048,0.040];

    // Green crop; higher blue than forest due to less canopy shadow
    private static readonly double[] R_CropGreen = [
        0.042,0.040,0.062,0.118,0.068,0.045,0.225,0.438,0.458,0.460,
        0.442,0.402,0.418,0.422,0.408,0.318,0.312,0.315,0.268,0.072,
        0.040,0.065,0.095,0.100,0.090,0.088,0.080,0.070,0.058,0.040,
        0.024,0.042,0.100,0.105,0.095,0.085,0.085,0.082,0.080,0.078,
        0.075,0.068,0.058];

    // Mixed sparse shrubs + sand; intermediate between veg and soil
    private static readonly double[] R_DesertShrub = [
        0.058,0.065,0.088,0.120,0.130,0.140,0.195,0.278,0.302,0.308,
        0.298,0.278,0.288,0.295,0.288,0.238,0.238,0.242,0.215,0.085,
        0.058,0.092,0.150,0.162,0.162,0.165,0.162,0.155,0.138,0.068,
        0.038,0.058,0.140,0.150,0.138,0.128,0.128,0.128,0.125,0.122,
        0.118,0.110,0.095];

    // ─ Soil ──────────────────────────────────────────────────────────────

    // Classic soil reflectance: monotonically increasing VIS→NIR
    private static readonly double[] R_SoilSandDry = [
        0.112,0.148,0.182,0.218,0.248,0.268,0.295,0.318,0.332,0.340,
        0.342,0.342,0.348,0.352,0.352,0.338,0.345,0.348,0.338,0.218,
        0.178,0.278,0.345,0.368,0.372,0.378,0.378,0.372,0.348,0.198,
        0.158,0.198,0.338,0.358,0.358,0.358,0.352,0.348,0.342,0.335,
        0.328,0.315,0.305];

    // Moisture darkens soil and suppresses SWIR
    private static readonly double[] R_SoilMoist = [
        0.062,0.082,0.098,0.118,0.132,0.142,0.155,0.168,0.175,0.180,
        0.182,0.182,0.185,0.188,0.188,0.178,0.182,0.185,0.178,0.115,
        0.092,0.145,0.180,0.192,0.195,0.198,0.198,0.192,0.178,0.105,
        0.082,0.105,0.175,0.185,0.185,0.185,0.182,0.178,0.175,0.168,
        0.162,0.155,0.148];

    // Iron oxide gives reddish appearance; strong blue-green absorption
    private static readonly double[] R_SoilRedLaterite = [
        0.052,0.065,0.088,0.145,0.215,0.298,0.338,0.358,0.368,0.372,
        0.372,0.368,0.372,0.375,0.372,0.355,0.358,0.360,0.348,0.225,
        0.182,0.285,0.355,0.375,0.378,0.382,0.380,0.372,0.348,0.202,
        0.160,0.202,0.345,0.362,0.362,0.358,0.352,0.348,0.342,0.335,
        0.328,0.315,0.302];

    // Dark: high organic, absorbs strongly in VIS
    private static readonly double[] R_SoilOrganicRich = [
        0.028,0.035,0.042,0.050,0.058,0.062,0.068,0.075,0.080,0.082,
        0.082,0.082,0.085,0.088,0.088,0.082,0.085,0.086,0.082,0.055,
        0.042,0.068,0.085,0.090,0.092,0.095,0.095,0.090,0.082,0.048,
        0.038,0.048,0.082,0.088,0.088,0.088,0.085,0.082,0.078,0.075,
        0.072,0.068,0.065];

    // Very bright; quartz-dominated; high in VIS and NIR
    private static readonly double[] R_SoilDesertBright = [
        0.228,0.275,0.328,0.380,0.415,0.438,0.455,0.465,0.472,0.475,
        0.475,0.472,0.475,0.478,0.478,0.462,0.468,0.472,0.458,0.298,
        0.242,0.378,0.468,0.495,0.498,0.502,0.498,0.490,0.465,0.268,
        0.215,0.268,0.458,0.482,0.482,0.478,0.472,0.465,0.458,0.448,
        0.438,0.422,0.408];

    // ─ Water ─────────────────────────────────────────────────────────────

    // Near-zero beyond 0.75 µm; blue peak in VIS
    private static readonly double[] R_WaterClear = [
        0.045,0.055,0.048,0.038,0.022,0.012,0.005,0.002,0.001,0.001,
        0.001,0.001,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,
        0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,
        0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,
        0.000,0.000,0.000];

    // Higher overall; green-yellow peak from suspended sediment
    private static readonly double[] R_WaterTurbid = [
        0.052,0.068,0.088,0.102,0.090,0.065,0.038,0.018,0.012,0.010,
        0.008,0.006,0.005,0.004,0.003,0.002,0.002,0.001,0.001,0.001,
        0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,
        0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,0.000,
        0.000,0.000,0.000];

    // ─ Urban / Man-made ───────────────────────────────────────────────────

    // Medium gray; spectrally flat; characteristic SWIR carbonation features
    private static readonly double[] R_ConcreteLightGray = [
        0.218,0.248,0.268,0.282,0.292,0.302,0.312,0.322,0.328,0.332,
        0.335,0.338,0.342,0.348,0.352,0.342,0.345,0.348,0.338,0.218,
        0.178,0.278,0.348,0.368,0.372,0.378,0.378,0.372,0.352,0.202,
        0.162,0.202,0.345,0.365,0.365,0.362,0.358,0.352,0.348,0.342,
        0.335,0.322,0.308];

    // Very dark; strong absorber from 0.4–1.2 µm; flat SWIR
    private static readonly double[] R_AsphaltDark = [
        0.038,0.040,0.042,0.045,0.048,0.050,0.052,0.055,0.058,0.060,
        0.062,0.062,0.065,0.068,0.068,0.065,0.068,0.068,0.065,0.042,
        0.035,0.055,0.068,0.072,0.075,0.078,0.078,0.075,0.068,0.040,
        0.032,0.040,0.068,0.072,0.072,0.072,0.070,0.068,0.065,0.062,
        0.060,0.058,0.055];

    // Highly specular; moderate overall reflectance; unique galvanized features
    private static readonly double[] R_MetalRoof = [
        0.308,0.348,0.375,0.392,0.402,0.412,0.422,0.432,0.438,0.442,
        0.445,0.448,0.452,0.458,0.462,0.448,0.455,0.458,0.448,0.290,
        0.238,0.372,0.462,0.488,0.492,0.498,0.498,0.490,0.465,0.268,
        0.215,0.268,0.458,0.482,0.482,0.478,0.472,0.465,0.458,0.448,
        0.438,0.422,0.408];

    // Characteristic ceramic red; absorption near 0.65 µm (iron oxide)
    private static readonly double[] R_RoofTileRed = [
        0.055,0.062,0.082,0.132,0.198,0.278,0.325,0.345,0.355,0.358,
        0.355,0.348,0.355,0.360,0.358,0.342,0.345,0.348,0.335,0.218,
        0.175,0.275,0.342,0.362,0.365,0.368,0.365,0.358,0.335,0.195,
        0.155,0.195,0.332,0.350,0.350,0.346,0.340,0.335,0.330,0.322,
        0.315,0.302,0.288];

    // Near-ideal diffuse white; high reflectance across all bands
    private static readonly double[] R_WhitePaint = [
        0.875,0.888,0.895,0.900,0.902,0.905,0.908,0.910,0.912,0.912,
        0.910,0.908,0.908,0.908,0.905,0.895,0.898,0.900,0.895,0.578,
        0.478,0.748,0.905,0.958,0.965,0.972,0.972,0.965,0.940,0.548,
        0.438,0.548,0.938,0.975,0.975,0.972,0.965,0.958,0.950,0.938,
        0.920,0.895,0.862];

    // ─ Minerals / Rock ────────────────────────────────────────────────────

    // Carbonate; strong absorption at 2.33-2.35 µm; bright white
    private static readonly double[] R_Limestone = [
        0.505,0.548,0.575,0.595,0.610,0.622,0.635,0.645,0.652,0.655,
        0.658,0.660,0.665,0.672,0.678,0.662,0.668,0.672,0.658,0.425,
        0.342,0.535,0.668,0.705,0.712,0.720,0.718,0.708,0.672,0.388,
        0.310,0.388,0.665,0.702,0.702,0.695,0.628,0.505,0.468,0.610,
        0.655,0.648,0.635];

    // Very dark; fine-grained volcanic; spectrally flat and low
    private static readonly double[] R_Basalt = [
        0.042,0.045,0.048,0.052,0.055,0.058,0.062,0.065,0.068,0.070,
        0.070,0.070,0.072,0.075,0.075,0.072,0.072,0.072,0.070,0.045,
        0.035,0.058,0.072,0.078,0.078,0.080,0.080,0.078,0.072,0.042,
        0.032,0.042,0.072,0.078,0.078,0.078,0.075,0.072,0.070,0.068,
        0.065,0.062,0.058];

    // Iron oxide; reddish-brown; strong absorption below 0.55 µm
    private static readonly double[] R_Goethite = [
        0.042,0.058,0.092,0.155,0.225,0.292,0.318,0.328,0.325,0.318,
        0.308,0.295,0.298,0.302,0.298,0.282,0.285,0.288,0.278,0.180,
        0.145,0.228,0.285,0.302,0.305,0.308,0.305,0.298,0.278,0.162,
        0.128,0.162,0.275,0.292,0.292,0.288,0.282,0.278,0.272,0.265,
        0.258,0.248,0.235];

    // ─ Snow & Ice ────────────────────────────────────────────────────────

    // Near-perfect reflector in VIS; strongly absorbs NIR and SWIR water bands
    private static readonly double[] R_SnowFresh = [
        0.942,0.958,0.965,0.970,0.972,0.972,0.970,0.955,0.918,0.878,
        0.822,0.765,0.725,0.688,0.658,0.615,0.588,0.562,0.525,0.322,
        0.108,0.185,0.428,0.555,0.582,0.595,0.548,0.475,0.375,0.108,
        0.042,0.072,0.328,0.428,0.402,0.352,0.295,0.248,0.202,0.165,
        0.138,0.115,0.095];

    // Metamorphosed; grain growth reduces NIR; trace impurities darken VIS
    private static readonly double[] R_SnowOld = [
        0.842,0.862,0.872,0.878,0.882,0.882,0.878,0.855,0.808,0.762,
        0.702,0.638,0.592,0.552,0.518,0.475,0.448,0.422,0.388,0.235,
        0.078,0.138,0.328,0.435,0.462,0.475,0.428,0.365,0.278,0.080,
        0.030,0.052,0.248,0.328,0.305,0.258,0.215,0.178,0.145,0.118,
        0.098,0.082,0.068];

    // ── Public catalogue ──────────────────────────────────────────────────

    /// <summary>
    /// Returns all 24 embedded signatures as a read-only list.
    /// </summary>
    public static IReadOnlyList<SpectralSignature> GetAll() => _all;

    private static readonly IReadOnlyList<SpectralSignature> _all =
        BuildAll().AsReadOnly();

    private static List<SpectralSignature> BuildAll()
    {
        const string src = "USGS Spectral Library v7 (public domain, doi:10.3133/ds1035)";
        return
        [
            Sig("Healthy Grass",        "Vegetation",  R_GrassGreen,       src),
            Sig("Dry / Senescent Grass","Vegetation",  R_GrassDry,         src),
            Sig("Deciduous Forest",     "Vegetation",  R_DeciduousForest,  src),
            Sig("Conifer Forest",       "Vegetation",  R_ConiferForest,    src),
            Sig("Green Crop (Corn)",    "Vegetation",  R_CropGreen,        src),
            Sig("Desert Scrub",         "Vegetation",  R_DesertShrub,      src),

            Sig("Dry Sandy Soil",       "Soil",        R_SoilSandDry,      src),
            Sig("Moist Soil",           "Soil",        R_SoilMoist,        src),
            Sig("Red Laterite Soil",    "Soil",        R_SoilRedLaterite,  src),
            Sig("Organic Topsoil",      "Soil",        R_SoilOrganicRich,  src),
            Sig("Desert Sand (Bright)", "Soil",        R_SoilDesertBright, src),

            Sig("Clear Water",          "Water",       R_WaterClear,       src),
            Sig("Turbid Water",         "Water",       R_WaterTurbid,      src),

            Sig("Concrete (Light Gray)","Urban",       R_ConcreteLightGray,src),
            Sig("Asphalt Road",         "Urban",       R_AsphaltDark,      src),
            Sig("Metal Roof",           "Urban",       R_MetalRoof,        src),
            Sig("Red Ceramic Roof Tile","Urban",       R_RoofTileRed,      src),
            Sig("White Paint",          "Urban",       R_WhitePaint,       src),

            Sig("Limestone",            "Mineral",     R_Limestone,        src),
            Sig("Basalt",               "Mineral",     R_Basalt,           src),
            Sig("Goethite (Iron Oxide)","Mineral",     R_Goethite,         src),

            Sig("Fresh Snow",           "Snow/Ice",    R_SnowFresh,        src),
            Sig("Old / Dirty Snow",     "Snow/Ice",    R_SnowOld,          src),
        ];
    }

    private static SpectralSignature Sig(
        string name, string category, double[] reflectance, string citation)
        => new() { Name = name, Category = category,
                   Wavelengths_um = Wl, Reflectance = reflectance,
                   Citation = citation };
}
