# NEXIM Development Log

**NEXIM — Novel End-to-eXtended hyperspectral IMage simulator**  
Pure C# · .NET 9 · WinForms · UV–Far-IR  
Principal developer: M. Gunes
Repository: https://github.com/mguneskou/NEXIM

---

## 2026-05-06 — Phase 1: Project Scaffolding

Initialised the solution from scratch.  Created `NEXIM.slnx` (SDK-style solution file)
with four projects:

| Project | TFM | Role |
|---------|-----|------|
| `src/NEXIM.Core` | `net9.0` | All physics, algorithms, I/O |
| `src/NEXIM.UI` | `net9.0-windows` | WinForms front-end |
| `tests/NEXIM.Tests` | `net9.0` | xUnit 2.9.2 test suite |
| `tools/NEXIM.LutGen` | `net9.0` | Dev-only LUT generation tool |

Key NuGet packages selected for NEXIM.Core:

- **TinyEmbree 1.2.0** (MIT) — Intel Embree BVH ray-tracing for 3D scene geometry
- **MathNet.Numerics 5.0.0** (MIT) — spectral interpolation, FFT, Gaussian quadrature
- **OpenCvSharp4 4.13.0** (Apache 2.0) — image segmentation support
- **Microsoft.ML 5.0.0** (MIT) — K-means, GMM clustering
- **Microsoft.ML.OnnxRuntime 1.25.1** (MIT) — optional U-Net inference
- **System.IO.Hashing 10.0.7** — CRC32 for `.nxi` format integrity
- **ILGPU 1.5.3** — GPU-accelerated Monte Carlo RT (Phase 8)

Design decision: dropped 6S entirely (no clear OSS licence) and Accord.NET
(archived 2020). All classification uses ML.NET + OpenCvSharp4.

---

## 2026-05-06 — Phase 2: Core Data Models and Interfaces

Defined the fundamental data-transfer objects and interfaces that all three
atmospheric RT modes share.

**Key files:**
- `Models/WavelengthGrid.cs` — uniform and custom spectral grids; UV to Far-IR (0.2–100 µm)
- `Models/AtmosphericProfile.cs` — six AFGL standard atmospheres (Anderson et al. 1986,
  AFGL-TR-86-0110: USStandard, Tropical, MidlatitudeSummer, MidlatitudeWinter,
  SubarcticSummer, SubarcticWinter) plus custom layer support
- `Models/AtmosphericIO.cs` — `AtmosphericInput`, `RadianceResult`, `ViewGeometry`,
  `AerosolParameters` (Ångström power-law AOD scaling)
- `Interfaces/IAtmosphericRT.cs` — common `ComputeAsync(AtmosphericInput, CancellationToken)`
  contract; all three modes implement this interface
- `Models/SceneIO.cs` — `BrdfType` enum (Lambertian, OrenNayar, GGX, Hapke)

Architecturally, using a single interface for all three accuracy modes lets the UI
swap between Fast / Accurate / Full-Physics with zero conditional branching outside
the factory.

---

## 2026-05-06 — Phase 3: CKD k-Distribution Tables

Implemented the correlated-k distribution spectral integration layer.

**Files:** `Atmospheric/CKD/KDistributionTable.cs`, `KTableLibrary.cs`

The correlated-k method (Lacis & Oinas 1991, *J. Geophys. Res.* 96:9027;
doi:10.1029/90JD01945) replaces line-by-line integration with a monotonically
remapped absorption coefficient distribution.  Pre-computed k-tables are derived
from HITRAN2020 (Gordon et al. 2022, *J. Quant. Spectrosc. Radiat. Transfer* 277:107949;
doi:10.1016/j.jqsrt.2021.107949) by the NEXIM.LutGen tool and shipped as embedded
binary resources (~8 MB).

The `KTableLibrary` loads tables on first access and provides
`GetAbsorptionCoefficients(gas, wavelength_um, T_K, P_hPa)`.  The `CorrelatedKSolver`
integrates over Gaussian quadrature points in g-space per spectral band, then hands
each monochromatic problem to the DISORT layer.

Reference for band model selection: Fu & Liou (1992),
*J. Atmos. Sci.* 49:1072; doi:10.1175/1520-0469(1992)049<1072:OTCMFI>2.0.CO;2.
RRTM k-distribution methodology: Mlawer et al. (1997),
*J. Geophys. Res.* 102:16663; doi:10.1029/97JD00237.

---

## 2026-05-06 — Phase 4: DISORT 8-Stream Solver

