using NEXIM.Core.Models;

namespace NEXIM.Core.Atmospheric;

/// <summary>
/// Tabulated standard atmosphere layer data used when <see cref="AtmosphericProfile"/>
/// specifies a preset (not custom layers).
///
/// Data source: Anderson et al. (1986) AFGL-TR-86-0110, DTIC ADA175173.
/// Six standard atmospheres: US Standard (1976), Tropical, Mid-Latitude Summer/Winter,
/// Sub-Arctic Summer/Winter.
///
/// This class provides a simplified 18-level US Standard Atmosphere 1976 for use
/// when k-table integration is the primary accuracy path.
/// Reference: COESA (1976) U.S. Standard Atmosphere, US Government Printing Office.
/// </summary>
public static class StandardAtmosphereLayers
{
    /// <summary>
    /// 18-level US Standard Atmosphere (1976) from surface (1013.25 hPa) to 70 km.
    /// Levels are ordered surface-to-top (ascending altitude), matching
    /// <see cref="AtmosphericProfile.CustomLayers"/> convention.
    ///
    /// H₂O column amounts are from Anderson et al. (1986) Table 2 (US Standard).
    /// O₃ amounts are from the same table in Dobson units / layer.
    /// CO₂ and CH₄ are set to 2024 global mean values (421 ppm, 1.9 ppm).
    /// </summary>
    public static readonly AtmosphericLayer[] USStandard18Levels =
    [
        // Alt base, Alt top,  P_hPa,    T_K,    H2O g/cm², O3 atm-cm,  CO2 VMR,    CH4 VMR
        new() { AltitudeBase_km=0.0,  AltitudeTop_km=1.0,  Pressure_hPa=1013.25, Temperature_K=288.15, H2O_g_cm2=1.400e0,  O3_atm_cm=3.0e-4,  CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=1.0,  AltitudeTop_km=2.0,  Pressure_hPa= 898.76, Temperature_K=281.65, H2O_g_cm2=8.000e-1, O3_atm_cm=3.5e-4,  CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=2.0,  AltitudeTop_km=3.0,  Pressure_hPa= 795.01, Temperature_K=275.15, H2O_g_cm2=4.000e-1, O3_atm_cm=3.9e-4,  CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=3.0,  AltitudeTop_km=4.0,  Pressure_hPa= 701.21, Temperature_K=268.66, H2O_g_cm2=2.000e-1, O3_atm_cm=4.3e-4,  CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=4.0,  AltitudeTop_km=5.0,  Pressure_hPa= 616.60, Temperature_K=262.17, H2O_g_cm2=8.000e-2, O3_atm_cm=4.7e-4,  CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=5.0,  AltitudeTop_km=7.0,  Pressure_hPa= 540.48, Temperature_K=255.68, H2O_g_cm2=3.000e-2, O3_atm_cm=5.3e-4,  CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=7.0,  AltitudeTop_km=9.0,  Pressure_hPa= 411.05, Temperature_K=242.70, H2O_g_cm2=5.000e-3, O3_atm_cm=6.1e-4,  CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=9.0,  AltitudeTop_km=11.0, Pressure_hPa= 308.00, Temperature_K=229.73, H2O_g_cm2=1.500e-3, O3_atm_cm=8.2e-4,  CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=11.0, AltitudeTop_km=13.0, Pressure_hPa= 226.99, Temperature_K=216.65, H2O_g_cm2=5.000e-4, O3_atm_cm=1.20e-3, CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=13.0, AltitudeTop_km=15.0, Pressure_hPa= 165.79, Temperature_K=216.65, H2O_g_cm2=3.000e-4, O3_atm_cm=2.00e-3, CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=15.0, AltitudeTop_km=18.0, Pressure_hPa= 121.11, Temperature_K=216.65, H2O_g_cm2=1.500e-4, O3_atm_cm=4.00e-3, CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=18.0, AltitudeTop_km=21.0, Pressure_hPa=  75.65, Temperature_K=221.65, H2O_g_cm2=8.000e-5, O3_atm_cm=5.50e-3, CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=21.0, AltitudeTop_km=25.0, Pressure_hPa=  47.29, Temperature_K=230.65, H2O_g_cm2=5.000e-5, O3_atm_cm=4.50e-3, CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=25.0, AltitudeTop_km=30.0, Pressure_hPa=  25.49, Temperature_K=240.65, H2O_g_cm2=3.000e-5, O3_atm_cm=3.00e-3, CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=30.0, AltitudeTop_km=40.0, Pressure_hPa=  11.97, Temperature_K=265.65, H2O_g_cm2=5.000e-6, O3_atm_cm=1.80e-3, CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=40.0, AltitudeTop_km=50.0, Pressure_hPa=   2.87, Temperature_K=270.65, H2O_g_cm2=5.000e-7, O3_atm_cm=5.00e-4, CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=50.0, AltitudeTop_km=60.0, Pressure_hPa=   7.98e-1, Temperature_K=247.02, H2O_g_cm2=1.000e-7, O3_atm_cm=1.00e-4, CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
        new() { AltitudeBase_km=60.0, AltitudeTop_km=70.0, Pressure_hPa=   2.20e-1, Temperature_K=219.70, H2O_g_cm2=5.000e-8, O3_atm_cm=2.00e-5, CO2_VMR=421e-6, CH4_VMR=1.9e-6 },
    ];
}
