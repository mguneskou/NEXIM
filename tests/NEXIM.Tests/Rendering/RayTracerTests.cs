// NEXIM — Phase 9 unit tests: BRDF models, SceneManager, and RayTracer.

using System.Numerics;
using TinyEmbree;
using NEXIM.Core.Models;
using NEXIM.Core.Rendering;

namespace NEXIM.Tests.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// BRDF model tests (pure math, no external dependencies)
// ─────────────────────────────────────────────────────────────────────────────

public class BrdfModelTests
{
    // ── Lambertian ─────────────────────────────────────────────────────────

    [Fact]
    public void Lambertian_ValueIsAlbedoOverPi()
    {
        var brdf     = new LambertianBrdf(0.5);
        double value = brdf.Evaluate(0.7, 0.6, 0.3, 0.55);
        Assert.Equal(0.5 / Math.PI, value, precision: 12);
    }

    [Fact]
    public void Lambertian_ViewAngleIndependent()
    {
        var brdf = new LambertianBrdf(0.8);
        double v1 = brdf.Evaluate(0.9, 0.1, -0.5, 1.0);
        double v2 = brdf.Evaluate(0.1, 0.9,  0.5, 1.0);
        Assert.Equal(v1, v2, precision: 14);
    }

    [Fact]
    public void Lambertian_Normalization_ReflectanceEqualsAlbedo()
    {
        // ∫ f_r cosO dω = albedo (Monte Carlo, 2-sample uniform hemisphere)
        const double albedo = 0.6;
        var    brdf  = new LambertianBrdf(albedo);
        var    rng   = new Random(1);
        double sum   = 0.0;
        const  int N = 50_000;

        for (int i = 0; i < N; i++)
        {
            double phi  = rng.NextDouble() * 2 * Math.PI;
            double cosO = rng.NextDouble();
            sum += brdf.Evaluate(0.7, cosO, Math.Cos(phi), 0.55) * cosO;
        }

        double estimate = 2 * Math.PI * sum / N;  // 2π for phi integral, 1/N for cosO
        Assert.InRange(estimate, albedo * 0.98, albedo * 1.02);
    }

    // ── Oren-Nayar ─────────────────────────────────────────────────────────

    [Fact]
    public void OrenNayar_ZeroSigma_EqualsLambertian()
    {
        var lamb = new LambertianBrdf(0.6);
        var on   = new OrenNayarBrdf(0.6, 0.0);

        foreach (var (ci, co, dp) in TestAngles())
        {
            double expected = lamb.Evaluate(ci, co, dp, 0.55);
            double actual   = on.Evaluate(ci, co, dp, 0.55);
            Assert.Equal(expected, actual, precision: 10);
        }
    }

    [Fact]
    public void OrenNayar_NonNegativeForAllAngles()
    {
        var on  = new OrenNayarBrdf(0.7, 0.5);
        var rng = new Random(42);

        for (int i = 0; i < 2000; i++)
        {
            double cosI = rng.NextDouble();
            double cosO = rng.NextDouble();
            double dp   = 2 * rng.NextDouble() - 1;
            Assert.True(on.Evaluate(cosI, cosO, dp, 0.55) >= 0,
                $"Negative BRDF at cosI={cosI:F3} cosO={cosO:F3} dp={dp:F3}");
        }
    }

    [Fact]
    public void OrenNayar_HighRoughness_LowerThanLambertianAtHighIncidence()
    {
        // At normal emission (cosO=1) and oblique incidence (cosI=0.3),
        // Oren-Nayar with σ>0 is darker than Lambertian (A < 1).
        var lamb = new LambertianBrdf(0.8);
        var on   = new OrenNayarBrdf(0.8, 0.8);

        double fLamb = lamb.Evaluate(0.3, 1.0, 0.0, 0.55);
        double fOn   = on.Evaluate(0.3, 1.0, 0.0, 0.55);
        Assert.True(fOn < fLamb,
            $"Expected Oren-Nayar ({fOn:F6}) < Lambertian ({fLamb:F6})");
    }

    // ── GGX ────────────────────────────────────────────────────────────────