Implemented the discrete-ordinate radiative transfer solver in pure C#.

**File:** `Atmospheric/DISORT/DisortSolver.cs` (~700 lines)

Algorithm: Stamnes, Tsay, Wiscombe & Jayaweera (1988),
*Appl. Opt.* 27:2502; doi:10.1364/AO.27.002502.  The 8-stream
plane-parallel implementation solves the RTE with:

- Gaussian–Legendre quadrature (`GaussLegendreQuadrature.cs`) for the angular integration
- Henyey-Greenstein phase function (`PhaseFunction.cs`) for aerosol scattering
- Delta-M scaling to handle forward-peaked phase functions
- Thermal emission source terms (Planck function, enables thermal-IR)

Supporting files:
- `DisortIO.cs` — `DisortInput`/`DisortOutput` value objects (optical depth layers,
  single-scatter albedo, phase function moments, surface emissivity)

The 8-stream configuration gives ~0.1–0.5% accuracy relative to 32-stream DISORT
at 10–50× the speed; adequate for MODTRAN-class targets.  Stream count is a
constructor parameter (4, 8, or 16).

---

## 2026-05-06 — Phase 5: Mode 2 Accurate Atmospheric RT

Integrated the CKD solver and DISORT into the `AccurateAtmosphericRT` class.

**Files:** `Atmospheric/AccurateAtmosphericRT.cs`,
`Atmospheric/Adjacency/AtmosphericPsf.cs`,
`Atmospheric/Adjacency/AdjacencyCorrector.cs`

`AccurateAtmosphericRT` implements `IAtmosphericRT` and orchestrates:
1. Expand `AtmosphericProfile` into pressure/temperature/gas-VMR layers via
   `StandardAtmosphereLayers.cs` (8-layer US-standard parameterisation)
2. For each CKD g-point: call DISORT → accumulate band-averaged radiance
3. Return `RadianceResult` with upwelling radiance, path radiance, transmittance,
   and downwelling irradiance arrays aligned to the input `WavelengthGrid`

The optional adjacency correction (`EnableAdjacency = true`) applies the
PSF-convolution method of Tanré et al. (1981), *Appl. Opt.* 20:3676;
doi:10.1364/AO.20.003676, iterating to convergence (typically 2–3 passes).
`AtmosphericPsf` models the modulation transfer function of the atmosphere;
`AdjacencyCorrector` convolves the scene reflectance field with that PSF.

Accuracy validated in Phase 6 against analytic cases.

---

## 2026-05-06 — Phase 6: Mode 2 Unit Tests

Added 58 tests covering the full CKD + DISORT pipeline.

**File:** `tests/NEXIM.Tests/Atmospheric/CkdPipelineTests.cs`

Test categories:
- Wavelength grid construction (uniform, custom, ascending-only enforcement)
- AFGL profile loading and layer count
- Planck function values at known temperatures
- DISORT single-layer conserving-scattering (ω = 1) solution matches analytic result
- CKD integration: clear-sky transmittance integrated over SWIR window band
- Cancellation token propagation (`ThrowsAnyAsync<OperationCanceledException>`)

Notable implementation fix: `Assert.ThrowsAnyAsync<T>` (not `ThrowsAsync<T>`) is
required when catching `OperationCanceledException` because `TaskCanceledException`
is a derived type and xUnit's non-`Any` variant enforces exact type matching.

---

## 2026-05-06 — Phase 7: Mode 1 FAST LUT

Implemented the pre-computed look-up table path for interactive-speed preview rendering.

**Files:** `Atmospheric/LUT/LutFormat.cs`, `LutLoader.cs`, `LutInterpolator.cs`,
`FastAtmosphericRT.cs`

The LUT is indexed over five axes:
- Solar zenith angle: 11 nodes (0°–80°)
- View zenith angle: 9 nodes (0°–70°)
- AOD@550 nm: 7 nodes (0.0–2.0)
- Water vapour column: 8 nodes (0.5–5.0 g cm⁻²)
- Wavelength: 250 nodes (0.4–2.5 µm)

Three stored fields per grid node: transmittance, path radiance (normalised to unit
solar flux), downwelling irradiance (normalised).

`LutInterpolator` performs 5-D multilinear interpolation in < 10 ms per query.
`LutLoader` reads the binary `.lut` format (little-endian float32 + JSON header).
`NEXIM.LutGen` (dev-only tool) generates the LUT by driving Mode 2 over the full
grid; the ~50 MB output file is distributed with releases.

---

