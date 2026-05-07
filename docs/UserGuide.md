# NEXIM User Guide

**NEXIM — Novel End-to-eXtended hyperspectral IMage simulator**  
Version 1.0 · Pure C# · .NET 9 · WinForms · UV–Far-IR (0.4–2.5 µm, v1)

---

## Table of Contents

1. [Overview](#1-overview)
2. [System Requirements](#2-system-requirements)
3. [Building from Source in Visual Studio 2022](#3-building-from-source-in-visual-studio-2022)
4. [First-Time Setup: Generating Data Assets](#4-first-time-setup-generating-data-assets)
5. [Application Layout](#5-application-layout)
6. [Scene Setup Tab](#6-scene-setup-tab)
7. [Atmosphere Tab](#7-atmosphere-tab)
8. [Sensor Tab](#8-sensor-tab)
9. [Segmentation Tab](#9-segmentation-tab)
10. [Running a Simulation](#10-running-a-simulation)
11. [Exporting Results](#11-exporting-results)
12. [Output File Formats](#12-output-file-formats)
13. [Atmospheric Mode Reference](#13-atmospheric-mode-reference)
14. [Segmentation Algorithm Reference](#14-segmentation-algorithm-reference)
15. [BRDF Model Reference](#15-brdf-model-reference)
16. [Troubleshooting](#16-troubleshooting)
17. [Academic References](#17-academic-references)

---

## 1. Overview

NEXIM is a physics-based hyperspectral scene simulator that produces synthetic at-sensor
radiance image cubes from configurable scene geometry, atmospheric conditions, and sensor
specifications.  It covers the reflected solar spectrum from 0.4 µm (violet) to 2.5 µm
(SWIR) in version 1, with far-IR thermal emission extensions planned for v2.

**What NEXIM computes:**

```
Sun → TOA solar irradiance
         ↓ atmospheric extinction (H₂O, O₃, aerosol, Rayleigh)
Scene surface (configurable BRDF, albedo, material) 
         ↓ reflected + emitted radiance
         ↓ upwelling atmospheric transmittance
Sensor (SRF convolution + FPA noise model)
         ↓
At-sensor spectral radiance [W m⁻² sr⁻¹ µm⁻¹]
Digital number (DN) cube [integer, 12–16 bit]
```

Three **atmospheric RT modes** are available, offering a speed/accuracy tradeoff:

| Mode | Engine | Speed | Accuracy | Use case |
|------|--------|-------|----------|----------|
| **1 — Fast** | Pre-computed LUT (5-D interpolation) | < 10 ms | ±5–10% | Interactive preview |
| **2 — Accurate** | CKD + DISORT 8-stream (pure C#) | 100–500 ms | ±0.5–1% (MODTRAN-class) | Publication-quality |
| **3 — Full-Physics** | Monte Carlo + Mie scattering | 5–30 s | ±0.1–0.5% | Maximum fidelity, 3D clouds |

---

## 2. System Requirements

### Minimum
| Component | Requirement |
|-----------|-------------|
| OS | Windows 10 22H2 or Windows 11 |
| CPU | x86-64, 4 logical cores |
| RAM | 4 GB |
| Disk | 500 MB (application) + 70 MB (data assets) |
| .NET | .NET 9.0 Desktop Runtime (installed automatically by VS 2022) |
| Visual Studio | VS 2022 version 17.8 or later, with **.NET desktop development** workload |

### Recommended
| Component | Recommendation |
|-----------|---------------|
| CPU | 8+ cores (benefits Mode 2 LUT generation and Monte Carlo Mode 3) |
| RAM | 8 GB+ |
| GPU | Any CUDA 11+ or OpenCL 2.0 device (accelerates Monte Carlo RT in Mode 3) |
| Disk | SSD (faster LUT file I/O) |

### LUT generation hardware sizing
The `NEXIM.LutGen` tool runs one `AccurateAtmosphericRT` call per grid point (5 544
total calls), parallelised across all logical cores:

| Core count | Approximate LUT generation time |
|------------|----------------------------------|
| 4 cores | ~20 minutes |
| 8 cores | ~10 minutes |
| 16 cores | ~5 minutes |

K-table generation (250 files) takes under 30 seconds on any modern CPU.

---

## 3. Building from Source in Visual Studio 2022

### Step-by-step

1. **Clone or download** the repository to a local folder, e.g. `D:\VSCodeRepo\NEXIM`.

2. Open **Visual Studio 2022**.

3. Choose **File → Open → Project/Solution** and navigate to `NEXIM.slnx`.  
   VS 2022 17.8+ natively supports the SDK-style `.slnx` solution format.

4. VS will restore NuGet packages automatically on first open.  
   If prompted, click **Restore** in the notification bar.

5. Set the startup project: in **Solution Explorer**, right-click `NEXIM.UI` →
   **Set as Startup Project**.

6. Select the **Debug** or **Release** configuration from the toolbar.

7. Press **F5** (Debug) or **Ctrl+F5** (Run without debugging) to build and launch.

### Solution structure

```
NEXIM.slnx
├── src/
│   ├── NEXIM.Core/          All physics engines, models, I/O, segmentation
│   └── NEXIM.UI/            WinForms application (net9.0-windows)
├── tests/
│   └── NEXIM.Tests/         xUnit 2.9.2 test suite (145 tests)
├── tools/
│   └── NEXIM.LutGen/        Dev-only data generation tool
└── data/
    ├── hitran_kdist/        H₂O k-table files (generated, not in source control)
    └── lut/                 nexim_v1.lut (generated, not in source control)
```

### Running unit tests
Open **Test → Run All Tests** in VS 2022, or from the command line:
```
cd D:\VSCodeRepo\NEXIM
dotnet test NEXIM.slnx -v q
```
Expected result: **145 / 145 passed**.

---

## 4. First-Time Setup: Generating Data Assets

NEXIM requires two pre-computed data files before it can perform atmospheric simulations.
These are **not** included in the repository (too large for source control) and must be
generated once using the `NEXIM.LutGen` tool.

### What the tool generates

| Asset | Location | Size | Required by |
|-------|----------|------|------------|
| H₂O k-table files (250 files) | `data/hitran_kdist/H2O_0000.ktbl` … `H2O_0249.ktbl` | ~50 MB | Mode 2 (Accurate) |
| Atmospheric LUT | `data/lut/nexim_v1.lut` | ~16 MB | Mode 1 (Fast) |

### Running NEXIM.LutGen in Visual Studio 2022

1. In **Solution Explorer**, right-click `NEXIM.LutGen` → **Set as Startup Project**.

2. Open **Project → NEXIM.LutGen Properties → Debug → General → Command line arguments**
   and enter one of:

   | Command | What it does |
   |---------|-------------|
   | `all` | Generate k-tables then LUT (recommended for first run) |
   | `ktables` | Generate k-tables only |
   | `lut` | Generate LUT only (requires k-tables to already exist) |

   Optional arguments:

   | Argument | Default | Description |
   |----------|---------|-------------|
   | `--ktables-dir <path>` | `data/hitran_kdist` | Output directory for `.ktbl` files |
   | `--lut-file <path>` | `data/lut/nexim_v1.lut` | Output path for the LUT file |
   | `--threads <n>` | All logical cores | Parallelism degree |

3. Press **Ctrl+F5** to run.  Progress is printed to the Output window:
   ```
   NEXIM.LutGen  —  hyperspectral atmospheric data generator
     Command      : all
     K-tables dir : data/hitran_kdist
     LUT file     : data/lut/nexim_v1.lut
     Threads      : 8

   === Step 1/2: Generating k-distribution tables ===
     Generating 250 H₂O k-tables into: data/hitran_kdist
       25/250 k-tables written  (0.576 µm)
       50/250 k-tables written  (0.828 µm)
       ...
     H₂O k-tables: 250 files written.

   === Step 2/2: Generating atmospheric LUT ===
     LUT grid: 11×9×7×8×250 = 4 158 000 floats
       100/5544 runs done  (SZA=0° VZA=10° AOD=0.10 WVC=0.5)
       ...
     Writing LUT → data/lut/nexim_v1.lut
     LUT file size: 16328 KiB

   Done.
   ```

4. After completion, switch the startup project back to **NEXIM.UI**.

> **Note:** You only need to run this once per machine.  The generated files are
> deterministic; re-running produces bit-identical output.

---

## 5. Application Layout

The NEXIM main window has four tabs arranged left-to-right reflecting the
simulation pipeline:

```
┌─────────────────────────────────────────────────────────────────┐
│  NEXIM — Novel End-to-eXtended hyperspectral IMage simulator    │
├────────────┬──────────────┬──────────────┬─────────────────────┤
│ Scene Setup│  Atmosphere  │    Sensor    │   Segmentation      │
│            │              │              │                     │
│  (Step 1)  │   (Step 2)   │   (Step 3)   │     (Step 4)        │
│            │              │              │                     │
├────────────┴──────────────┴──────────────┴─────────────────────┤
│  [  Run Simulation  ]          [  Export Results  ]            │
│  ████████████████░░░░  75%     Status: Computing band 187/250  │
└─────────────────────────────────────────────────────────────────┘
```

- **Run Simulation** executes the full pipeline using all four tabs' current settings.
- **Export Results** writes the last simulation's output in the chosen format.
- The **progress bar** and **status label** show real-time pipeline progress.

---

## 6. Scene Setup Tab

Defines the spatial dimensions of the synthetic image and the illumination/viewing
geometry.

### Spatial geometry

| Control | Default | Description |
|---------|---------|-------------|
| Rows | 32 | Number of image rows (pixels along track) |
| Columns | 32 | Number of image columns (pixels across track) |
| Pixel size (m) | 5.0 | Ground sampling distance per pixel [metres] |

A 32 × 32 scene at 5 m GSD represents a 160 m × 160 m ground footprint.
Increasing rows/columns extends simulation time proportionally.

### Sun geometry

| Control | Default | Range | Description |
|---------|---------|-------|-------------|
| Solar zenith (°) | 30 | 0–89 | 0° = sun directly overhead (zenith); 90° = horizon |
| Solar azimuth (°) | 135 | 0–360 | Measured clockwise from north |

### Sensor geometry

| Control | Default | Range | Description |
|---------|---------|-------|-------------|
| View zenith (°) | 0 | 0–70 | 0° = nadir-looking |
| View azimuth (°) | 0 | 0–360 | Measured clockwise from north |

### Surface BRDF

All pixels in v1 share a single surface BRDF model.  Per-pixel material maps are
planned for v2.

| Control | Description |
|---------|-------------|
| BRDF model | Lambertian / Oren-Nayar / GGX / Hapke (see [Section 15](#15-brdf-model-reference)) |
| Albedo | Broadband hemispherical reflectance (0.0–1.0) |
| Roughness | Surface roughness parameter (0.0–1.0; used by Oren-Nayar, GGX, Hapke) |

> **Tip:** For vegetation, use albedo ≈ 0.10 with Lambertian.  For bare soil, use
> albedo ≈ 0.25–0.35.  For snow, use albedo ≈ 0.85 with Hapke and roughness ≈ 0.1.

---

## 7. Atmosphere Tab

Controls the spectral wavelength grid and the atmospheric radiative transfer calculation.

### Atmospheric mode

| Radio button | Engine | Speed | Best for |
|-------------|--------|-------|----------|
| **Mode 1 — Fast** | Pre-computed LUT | < 10 ms | Quick preview, large scenes |
| **Mode 2 — Accurate** | CKD + DISORT 8-stream | ~200 ms | Publication-quality results |
| **Mode 3 — Full-Physics** | Monte Carlo + Mie | ~10 s | Cloudy scenes, maximum accuracy |

> **Fallback behaviour:** If Mode 1 is selected but `data/lut/nexim_v1.lut` is not found,
> the application automatically falls back to Mode 2 and shows a notification in the
> status bar.

### Wavelength grid

| Control | Default | Description |
|---------|---------|-------------|
| Start wavelength (µm) | 0.40 | Minimum wavelength of simulation grid |
| End wavelength (µm) | 2.50 | Maximum wavelength of simulation grid |
| Step (µm) | 0.010 | Spectral sampling interval |

The number of spectral bands is computed as `floor((End − Start) / Step) + 1`.
Default: 211 bands.  The sensor SRF then convolves this high-resolution grid down to
the sensor's actual band count.

**Recommended step sizes by application:**

| Application | Step (µm) | Bands (0.4–2.5) |
|-------------|-----------|-----------------|
| Fast preview | 0.05 | 43 |
| Standard | 0.01 | 211 |
| High resolution | 0.004 | 526 |

### Atmospheric profile

| Profile | Description |
|---------|-------------|
| US Standard (1976) | Mid-latitude annual mean; most common choice |
| Tropical | High humidity, warm surface; equatorial scenes |
| Midlatitude Summer | Higher water vapour than US Standard |
| Midlatitude Winter | Lower humidity, cold surface |
| Subarctic Summer | Low-to-moderate humidity |
| Subarctic Winter | Very dry, cold |

All profiles follow AFGL-TR-86-0110 (Anderson et al. 1986).

### Aerosol

| Control | Default | Range | Description |
|---------|---------|-------|-------------|
| AOT@550 nm | 0.20 | 0.0–3.0 | Aerosol optical thickness at 550 nm (continental aerosol model) |

> **Typical AOT values:** Clean maritime: 0.05–0.1; Rural: 0.1–0.3; Urban: 0.3–0.8;
> Heavy smoke/dust: 1.0–3.0.

### Altitude

| Control | Default | Description |
|---------|---------|-------------|
| Sensor altitude (km) | 0 | Ground level above MSL.  Affects where the atmosphere is sliced. |

---

## 8. Sensor Tab

Defines the imaging spectrometer — spectral response function, optics, focal-plane
array, and noise model.

### Spectral bands

| Control | Default | Description |
|---------|---------|-------------|
| Number of sensor bands | 50 | Spectral channels output in the image cube |
| Start wavelength (µm) | 0.40 | First sensor channel centre |
| End wavelength (µm) | 2.50 | Last sensor channel centre |
| FWHM fraction | 0.70 | SRF full-width half-maximum as a fraction of the inter-channel spacing |

Sensor band centres are uniformly spaced between Start and End.  With 50 bands from
0.40–2.50 µm, the spacing is 42.9 nm and the default FWHM is ~30 nm.

### Detector quantum efficiency

| Control | Default | Description |
|---------|---------|-------------|
| Peak QE | 0.80 | Peak detector quantum efficiency (0–1) |
| Optics transmittance | 0.50 | Combined fore-optics transmittance (0–1) |

### Focal-plane array

| Control | Default | Description |
|---------|---------|-------------|
| IFOV (mrad) | 1.0 | Instantaneous field of view per pixel [mrad] |
| Altitude (m) | 5 000 | Platform altitude above ground [metres]; determines projected pixel size |
| Pixel pitch (µm) | 25 | Physical detector pixel size [µm] |
| Integration time (ms) | 5 | Per-line integration (dwell) time [ms] |
| Full-well capacity (e⁻) | 80 000 | Maximum electrons before saturation |
| ADC bits | 14 | Analogue-to-digital converter bit depth |
| Read noise (e⁻) | 50 | RMS read noise per pixel per frame |
| Dark current (e⁻/s) | 500 | Dark current rate at operating temperature |

### Noise model

NEXIM computes per-pixel noise contributions:

| Noise source | Model |
|-------------|-------|
| Shot noise | Poisson statistics (Knuth algorithm) on signal electrons |
| Read noise | Gaussian (Box-Muller) with σ = read noise value |
| Dark current | Gaussian with mean = rate × integration time |
| Quantisation | Uniform within ±½ LSB |

The **Noise Equivalent Radiance (NEdL)** is reported in the simulation log.

---

## 9. Segmentation Tab

Applies spectral clustering to the simulated radiance cube after simulation.

### Algorithm selection

| Algorithm | Type | Key parameter | Description |
|-----------|------|---------------|-------------|
| **K-means** | Partitional | Number of clusters (k) | Fast, globally optimal solution via ML.NET |
| **GMM** | Probabilistic | Number of components | Soft cluster assignments; outputs class probabilities |
| **DBSCAN** | Density-based | ε (spectral distance), MinPts | Noise-robust; automatically determines cluster count |
| **SAM** | Spectral angle | Angle threshold (radians) | Classifies by spectral angle to reference endmembers |

### SAM endmember editor

SAM (Spectral Angle Mapper) requires user-specified reference endmember spectra.
Click **Edit Endmembers…** to open the endmember editor dialog:

1. Click **Add** to add a new endmember row.
2. Enter a **name** (e.g. "Vegetation", "Bare soil").
3. Enter the **spectrum** as a comma-separated list of reflectance values
   (one value per sensor wavelength, 0–1 range).
4. Click **OK** to confirm.

If no endmembers are provided, SAM falls back to K-means-derived centroids.

### Result display

After segmentation, a **pseudo-colour label map** is rendered in the panel:
each unique class label is assigned a distinct colour.  The map updates automatically
after each simulation + segmentation run.

---

## 10. Running a Simulation

### Workflow

1. Configure all four tabs to your desired scene, atmosphere, sensor, and segmentation settings.
2. Click **Run Simulation**.
3. The progress bar advances in two phases:
   - **Phase 1 (0–50%):** Atmospheric RT — computing transmittance, path radiance, and
     downwelling irradiance for the current geometry.
   - **Phase 2 (50–100%):** Per-pixel sensor simulation — convolving the spectrum, applying
     the noise model, and producing DN values for each pixel.
4. When complete, the status bar shows `"Simulation complete (XXX ms)"`.

### Simulation time guide

| Mode | Bands | Scene | Typical time |
|------|-------|-------|-------------|
| Mode 1 (Fast) | 211 | 32×32 | < 1 s |
| Mode 2 (Accurate) | 211 | 32×32 | 5–15 s |
| Mode 3 (Full-Physics) | 211 | 32×32 | 2–10 min |

> **Tip:** Use Mode 1 for parameter exploration and switch to Mode 2 before final export.

### Cancellation

There is currently no cancel button in v1.  To abort a long Mode 3 simulation, close
and reopen the application.

---

## 11. Exporting Results

Click **Export Results** after a successful simulation.  A Save dialog appears with
three format options:

| Filter | Format | Description |
|--------|--------|-------------|
| `*.nxi` | NEXIM native | Binary header + JSON metadata + float32 BIL + CRC32 |
| `*.img` | ENVI standard | `.img` float32 BIL + `.hdr` text header |
| `*.csv` | Long-form CSV | Row, Column, Band, Wavelength_um, Radiance values |

Choose a filename and click **Save**.

---

## 12. Output File Formats

### 12.1 .nxi — NEXIM Native Format

The `.nxi` format is NEXIM's primary interchange format.  It is self-describing
(the JSON metadata block carries all axis information) and integrity-protected.

**Binary layout:**

| Offset | Size | Field |
|--------|------|-------|
| 0 | 4 bytes | Magic: `4E 58 49 01` (`NXI\x01`) |
| 4 | 4 bytes | Rows (uint32 LE) |
| 8 | 4 bytes | Bands (uint32 LE) |
| 12 | 4 bytes | Columns (uint32 LE) |
| 16 | 2 bytes | Bit depth: `20 00` (float32 = 32, stored as uint16) |
| 18 | 2 bytes | Reserved (zero) |
| 20 | 4 bytes | JSON metadata length in bytes (uint32 LE) |
| 24 | N bytes | JSON metadata (UTF-8) — see below |
| 24+N | R×B×C×4 bytes | Float32 BIL data: `[row][band][column]` |
| End-4 | 4 bytes | CRC32 (covers all preceding bytes) |

**JSON metadata fields:**

```json
{
  "SceneName": "MySim_001",
  "CreatedUtc": "2026-05-06T14:23:00Z",
  "Wavelengths_um": [0.400, 0.410, ...],
  "Fwhm_um": [0.008, 0.008, ...],
  "Extras": {
    "AtmosphericMode": "ACCURATE (CKD+DISORT 8-stream)",
    "SolarZenith_deg": "30",
    "AOT550": "0.2"
  }
}
```

**Reading .nxi files** from external software:
1. Read the fixed 24-byte header to get dimensions and JSON length.
2. Parse the JSON metadata block.
3. Read `Rows × Bands × Columns` float32 values (little-endian).
4. Verify the trailing CRC32 matches `Crc32(all bytes except last 4)`.

### 12.2 ENVI Standard

NEXIM produces an `.img` file (raw float32 BIL) and an `.hdr` text header.

**Sample `.hdr` file:**
```
ENVI
description = {NEXIM export}
samples = 32
lines = 32
bands = 50
header offset = 0
file type = ENVI Standard
data type = 4
interleave = bil
byte order = 0
wavelength units = Nanometers
wavelength = {400.0, 442.0, 484.0, ..., 2500.0}
```

This format is compatible with ENVI, QGIS (with GDAL), Python (spectral, rasterio),
and MATLAB.

**Reading with Python (spectral library):**
```python
import spectral
img = spectral.open_image('output.hdr')
cube = img.load()          # shape: (rows, cols, bands)
wl   = img.bands.centers   # wavelengths in nm
```

**Reading with MATLAB:**
```matlab
[cube, info] = multibandread('output.img', [32, 32, 50], 'float32', 0, 'bil', 'ieee-le');
```

### 12.3 Long-form CSV

Each row in the CSV represents one (row, column, band) measurement:

```
Row,Column,Band,Wavelength_um,Radiance_Wm2sr1um1
0,0,0,0.400,0.04512
0,0,1,0.410,0.04889
...
```

This format is convenient for Python pandas analysis but is substantially larger than
binary formats.  For a 32×32 scene with 50 bands, the CSV contains 51 201 rows
(including header).

---

## 13. Atmospheric Mode Reference

### Mode 1 — Fast (LUT)

**Algorithm:** 5-D multilinear interpolation in a pre-computed lookup table.

The LUT was generated by running Mode 2 over a structured grid:

| Axis | Nodes | Values |
|------|-------|--------|
| Solar zenith angle | 11 | 0, 10, 20, 30, 40, 50, 60, 65, 70, 75, 80° |
| View zenith angle | 9 | 0, 10, 20, 30, 40, 50, 60, 70, 80° |
| AOD@550 nm | 7 | 0.0, 0.05, 0.1, 0.2, 0.4, 0.8, 2.0 |
| Water vapour column | 8 | 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0 g cm⁻² |
| Wavelength | 250 | 0.4–2.5 µm |

**Accuracy degradation cases:**
- Solar or view zenith > 80° (beyond LUT boundary — extrapolates, error increases)
- AOD > 2.0 (LUT upper limit; heavy smoke/dust → use Mode 2 or 3)
- Very dry atmospheres (WVC < 0.5 g cm⁻²; high-altitude desert → use Mode 2)

### Mode 2 — Accurate (CKD+DISORT)

**Spectral integration:** Correlated-k distribution (Lacis & Oinas 1991).  The
k-distribution collapses the line-by-line integral into 16 Gauss-Legendre quadrature
points in g-space per spectral band.

**RT solver:** DISORT 8-stream (Stamnes et al. 1988) — discrete ordinates with
delta-M flux scaling for peaked aerosol phase functions.

**Gas species treated:**
- H₂O: CKD k-tables (7 band clusters, aggregate Lorentzian line model)
- All other species (O₃, CO₂, CH₄): Beer-Lambert approximation using AFGL profiles

**Output fields per wavelength:** transmittance τ, path radiance L_path, downwelling
irradiance E_down.

**Accuracy:** ±0.5–1% relative to MODTRAN6 over 0.4–2.5 µm under standard conditions.
Accuracy degrades near strong water vapour bands (1.38 µm, 1.87 µm) where the
Beer-Lambert fallback for non-H₂O species is less accurate.

### Mode 3 — Full-Physics (Monte Carlo)

**Algorithm:** Forward Monte Carlo photon tracing with Russian-roulette weight cutoff.
100 000 photons per spectral band (statistical error ≈ 0.3%).

**Aerosol phase function:** Full Mie scattering (Bohren & Huffman 1983, §4.4
size-parameter series) with user-specified effective radius and refractive index.

**Cloud support:** Uniform-slab liquid water clouds are supported via `CloudField`
(optical depth, droplet effective radius, cloud-base and top altitudes).

**GPU acceleration:** Automatically uses the first available CUDA 11+ or OpenCL 2.0
device.  Falls back to CPU multi-threading if no compatible GPU is found.

**When to use Mode 3:**
- Scenes with clouds (Mode 1/2 are clear-sky only)
- When adjacency effects between adjacent pixels matter (high-contrast scenes)
- When statistical validation of Mode 2 is required

---

## 14. Segmentation Algorithm Reference

### K-means

Minimises within-cluster sum-of-squared spectral distances using Lloyd's algorithm,
implemented via ML.NET `KMeansTrainer`.

- **Input:** spectral feature vector per pixel (sensor band radiances, normalised)
- **Output:** hard cluster assignment 0…k−1
- **Convergence:** 100 iterations maximum; early termination when centroid shift < 10⁻⁶
- **Initialisation:** k-means++ (ML.NET default)

> **Recommended k:** For most hyperspectral scenes, k = 5–15 gives meaningful
> spectral classes.  Too-high k leads to over-segmentation; use DBSCAN to validate.

### GMM (Gaussian Mixture Model)

Diagonal-covariance EM algorithm.  Initialised from K-means centroids (same k).

- **Output:** both hard MAP labels and per-pixel class probabilities
- **Convergence:** 200 EM iterations or log-likelihood change < 10⁻⁶

> **When to use:** When uncertainty quantification per pixel is required (e.g. mixed
> pixels at class boundaries).

### DBSCAN

Density-based spatial clustering in spectral space.

| Parameter | Description | Typical range |
|-----------|-------------|---------------|
| ε | Spectral L2 distance threshold for neighbourhood | 0.01–0.10 |
| MinPts | Minimum points to form a dense region | 3–10 |

- Pixels that do not belong to any dense region are labelled as **noise** (class −1)
- Automatically determines cluster count — no k to specify
- Robust to irregular cluster shapes

> **Tip:** Start with ε = 0.05 and MinPts = 5.  Decrease ε to find finer spectral
> classes; increase MinPts to reduce noise sensitivity.

### SAM (Spectral Angle Mapper)

Classifies each pixel by computing the spectral angle between its radiance vector and
each reference endmember spectrum.  The pixel is assigned to the endmember with the
smallest angle, provided the angle is below the threshold.

$$\alpha = \cos^{-1}\!\left(\frac{\mathbf{t} \cdot \mathbf{r}}{|\mathbf{t}||\mathbf{r}|}\right)$$

where **t** is the pixel spectrum and **r** is the reference endmember.

| Parameter | Default | Description |
|-----------|---------|-------------|
| Angle threshold (rad) | 0.10 | ≈ 5.7°; pixels with α > threshold are unclassified |

- Scale-invariant: insensitive to illumination magnitude differences
- Sensitive to shape differences (absorption band positions)
- Requires user-provided reference spectra (see [Section 9](#9-segmentation-tab))

---

## 15. BRDF Model Reference

All BRDF models are energy-conserving (hemispherical reflectance ≤ 1).

### Lambertian

$f_r(\omega_i, \omega_o) = \frac{\rho}{\pi}$

where ρ is the diffuse albedo.  Scatters incoming radiance equally in all directions.
Appropriate for: matte surfaces, vegetation canopy approximations, snow (low roughness).

### Oren-Nayar (rough diffuse)

Generalisation of Lambert's law for rough surfaces (Oren & Nayar 1994).  Models
microfacet roughness with Gaussian facet distribution.

- **Roughness σ = 0:** reduces to Lambertian
- **Roughness σ = 1:** maximum retroreflective lobe
- Appropriate for: dry soil, regolith, rough concrete

### GGX Microfacet Specular

Walter et al. (2007) GGX (Trowbridge-Reitz) microfacet model with Smith shadowing-masking.

- **Roughness α = 0:** mirror-like specular reflection
- **Roughness α = 1:** broad specular highlight approaching diffuse
- Appropriate for: water surfaces, wet roads, metallic materials

### Hapke Regolith

Hapke (1981) model for granular planetary surfaces with opposition effect.

- Designed for: planetary surfaces, lunar regolith, dry desert soils
- **Roughness parameter** controls macroscopic surface tilt
- Includes single-scattering albedo and opposition surge terms

---

## 16. Troubleshooting

### "Could not find nexim_v1.lut"

**Cause:** The Mode 1 LUT file has not been generated.  
**Fix:** Run `NEXIM.LutGen all` as described in [Section 4](#4-first-time-setup-generating-data-assets).
Mode 2 will be used automatically as a fallback in the meantime.

### "Could not find H2O_0000.ktbl"

**Cause:** The k-table files have not been generated.  
**Fix:** Run `NEXIM.LutGen ktables` (or `NEXIM.LutGen all`).  Mode 2 will fall back to
Rayleigh + aerosol scattering only (no H₂O absorption) if k-tables are absent.

### Mode 2 runs very slowly (> 30 s per simulation)

**Cause:** Large spectral grid (many bands) combined with many atmospheric layers.  
**Fix:**
- Increase the wavelength step to 0.02–0.05 µm for exploration runs.
- Reduce scene size (rows/columns) — Mode 2 runs once per scene, not per pixel.
  Scene size primarily affects the sensor simulation phase (Phase 2), not Mode 2.

### Mode 3 simulation produces NaN radiance values

**Cause:** Monte Carlo statistical convergence issue, or a geometry configuration with
solar zenith > 85° (grazing illumination).  
**Fix:**
- Ensure solar zenith ≤ 80°.
- Check that the atmosphere panel wavelength range matches the Mode 3 settings.

### Export produces a 0-byte file

**Cause:** No simulation has been run since the last application launch.  
**Fix:** Click **Run Simulation** first, then **Export Results**.

### ENVI file not readable by external software

**Cause:** Some ENVI readers expect `.hdr` extension on the header and `.img` on the data.  
**Fix:** When saving, choose a base name without extension — NEXIM appends `.img` and `.hdr`
automatically.  If a `.hdr` was not created, ensure the chosen save path is writable.

### GPU not detected in Mode 3

**Cause:** ILGPU requires CUDA Toolkit 11+ (NVIDIA) or OpenCL 2.0 runtime (AMD/Intel).  
**Fix:** Install the appropriate driver/runtime.  Mode 3 runs correctly on CPU
multi-threading regardless — the GPU only accelerates the Monte Carlo inner loop.

### VS 2022 does not open NEXIM.slnx

**Cause:** VS 2022 version < 17.8 does not support `.slnx` format.  
**Fix:** Update Visual Studio 2022 to version 17.8 or later via **Help → Check for Updates**.

---

## 17. Academic References

1. **Stamnes, K., Tsay, S.-C., Wiscombe, W., Jayaweera, K.** (1988).
   Numerically stable algorithm for discrete-ordinate-method radiative transfer in
   multiple scattering and emitting layered media.
   *Appl. Opt.* 27(12):2502–2509. doi:[10.1364/AO.27.002502](https://doi.org/10.1364/AO.27.002502)

2. **Lacis, A. A. & Oinas, V.** (1991).
   A description of the correlated k distribution method for modeling nongray gaseous
   absorption, thermal emission, and multiple scattering in vertically inhomogeneous
   atmospheres.
   *J. Geophys. Res.* 96(D5):9027–9063. doi:[10.1029/90JD01945](https://doi.org/10.1029/90JD01945)

3. **Fu, Q. & Liou, K. N.** (1992).
   On the correlated k-distribution method for radiative transfer in nonhomogeneous
   atmospheres.
   *J. Atmos. Sci.* 49(10):1072–1091. doi:[10.1175/1520-0469(1992)049<1072:OTCMFI>2.0.CO;2](https://doi.org/10.1175/1520-0469(1992)049<1072:OTCMFI>2.0.CO;2)

4. **Mlawer, E. J., Taubman, S. J., Brown, P. D., Iacono, M. J., Clough, S. A.** (1997).
   Radiative transfer for inhomogeneous atmospheres: RRTM, a validated correlated-k
   model for the longwave.
   *J. Geophys. Res.* 102(D14):16663–16682. doi:[10.1029/97JD00237](https://doi.org/10.1029/97JD00237)

5. **Gordon, I. E., et al.** (2022).
   The HITRAN2020 molecular spectroscopic database.
   *J. Quant. Spectrosc. Radiat. Transfer* 277:107949. doi:[10.1016/j.jqsrt.2021.107949](https://doi.org/10.1016/j.jqsrt.2021.107949)

6. **Anderson, G. P., et al.** (1986).
   AFGL atmospheric constituent profiles (0–120 km).
   AFGL-TR-86-0110. Air Force Geophysics Laboratory, DTIC ADA175173.

7. **Clough, S. A., Kneizys, F. X., Davies, R. W.** (1989).
   Line shape and the water vapor continuum.
   *Atmos. Res.* 23(3–4):229–241. doi:[10.1016/0169-8095(89)90020-3](https://doi.org/10.1016/0169-8095(89)90020-3)

8. **Bohren, C. F. & Huffman, D. R.** (1983).
   *Absorption and Scattering of Light by Small Particles.*
   Wiley-Interscience. ISBN 978-0-471-29340-8.

9. **Oren, M. & Nayar, S. K.** (1994).
   Generalization of Lambert's reflectance model.
   *Proc. SIGGRAPH* 94:239–246. doi:[10.1145/192161.192213](https://doi.org/10.1145/192161.192213)

10. **Walter, B., Marschner, S. R., Li, H., Torrance, K. E.** (2007).
    Microfacet models for refraction through rough surfaces.
    *Proc. EGSR* 2007:195–206. doi:[10.2312/EGSR/EGSR07/195-206](https://doi.org/10.2312/EGSR/EGSR07/195-206)

11. **Hapke, B.** (1981).
    Bidirectional reflectance spectroscopy: 1. Theory.
    *J. Geophys. Res.* 86(B4):3039–3054. doi:[10.1029/JB086iB04p03039](https://doi.org/10.1029/JB086iB04p03039)

12. **Kruse, F. A., et al.** (1993).
    The spectral image processing system (SIPS) — interactive visualisation and
    analysis of imaging spectrometer data.
    *Remote Sensing Environ.* 44(2–3):145–163. doi:[10.1016/0034-4257(93)90013-N](https://doi.org/10.1016/0034-4257(93)90013-N)

13. **Tanré, D., Herman, M., Deschamps, P. Y., de Leffe, A.** (1981).
    Atmospheric modeling for space measurements of ground reflectances, including
    bidirectional properties.
    *Appl. Opt.* 20(20):3676–3684. doi:[10.1364/AO.20.003676](https://doi.org/10.1364/AO.20.003676)

14. **Moorhead, I. R., et al.** (2001).
    CAMEO-SIM: a physics-based broadband scene simulation tool for assessment of
    camouflage, concealment, and deception methodologies.
    *Opt. Eng.* 40(9):1750–1759. doi:[10.1117/1.1386798](https://doi.org/10.1117/1.1386798)

15. **Zahidi, U. A., Yuen, P. W. T., Piper, J., Lewis, A.** (2019).
    Evaluation of hyperspectral imaging system performance — a simulation approach.
    *Remote Sensing* 12(1):74. doi:[10.3390/rs12010074](https://doi.org/10.3390/rs12010074)

---

*NEXIM v1.0 · Principal developer: M. Güneş Köyük · Repository: https://github.com/mguneskou/NEXIM*