    [Fact]
    public void Ggx_NonNegativeForAllAngles()
    {
        var ggx = new GgxBrdf(0.3, 0.04);
        var rng = new Random(7);

        for (int i = 0; i < 2000; i++)
        {
            double cosI = rng.NextDouble();
            double cosO = rng.NextDouble();
            double dp   = 2 * rng.NextDouble() - 1;
            double val  = ggx.Evaluate(cosI, cosO, dp, 0.55);
            Assert.True(val >= 0.0,
                $"Negative GGX BRDF at cosI={cosI:F3} cosO={cosO:F3} dp={dp:F3}");
        }
    }

    [Fact]
    public void Ggx_Reciprocal_SwapIoGivesSameValue()
    {
        var    ggx = new GgxBrdf(0.3, 0.04);
        double v1  = ggx.Evaluate(0.7, 0.5,  0.3, 0.55);
        double v2  = ggx.Evaluate(0.5, 0.7,  0.3, 0.55);
        // GGX BRDF is Helmholtz-reciprocal: f_r(i,o) = f_r(o,i)
        Assert.Equal(v1, v2, precision: 10);
    }

    [Fact]
    public void Ggx_GrazingAngles_ReturnsZero()
    {
        var ggx = new GgxBrdf(0.3, 0.04);
        Assert.Equal(0.0, ggx.Evaluate(0.0, 0.7, 0.0, 0.55));
        Assert.Equal(0.0, ggx.Evaluate(0.7, 0.0, 0.0, 0.55));
    }

    // ── Hapke ──────────────────────────────────────────────────────────────

    [Fact]
    public void Hapke_NonNegativeForAllAngles()
    {
        var hapke = new HapkeBrdf(0.5, 0.2, 0.3, 0.06);
        var rng   = new Random(13);

        for (int i = 0; i < 2000; i++)
        {
            double cosI = rng.NextDouble();
            double cosO = rng.NextDouble();
            double dp   = 2 * rng.NextDouble() - 1;
            Assert.True(hapke.Evaluate(cosI, cosO, dp, 0.55) >= 0.0,
                $"Negative Hapke BRDF at cosI={cosI:F3} cosO={cosO:F3} dp={dp:F3}");
        }
    }

    [Fact]
    public void Hapke_IsotropicNoSurge_GrazingReturnsZero()
    {
        var hapke = new HapkeBrdf(0.5);
        Assert.Equal(0.0, hapke.Evaluate(0.0, 0.5, 0.0, 0.55));
        Assert.Equal(0.0, hapke.Evaluate(0.5, 0.0, 0.0, 0.55));
    }

    [Fact]
    public void Hapke_EnergyConserving_ReflectanceLessThanOne()
    {
        // Monte Carlo estimate of ∫ f_r cosO dω should be < 1
        var    hapke = new HapkeBrdf(0.9, 0.0, 0.0, 0.06);
        var    rng   = new Random(17);
        double sum   = 0.0;
        const  int N = 50_000;

        for (int i = 0; i < N; i++)
        {
            double phi  = rng.NextDouble() * 2 * Math.PI;
            double cosO = rng.NextDouble();
            sum += hapke.Evaluate(0.7, cosO, Math.Cos(phi), 0.55) * cosO;
        }

        double reflectance = 2 * Math.PI * sum / N;
        Assert.True(reflectance <= 1.0 + 0.02,
            $"Hapke hemispherical reflectance = {reflectance:F4} exceeds 1");
        Assert.True(reflectance >= 0.0);
    }

    // ── BrdfFactory ─────────────────────────────────────────────────────────

    [Fact]
    public void BrdfFactory_CreateLambertian_ReturnsCorrectType()
    {
        var mat  = new Material { Albedo = new[] { 0.4 }, BrdfType = BrdfType.Lambertian };
        var brdf = BrdfFactory.Create(mat, 0);
        Assert.IsType<LambertianBrdf>(brdf);
    }

    [Fact]
    public void BrdfFactory_CreateHapke_ReturnsHapkeBrdf()
    {
        var mat  = new Material { Albedo = new[] { 0.5 }, BrdfType = BrdfType.Hapke, HapkeW = 0.5 };
        var brdf = BrdfFactory.Create(mat, 0);
        Assert.IsType<HapkeBrdf>(brdf);
    }