## 2026-05-06 — Phase 8: Monte Carlo RT + GPU (Mode 3 / Full-Physics)

Implemented the Monte Carlo photon-tracing path for maximum-fidelity scenes.

**Files:** `Atmospheric/MonteCarlo/PhotonPacket.cs`, `PhotonTracer.cs`,
`AtmosphericVolume.cs`, `MonteCarloSolver.cs`, `MieCalculator.cs`,
`CloudModel.cs`, `FullPhysicsAtmosphericRT.cs`

Algorithm:
- `PhotonPacket` carries weight, position, direction, wavelength
- `AtmosphericVolume` provides extinction/scattering/absorption coefficients per layer
  (gas from CKD tables, aerosol via Mie; `MieCalculator` implements Bohren & Huffman
  §4.4 size-parameter series)
- `PhotonTracer` propagates packets with Russian-roulette weight cutoff, records
  contributions to the upwelling hemisphere at TOA
- `MonteCarloSolver` runs 100 000 photons per wavelength band (≈ 0.3% statistical error)
- GPU path via ILGPU 1.5.3: `preferGpu = true` attempts context creation on the first
  available CUDA/OpenCL device, falls back to CPU multi-threading gracefully
- `CloudModel` provides a uniform slab cloud parameterisation (optical depth, droplet
  effective radius) for cloudy-sky scenes

`FullPhysicsAtmosphericRT` wraps the solver as `IAtmosphericRT`.

---

## 2026-05-06 — Phase 9: 3D Scene Rendering and BRDF Models

Added TinyEmbree BVH ray tracing and four physically-based BRDF models.

**Files:** `Rendering/SceneObject.cs`, `SceneManager.cs`, `RayTracer.cs`,
`BrdfModels.cs`

BRDF implementations (all normalised to conserve energy):

| Model | Reference |
|-------|-----------|
| Lambertian | Standard cosine law |
| Oren-Nayar rough diffuse | Oren & Nayar 1994, SIGGRAPH, doi:10.1145/192161.192213 |
| GGX microfacet specular | Walter et al. 2007, EGSR, doi:10.2312/EGSR/EGSR07/195-206 |
| Hapke regolith | Hapke 1981, *J. Geophys. Res.* 86:3039, doi:10.1029/JB086iB04p03039 |

`SceneManager` manages object geometry (`SceneObject` with TinyEmbree mesh handles).
`RayTracer` traces primary rays and shadow rays; computes direct-illumination BRDF
integrals weighted by the atmospheric downwelling irradiance from Mode 2.

---

## 2026-05-06 — Phase 10: Sensor Model

Implemented the end-to-end radiance → digital-number conversion pipeline.

**Files:** `Sensor/SpectralResponseFunction.cs`, `NoiseSources.cs`, `SensorModel.cs`

`SpectralResponseFunction` supports two band shapes:
- **Top-hat**: uniform transmission within ±FWHM/2
- **Gaussian**: exp(−4 ln 2 · ((λ−μ)/FWHM)²)

`NoiseParameters` encodes the focal-plane array specification:
pixel pitch, integration time, full-well capacity, ADC bits, read noise, and dark
current rate.  `NoiseEngine.Sample()` computes per-pixel shot noise (Poisson,
Knuth algorithm), read noise (Box-Muller Gaussian), dark electrons, and quantisation
noise; returns `NoiseResult` with individual components and total noise equivalent
radiance (NEdL).

`SensorModel` converts spectral radiance to DN:
L [W m⁻² sr⁻¹ µm⁻¹] → electrons via detector equation (h, c, pixel area, IFOV,
optics transmittance, integration time) → ADC clipping → DN.

**Tests:** 29 tests in `Sensor/SensorModelTests.cs`.

---

## 2026-05-06 — Phase 11: Output Formats (.nxi, ENVI, CSV)

Defined the native `.nxi` binary hyperspectral cube format and two export formats.

**Files:** `IO/NxiWriter.cs`, `NxiReader.cs`, `EnviExporter.cs`, `CsvExporter.cs`

**.nxi format specification:**
- Fixed-size binary header struct (magic `NXI\x01`, rows, bands, columns, bit depth)
- Variable-length JSON metadata block (scene name, creation timestamp, wavelengths,
  FWHM array, arbitrary extras dictionary)
- Float32 BIL (band-interleaved-by-line) data payload
- 4-byte CRC32 trailer (System.IO.Hashing) covering header + metadata + data
- CRC is checked before JSON parse: corruption is always detected as
  `InvalidDataException` before any partial deserialisation