    [Fact]
    public void BrdfFactory_PerBandAlbedo_UsesCorrectBand()
    {
        var mat   = new Material { Albedo = new[] { 0.2, 0.8 }, BrdfType = BrdfType.Lambertian };
        var brdf0 = BrdfFactory.Create(mat, 0);
        var brdf1 = BrdfFactory.Create(mat, 1);
        Assert.Equal(0.2 / Math.PI, brdf0.Evaluate(0.5, 0.5, 0.0, 0.55), precision: 12);
        Assert.Equal(0.8 / Math.PI, brdf1.Evaluate(0.5, 0.5, 0.0, 0.55), precision: 12);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static IEnumerable<(double cosI, double cosO, double dp)> TestAngles()
    {
        yield return (1.0, 1.0, 1.0);
        yield return (0.7, 0.7, 0.0);
        yield return (0.5, 0.9, -0.5);
        yield return (0.9, 0.3,  0.8);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SceneManager tests (uses TinyEmbree native BVH)
// ─────────────────────────────────────────────────────────────────────────────

public class SceneManagerTests : IDisposable
{
    static readonly Vector3[] QuadVerts =
    {
        new(-5, -5, 0), new(5, -5, 0), new(5, 5, 0), new(-5, 5, 0),
    };
    static readonly int[] QuadIdx = { 0, 1, 2, 0, 2, 3 };

    static readonly Material DefaultMat = new()
    {
        Id     = 0,
        Albedo = new[] { 0.5 },
    };

    readonly SceneManager _scene = new();

    public void Dispose() => _scene.Dispose();

    [Fact]
    public void AddAndBuild_RayFromAbove_HitsFloor()
    {
        _scene.AddObject(QuadVerts, QuadIdx, DefaultMat);
        _scene.Build();

        var (hit, obj) = _scene.Trace(new Ray
        {
            Origin      = new Vector3(0, 0, 10),
            Direction   = new Vector3(0, 0, -1),
            MinDistance = 0.0f,
        });

        Assert.True(hit, "Ray from above should hit the floor quad");
        Assert.NotNull(obj);
        Assert.Equal(0, obj!.Id);
    }

    [Fact]
    public void RayMisses_ReturnsNullObject()
    {
        _scene.AddObject(QuadVerts, QuadIdx, DefaultMat);
        _scene.Build();

        // Ray aimed far to the side — misses the 10×10 quad
        var (hit, obj) = _scene.Trace(new Ray
        {
            Origin      = new Vector3(100, 100, 10),
            Direction   = new Vector3(0,   0,  -1),
            MinDistance = 0.0f,
        });

        Assert.False(hit);
        Assert.Null(obj);
    }

    [Fact]
    public void HitReturnsCorrectMaterial_ById()
    {
        var mat1 = new Material { Id = 42, Albedo = new[] { 0.3 } };
        _scene.AddObject(QuadVerts, QuadIdx, mat1);
        _scene.Build();

        var (_, obj) = _scene.Trace(new Ray
        {
            Origin    = new Vector3(0, 0, 5),
            Direction = new Vector3(0, 0, -1),
        });

        Assert.NotNull(obj);
        Assert.Equal(42, obj!.Material.Id);
    }

    [Fact]
    public void OcclusionTest_WallBlocksHorizontalShadowRay()
    {
        // Floor at z=0
        _scene.AddObject(QuadVerts, QuadIdx, DefaultMat);
        // Vertical wall at x=2, winding gives face normal pointing (-1,0,0)
        // — faces toward the shadow ray origin, ensuring a front-face hit.
        Vector3[] wallVerts =
        {
            new(2, -2, -2), new(2, -2, 2), new(2, 2, 2), new(2, 2, -2),
        };
        _scene.AddObject(wallVerts, QuadIdx, DefaultMat);
        _scene.Build();

        // Hit the floor
        var (hit, _) = _scene.Trace(new Ray
        {
            Origin    = new Vector3(0, 0, 5),
            Direction = new Vector3(0, 0, -1),
        });
        Assert.True(hit);

        // Horizontal shadow ray toward +x — wall at x=2 blocks it
        bool blocked = _scene.IsOccluded(hit, new Vector3(1, 0, 0));
        Assert.True(blocked, "Shadow ray should be blocked by vertical wall at x=2");
    }

    [Fact]
    public void OcclusionTest_NoOccluder_RayLeavesScene()
    {
        _scene.AddObject(QuadVerts, QuadIdx, DefaultMat);
        _scene.Build();

        var (hit, _) = _scene.Trace(new Ray
        {
            Origin    = new Vector3(0, 0, 10),
            Direction = new Vector3(0, 0, -1),
        });

        Assert.True(hit);

        // Shadow ray straight up — only the floor, nothing above → not occluded
        bool blocked = _scene.IsOccluded(hit, new Vector3(0, 0, 1));
        Assert.False(blocked, "No occluder above — shadow ray should leave scene");
    }

    [Fact]
    public void AddObjectAfterBuild_Throws()
    {
        _scene.Build();
        Assert.Throws<InvalidOperationException>(() =>
            _scene.AddObject(QuadVerts, QuadIdx, DefaultMat));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// RayTracer tests
// ─────────────────────────────────────────────────────────────────────────────

public class RayTracerTests : IDisposable
{
    static readonly Vector3[] QuadVerts =
    {
        new(-10, -10, 0), new(10, -10, 0), new(10, 10, 0), new(-10, 10, 0),
    };
    static readonly int[] QuadIdx = { 0, 1, 2, 0, 2, 3 };

    static readonly double[] Wavelengths = { 0.45, 0.55, 0.65 };

    readonly SceneManager _scene = new();
    readonly RayTracer    _tracer;

    public RayTracerTests()
    {
        _scene.AddObject(QuadVerts, QuadIdx, new Material
        {
            Id     = 1,
            Albedo = new[] { 0.5 },
        });
        _scene.Build();
        _tracer = new RayTracer(_scene);
    }

    public void Dispose() => _scene.Dispose();

    // ── Solar direction helper ────────────────────────────────────────────

    [Fact]
    public void ComputeSolarDirection_NadirSun_IsZUp()
    {
        Vector3 dir = RayTracer.ComputeSolarDirection(0.0, 0.0);
        Assert.Equal(0.0f, dir.X, 5);
        Assert.Equal(0.0f, dir.Y, 5);
        Assert.Equal(1.0f, dir.Z, 5);
    }

    [Fact]
    public void ComputeSolarDirection_IsUnitVector()
    {
        Vector3 dir = RayTracer.ComputeSolarDirection(45.0, 120.0);
        Assert.Equal(1.0f, dir.Length(), 5);
    }

    // ── Miss path ─────────────────────────────────────────────────────────

    [Fact]
    public void MissScene_ReturnsOnlyPathRadiance()
    {
        double[] pathRad = { 0.01, 0.02, 0.03 };
        var atm = MakeAtmResult(Wavelengths, pathRad: pathRad,
                                transmittance: new[] { 0.9, 0.9, 0.9 },
                                downwelling:   new[] { 100.0, 100.0, 100.0 });

        // Ray aimed away from the scene (points upward, away from floor)
        double[] rad = _tracer.ComputeSpectralRadiance(
            viewOrigin:    new Vector3(0, 0, 5),
            viewDirection: new Vector3(0, 0, 1),   // pointing up → miss
            solarDirection: RayTracer.ComputeSolarDirection(30.0, 0.0),
            atmResult:     atm,
            wavelengths:   Wavelengths);

        for (int k = 0; k < Wavelengths.Length; k++)
            Assert.Equal(pathRad[k], rad[k], precision: 12);
    }

    // ── Surface hit path ─────────────────────────────────────────────────

    [Fact]
    public void WhiteLambertianSurface_ReturnsPositiveRadiance()
    {
        var scene2 = new SceneManager();
        scene2.AddObject(QuadVerts, QuadIdx, new Material
        {
            Id     = 0,
            Albedo = new[] { 1.0 },
        });
        scene2.Build();
        var tracer2 = new RayTracer(scene2);

        double[] pathRad    = { 0.01, 0.01, 0.01 };
        double[] downwelling = { 200.0, 200.0, 200.0 };
        var atm = MakeAtmResult(Wavelengths, pathRad: pathRad,
                                transmittance: new[] { 0.8, 0.8, 0.8 },
                                downwelling:   downwelling);

        double[] rad = tracer2.ComputeSpectralRadiance(
            viewOrigin:    new Vector3(0, 0, 5),
            viewDirection: new Vector3(0, 0, -1),
            solarDirection: RayTracer.ComputeSolarDirection(30.0, 0.0),
            atmResult:     atm,
            wavelengths:   Wavelengths);

        scene2.Dispose();

        foreach (double r in rad)
            Assert.True(r > 0.0, $"Expected positive radiance, got {r}");
    }

    [Fact]
    public void BlackSurface_LowerRadianceThanWhite()
    {
        double[] pathRad    = { 0.01, 0.01, 0.01 };
        double[] downwelling = { 200.0, 200.0, 200.0 };
        double[] tau         = { 0.8,   0.8,   0.8  };

        var atmBlack = MakeAtmResult(Wavelengths, pathRad, tau, downwelling);
        var atmWhite = MakeAtmResult(Wavelengths, pathRad, tau, downwelling);

        SceneManager MakeScene(double albedo)
        {
            var s = new SceneManager();
            s.AddObject(QuadVerts, QuadIdx, new Material { Id = 0, Albedo = new[] { albedo } });
            s.Build();
            return s;
        }

        Vector3 viewOrigin  = new(0, 0, 5);
        Vector3 viewDir     = new(0, 0, -1);
        Vector3 sunDir      = RayTracer.ComputeSolarDirection(30.0, 0.0);

        using var sceneBlack = MakeScene(0.0);
        using var sceneWhite = MakeScene(1.0);

        double[] radBlack = new RayTracer(sceneBlack).ComputeSpectralRadiance(viewOrigin, viewDir, sunDir, atmBlack, Wavelengths);
        double[] radWhite = new RayTracer(sceneWhite).ComputeSpectralRadiance(viewOrigin, viewDir, sunDir, atmWhite, Wavelengths);

        for (int k = 0; k < Wavelengths.Length; k++)
            Assert.True(radWhite[k] > radBlack[k],
                $"White radiance ({radWhite[k]:F6}) should exceed black ({radBlack[k]:F6}) at band {k}");
    }

    [Fact]
    public void ShadowedSurface_LowerRadianceThanIlluminated()
    {
        double[] pathRad    = { 0.01, 0.01, 0.01 };
        double[] downwelling = { 200.0, 200.0, 200.0 };
        double[] tau         = { 0.8,   0.8,   0.8  };
        var atm = MakeAtmResult(Wavelengths, pathRad, tau, downwelling);

        // Illuminated scene: floor only, sun unobstructed
        using var sceneOpen = new SceneManager();
        sceneOpen.AddObject(QuadVerts, QuadIdx, new Material { Id=0, Albedo = new[]{ 0.5 } });
        sceneOpen.Build();

        // Shadowed scene: floor + occluder between floor and sun (at z=1, directly above hit)
        using var sceneShadow = new SceneManager();
        sceneShadow.AddObject(QuadVerts, QuadIdx, new Material { Id=0, Albedo = new[]{ 0.5 } });
        // Reversed winding → normal faces DOWN (toward shadow ray origin on floor)
        Vector3[] occ = { new(-2,-2,1), new(-2,2,1), new(2,2,1), new(2,-2,1) };
        sceneShadow.AddObject(occ, QuadIdx, new Material { Id=1, Albedo = new[]{ 0.0 } });
        sceneShadow.Build();

        Vector3 viewOrig = new(0, 0, 5);
        Vector3 viewDir  = new(0, 0, -1);
        Vector3 sunDir   = new(0, 0, 1);   // sun directly overhead

        double[] radOpen   = new RayTracer(sceneOpen).ComputeSpectralRadiance(viewOrig, viewDir, sunDir, atm, Wavelengths);
        double[] radShadow = new RayTracer(sceneShadow).ComputeSpectralRadiance(viewOrig, viewDir, sunDir, atm, Wavelengths);

        for (int k = 0; k < Wavelengths.Length; k++)
            Assert.True(radOpen[k] > radShadow[k],
                $"Open sky ({radOpen[k]:F6}) should exceed shadow ({radShadow[k]:F6}) at band {k}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    static RadianceResult MakeAtmResult(
        double[]  wavelengths,
        double[]  pathRad,
        double[]  transmittance,
        double[]  downwelling)
    {
        return new RadianceResult
        {
            UpwellingRadiance    = new double[wavelengths.Length],
            PathRadiance         = pathRad,
            Transmittance        = transmittance,
            DownwellingIrradiance = downwelling,
            Grid                 = new WavelengthGrid(wavelengths),
            ModeName             = "Test",
        };
    }
}