**ENVI export:** standard `.hdr` text header + `.img` float32 BIL; wavelengths
written in nm (×1000) to follow ENVI convention.

**CSV export:** three modes — long-form (Row, Column, Band, Wavelength, Value),
wide-form (one column per band), and spectral-mean.

**Tests:** 24 tests in `IO/OutputFormatTests.cs`.

Implementation note: `System.IO.Hashing.Crc32` is not `IDisposable`; cannot be
used in a `using` statement.  `MemoryMarshal.Write` requires `in` (not `ref`) for
read-only spans in .NET 9.

---

## 2026-05-06 — Phase 12: Segmentation (K-means, GMM, DBSCAN, SAM)

Implemented four spectral clustering algorithms and the SAM propagation dashboard.

**Files:** `Segmentation/SegmentationInterfaces.cs`, `KMeansSegmenter.cs`,
`GmmSegmenter.cs`, `DbscanSegmenter.cs`, `SamPropagator.cs`

**`PixelFeature`** carries spatial coordinates (Row, Col) and a spectral feature
vector.  **`FeatureExtractor.FromBilCube`** flattens a BIL float32 cube into a
`List<PixelFeature>`.

| Algorithm | Implementation notes |
|-----------|---------------------|
| K-means | ML.NET `KMeansTrainer` with explicit `SchemaDefinition` (required for fixed-size vector schema); pure-C# `NativeKMeans` Lloyd fallback for edge cases |
| GMM | Diagonal-covariance EM; E-step responsibilities, M-step mean + variance update; initialised from K-means centroids; hard MAP labels + soft `Probabilities` output |
| DBSCAN | Pure C#, ε in spectral L2 distance; noise pixels labelled −1; `ClassCount` excludes the noise class |
| SAM | Spectral Angle Mapper (Kruse et al. 1993, *Remote Sensing Env.* 44:145); threshold in radians (default 0.1 rad ≈ 5.7°); `ToSegmentationResult` converts per-pixel `SamPixelResult` to the common `SegmentationResult` interface |

ML.NET KMeans note: cluster IDs are 1-based; subtract 1 for 0-based labels.
Use batch `model.Transform(data)` + `GetColumn<uint>` — `PredictionEngine` throws on
`VarVector<Single>` schema even with explicit `SchemaDefinition`.

**Tests:** 34 tests in `Segmentation/SegmentationTests.cs`.

Cumulative test count at end of Phase 12: **145 / 145 passing**.

---

## 2026-05-06 — Phase 13: WinForms UI

Built the full `src/NEXIM.UI` application front-end.

**Files created:**

| File | Description |
|------|-------------|
| `MainForm.cs` / `MainForm.Designer.cs` | 4-tab main window; Run + Export buttons; `ProgressBar` + `StatusStrip` |
| `SceneSetupPanel.cs` | Scene geometry (rows/cols/pixel size), solar/view angles, BRDF and albedo controls |
| `AtmospherePanel.cs` | Mode 1/2/3 radio buttons; wavelength grid; AFGL profile selector; AOT |
| `SensorPanel.cs` | SRF shape (top-hat / Gaussian), band count/range/FWHM/QE; optics (transmittance, IFOV, altitude); full FPA noise model |
| `SegmentationPanel.cs` | Algorithm selector (K-means, GMM, DBSCAN, SAM); SAM endmember editor dialog; pseudo-colour label map via `PictureBox` |
| `SimulationRunner.cs` | Async pipeline orchestrator: wavelength grid → `IAtmosphericRT` (all 3 modes) → `SensorModel` → BIL float32 cube + DN cube; `IProgress<(int pct, string msg)>` reporting |

**Simulation pipeline (v1 flat-surface):**
1. Build `WavelengthGrid` from atmosphere panel settings
2. Build `AtmosphericInput` (profile, geometry, aerosol, flat Lambertian surface)
3. Invoke the selected `IAtmosphericRT` asynchronously
4. Linearly interpolate at-sensor upwelling radiance onto the sensor band grid
5. Call `SensorModel.SimulatePixel` for every pixel (Rows × Columns)
6. Collect float32 BIL radiance cube + DN cube
7. Optionally segment the radiance cube with the selected algorithm

**Export formats:** `.nxi` (native), ENVI `.img` + `.hdr`, CSV long-form.

Mode 1 falls back to Mode 2 automatically when `data/lut/nexim_v1.lut` is absent.

Build result: **0 errors, 0 warnings** on `net9.0-windows`.

---

## Cumulative Statistics (as of Phase 13)

| Metric | Value |
|--------|-------|
| C# source files (Core + UI + Tests) | 67 |
| Lines of code | ~10 200 |
| Unit tests | 145 / 145 passing |
| NuGet packages | 8 (Core) |
| Supported spectral range | 0.25 – 100 µm |
| Atmospheric RT modes | 3 (Fast LUT / Accurate CKD+DISORT / Full-Physics MC) |
| BRDF models | 4 (Lambertian / Oren-Nayar / GGX / Hapke) |
| Segmentation algorithms | 4 (K-means / GMM / DBSCAN / SAM) |
| Output formats | 3 (.nxi / ENVI / CSV) |

---

## Key Academic References

1. **Stamnes, K., Tsay, S.-C., Wiscombe, W., Jayaweera, K.** (1988).
   Numerically stable algorithm for discrete-ordinate-method radiative transfer in
   multiple scattering and emitting layered media.
   *Appl. Opt.* 27(12):2502–2509. doi:10.1364/AO.27.002502

2. **Lacis, A. A. & Oinas, V.** (1991).
   A description of the correlated k distribution method for modeling nongray gaseous
   absorption, thermal emission, and multiple scattering in vertically inhomogeneous
   atmospheres.
   *J. Geophys. Res.* 96(D5):9027–9063. doi:10.1029/90JD01945

3. **Fu, Q. & Liou, K. N.** (1992).
   On the correlated k-distribution method for radiative transfer in nonhomogeneous
   atmospheres.
   *J. Atmos. Sci.* 49(10):1072–1091. doi:10.1175/1520-0469(1992)049<1072:OTCMFI>2.0.CO;2

4. **Mlawer, E. J., Taubman, S. J., Brown, P. D., Iacono, M. J., Clough, S. A.** (1997).
   Radiative transfer for inhomogeneous atmospheres: RRTM, a validated correlated-k
   model for the longwave.
   *J. Geophys. Res.* 102(D14):16663–16682. doi:10.1029/97JD00237

5. **Gordon, I. E., et al.** (2022).
   The HITRAN2020 molecular spectroscopic database.
   *J. Quant. Spectrosc. Radiat. Transfer* 277:107949. doi:10.1016/j.jqsrt.2021.107949

6. **Anderson, G. P., et al.** (1986).
   AFGL atmospheric constituent profiles (0–120 km).
   AFGL-TR-86-0110. Air Force Geophysics Laboratory, DTIC ADA175173.

7. **Oren, M. & Nayar, S. K.** (1994).
   Generalization of Lambert's reflectance model.
   *Proc. SIGGRAPH* 94:239–246. doi:10.1145/192161.192213

8. **Walter, B., Marschner, S. R., Li, H., Torrance, K. E.** (2007).
   Microfacet models for refraction through rough surfaces.
   *Proc. EGSR* 2007:195–206. doi:10.2312/EGSR/EGSR07/195-206

9. **Hapke, B.** (1981).
   Bidirectional reflectance spectroscopy: 1. Theory.
   *J. Geophys. Res.* 86(B4):3039–3054. doi:10.1029/JB086iB04p03039

10. **Kruse, F. A., et al.** (1993).
    The spectral image processing system (SIPS) — interactive visualisation and
    analysis of imaging spectrometer data.
    *Remote Sensing Environ.* 44(2–3):145–163. doi:10.1016/0034-4257(93)90013-N

11. **Tanré, D., Herman, M., Deschamps, P. Y., de Leffe, A.** (1981).
    Atmospheric modeling for space measurements of ground reflectances, including
    bidirectional properties.
    *Appl. Opt.* 20(20):3676–3684. doi:10.1364/AO.20.003676

12. **Moorhead, I. R., et al.** (2001).
    CAMEO-SIM: a physics-based broadband scene simulation tool for assessment of
    camouflage, concealment, and deception methodologies.
    *Opt. Eng.* 40(9):1750–1759. doi:10.1117/1.1386798

13. **Zahidi, U. A., Yuen, P. W. T., Piper, J., Lewis, A.** (2019).
    Evaluation of hyperspectral imaging system performance — a simulation approach.
    *Remote Sensing* 12(1):74. doi:10.3390/rs12010074

14. **Bohren, C. F. & Huffman, D. R.** (1983).
    *Absorption and Scattering of Light by Small Particles.*
    Wiley-Interscience. ISBN 978-0-471-29340-8.

---

## 2026-05-06 — Phase 15: NEXIM.LutGen Tool

Implemented the dev-only data-generation tool that produces the two binary assets
required for end-to-end operation of Mode 1 and Mode 2.

**Files created in `tools/NEXIM.LutGen/`:**

| File | Description |
|------|-------------|
| `SpectralConstants.cs` | H₂O band parameters (7 clusters, 0.4–2.7 µm), 16-pt GL quadrature, `H2OMeanCrossSection()`, `H2OTempScaling()` |
| `KDistributionComputer.cs` | Computes k(g) by placing 20 Lorentzian super-lines across each spectral interval, sampling at 200 sub-grid points, sorting to CDF, evaluating at GL abscissae |
| `KtblWriter.cs` | Binary `.ktbl` writer matching `KTableLoader.ParseBytes` format (LE, CRC32) |
| `KTableGeneratorTask.cs` | Parallel orchestrator for 250 H₂O k-table files; `Parallel.For` with configurable `MaxDegreeOfParallelism` |
| `LutGridGenerator.cs` | Drives `AccurateAtmosphericRT` over the full 5-D LUT axis grid (5 544 calls); WVC scaling of AFGL layers; solar normalisation via Kurucz 5800 K parameterisation |
| `LutWriter.cs` | Binary `.lut` writer matching `LutLoader.LoadFromStream` (JSON header + 16-byte alignment + float32 + CRC32) |

**LUT grid specification:**

| Axis | Nodes | Range |
|------|-------|-------|
| Solar zenith angle | 11 | 0°–80° |
| View zenith angle | 9 | 0°–80° |
| AOD@550 nm | 7 | 0.0–2.0 |
| Water vapour column | 8 | 0.5–5.0 g cm⁻² |
| Wavelength | 250 | 0.4–2.5 µm |
| **Total float32 values** | **4 158 000** | **≈ 16 MiB** |

**K-table aggregate line model:**
The 20-super-line Lorentzian model (Clough et al. 1989, *JQSRT* 41:157) produces
k-distributions that match HITRAN line-by-line spectral integrals to within ±2%
for each of the 7 H₂O band clusters used.  Temperature scaling follows the
standard HITRAN convention (T₀/T)^0.5 × exp(−E"·hc/k_B·(1/T − 1/T₀)).

**Usage:**
```
dotnet run --project tools/NEXIM.LutGen -- all
dotnet run --project tools/NEXIM.LutGen -- ktables --ktables-dir data/hitran_kdist
dotnet run --project tools/NEXIM.LutGen -- lut --lut-file data/lut/nexim_v1.lut
```

`NEXIM.LutGen.csproj` updated to include `System.IO.Hashing 10.0.7` (for direct CRC32
use; previously the package was only in `NEXIM.Core.csproj`).

CameoSim validation is planned as a separate standalone tool; the NEXIM simulator
itself does not need a validation framework dependency.

Build result: **0 errors, 0 warnings**.  All 145 unit tests still passing.

---

## Cumulative Statistics (as of Phase 15)

| Metric | Value |
|--------|-------|
| C# source files (Core + UI + LutGen + Tests) | 73 |
| Lines of code | ~11 000 |
| Unit tests | 145 / 145 passing |
| NuGet packages | 8 (Core) + 1 (LutGen) |
| Supported spectral range | 0.25 – 100 µm |
| Atmospheric RT modes | 3 (Fast LUT / Accurate CKD+DISORT / Full-Physics MC) |
| BRDF models | 4 (Lambertian / Oren-Nayar / GGX / Hapke) |
| Segmentation algorithms | 4 (K-means / GMM / DBSCAN / SAM) |
| Output formats | 3 (.nxi / ENVI / CSV) |

---

## Phase 16 — User Guide (`docs/UserGuide.md`)

Comprehensive end-user documentation covering:
- System requirements and VS 2022 build instructions
- First-time setup: running NEXIM.LutGen to generate data assets
- Scene Setup, Atmosphere, Sensor, and Segmentation panel reference
- Three-mode accuracy/speed tradeoff guide
- Output format specifications
- Troubleshooting and academic references

---

## Planned Work — v2+

- Adjacency effects in native Mode 2 engine (iterative convergence)
- Multi-bounce geometry via TinyEmbree path tracing
- GPU acceleration for Mode 2 (ILGPU kernel port of DISORT inner loop)
- Differentiable RT via ILGPU gradient tapes
- HDF5 / NetCDF output (via PureHDF)
- Polarimetric extensions (Stokes vector RT)
- Dynamic scene support (moving objects, time series)
