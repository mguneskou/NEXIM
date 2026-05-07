// NEXIM — Phase 12 unit tests: segmentation algorithms + SAM propagator.

using NEXIM.Core.Segmentation;

namespace NEXIM.Tests.Segmentation;

// ─────────────────────────────────────────────────────────────────────────────
// Shared helpers
// ─────────────────────────────────────────────────────────────────────────────

file static class PixelFactory
{
    /// <summary>
    /// Build <paramref name="n"/> pixels whose feature vectors are unit vectors
    /// pointing along dimension <c>i % dims</c> (creates <c>dims</c> clusters).
    /// </summary>
    public static List<PixelFeature> Clustered(int n, int dims)
    {
        var list = new List<PixelFeature>(n);
        for (int i = 0; i < n; i++)
        {
            double[] feat = new double[dims];
            feat[i % dims] = 1.0;
            list.Add(new PixelFeature { Row = i, Column = 0, Features = feat });
        }
        return list;
    }

    /// <summary>Single pixel at the given feature vector.</summary>
    public static PixelFeature Single(params double[] features) =>
        new() { Row = 0, Column = 0, Features = features };
}

// ─────────────────────────────────────────────────────────────────────────────
// FeatureExtractor tests
// ─────────────────────────────────────────────────────────────────────────────

public class FeatureExtractorTests
{
    [Fact]
    public void FromBilCube_PixelCount_Correct()
    {
        var cube = MakeCube(3, 4, 5);
        var pix  = FeatureExtractor.FromBilCube(cube, 3, 4, 5);
        Assert.Equal(3 * 5, pix.Count);  // rows × columns
    }

    [Fact]
    public void FromBilCube_FeatureLength_EqualsBands()
    {
        var cube = MakeCube(2, 6, 4);
        var pix  = FeatureExtractor.FromBilCube(cube, 2, 6, 4);
        Assert.All(pix, p => Assert.Equal(6, p.Features.Length));
    }

    [Fact]
    public void NormaliseL2_UnitNorm()
    {
        var pix = new List<PixelFeature>
        {
            new() { Row = 0, Column = 0, Features = new[] { 3.0, 4.0 } },
        };
        FeatureExtractor.NormaliseL2(pix);
        double norm = Math.Sqrt(pix[0].Features[0] * pix[0].Features[0]
                               + pix[0].Features[1] * pix[0].Features[1]);
        Assert.Equal(1.0, norm, 10);
    }

    [Fact]
    public void NormaliseMinMax_RangeIsZeroToOne()
    {
        var pix = new List<PixelFeature>
        {
            new() { Row = 0, Column = 0, Features = new[] { 2.0, 10.0 } },
            new() { Row = 1, Column = 0, Features = new[] { 8.0,  0.0 } },
        };
        FeatureExtractor.NormaliseMinMax(pix);
        // dim 0: min=2, max=8 → 0.0 and 1.0; dim 1: min=0, max=10 → 1.0 and 0.0
        Assert.Equal(0.0, pix[0].Features[0], 10);
        Assert.Equal(1.0, pix[1].Features[0], 10);
        Assert.Equal(1.0, pix[0].Features[1], 10);
        Assert.Equal(0.0, pix[1].Features[1], 10);
    }

    static float[][] MakeCube(int rows, int bands, int columns)
    {
        var cube = new float[rows * bands][];
        for (int r = 0; r < rows; r++)
            for (int b = 0; b < bands; b++)
            {
                cube[r * bands + b] = new float[columns];
                for (int c = 0; c < columns; c++)
                    cube[r * bands + b][c] = r * 100f + b * 10f + c;
            }
        return cube;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// NativeKMeans tests
// ─────────────────────────────────────────────────────────────────────────────

public class NativeKMeansTests
{
    [Fact]
    public void Run_EmptyInput_ReturnsEmptyResult()
    {
        var r = NativeKMeans.Run(new List<PixelFeature>(), 3);
        Assert.Empty(r.Labels);
        Assert.Equal(0, r.ClassCount);
    }

    [Fact]
    public void Run_LabelCountEqualsPixelCount()
    {
        var pix = PixelFactory.Clustered(20, 4);
        var r   = NativeKMeans.Run(pix, 4);
        Assert.Equal(20, r.Labels.Length);
    }

    [Fact]
    public void Run_LabelsInRange()
    {
        int k   = 3;
        var pix = PixelFactory.Clustered(30, k);
        var r   = NativeKMeans.Run(pix, k);
        Assert.All(r.Labels, l => Assert.InRange(l, 0, k - 1));
    }

    [Fact]
    public void Run_WellSeparatedClusters_CorrectClassCount()
    {
        // 3 well-separated clusters of 10 pixels each in 3D
        var pix = new List<PixelFeature>();
        for (int i = 0; i < 10; i++) pix.Add(PixelFactory.Single(1.0, 0.0, 0.0));
        for (int i = 0; i < 10; i++) pix.Add(PixelFactory.Single(0.0, 1.0, 0.0));
        for (int i = 0; i < 10; i++) pix.Add(PixelFactory.Single(0.0, 0.0, 1.0));

        var r = NativeKMeans.Run(pix, 3, seed: 0);
        Assert.Equal(3, r.ClassCount);
        // All pixels in the same original group should share a label
        int lbl0 = r.Labels[0];
        Assert.All(r.Labels[..10], l => Assert.Equal(lbl0, l));
        int lbl1 = r.Labels[10];
        Assert.All(r.Labels[10..20], l => Assert.Equal(lbl1, l));
        int lbl2 = r.Labels[20];
        Assert.All(r.Labels[20..30], l => Assert.Equal(lbl2, l));
        Assert.True(lbl0 != lbl1 && lbl1 != lbl2 && lbl0 != lbl2);
    }

    [Fact]
    public void Run_CentroidsNotNull()
    {
        var r = NativeKMeans.Run(PixelFactory.Clustered(10, 3), 3);
        Assert.NotNull(r.Centroids);
        Assert.Equal(3, r.Centroids!.Length);
    }

    [Fact]
    public void Run_KGreaterThanN_ClampsToCoverAllPixels()
    {
        var pix = PixelFactory.Clustered(5, 2);
        var r   = NativeKMeans.Run(pix, 10);  // k > n → ek = 5
        Assert.Equal(5, r.ClassCount);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// KMeansSegmenter tests (smoke tests — ML.NET path)
// ─────────────────────────────────────────────────────────────────────────────

public class KMeansSegmenterTests
{
    [Fact]
    public void Segment_EmptyInput_ReturnsEmptyResult()
    {
        var seg = new KMeansSegmenter(k: 3);
        var r   = seg.Segment(new List<PixelFeature>());
        Assert.Empty(r.Labels);
    }

    [Fact]
    public void Segment_LabelCountMatchesPixelCount()
    {
        var seg = new KMeansSegmenter(k: 3, seed: 0);
        var pix = PixelFactory.Clustered(30, 3);
        var r   = seg.Segment(pix);
        Assert.Equal(30, r.Labels.Length);
    }

    [Fact]
    public void Segment_LabelsInRange()
    {
        var seg = new KMeansSegmenter(k: 4, seed: 1);
        var pix = PixelFactory.Clustered(40, 4);
        var r   = seg.Segment(pix);
        Assert.All(r.Labels, l => Assert.InRange(l, 0, 3));
    }

    [Fact]
    public void Segment_K1_AllLabelsZero()
    {
        var seg = new KMeansSegmenter(k: 1);
        var pix = PixelFactory.Clustered(10, 3);
        var r   = seg.Segment(pix);
        Assert.All(r.Labels, l => Assert.Equal(0, l));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// GmmSegmenter tests
// ─────────────────────────────────────────────────────────────────────────────

public class GmmSegmenterTests
{
    [Fact]
    public void Segment_EmptyInput_ReturnsEmpty()
    {
        var r = new GmmSegmenter(k: 3).Segment(new List<PixelFeature>());
        Assert.Empty(r.Labels);
    }

    [Fact]
    public void Segment_LabelCountMatchesPixelCount()
    {
        var pix = PixelFactory.Clustered(30, 3);
        var r   = new GmmSegmenter(k: 3, seed: 0).Segment(pix);
        Assert.Equal(30, r.Labels.Length);
    }

    [Fact]
    public void Segment_LabelsInRange()
    {
        int k   = 3;
        var pix = PixelFactory.Clustered(30, k);
        var r   = new GmmSegmenter(k: k, seed: 0).Segment(pix);
        Assert.All(r.Labels, l => Assert.InRange(l, 0, k - 1));
    }

    [Fact]
    public void Segment_ProbabilitiesSumToOne()
    {
        var pix = PixelFactory.Clustered(20, 3);
        var r   = new GmmSegmenter(k: 3, seed: 0).Segment(pix);
        Assert.NotNull(r.Probabilities);
        foreach (double[] probs in r.Probabilities!)
        {
            double sum = probs.Sum();
            Assert.Equal(1.0, sum, 4);
        }
    }

    [Fact]
    public void Segment_WellSeparated_GroupsConsistent()
    {
        var pix = new List<PixelFeature>();
        for (int i = 0; i < 15; i++) pix.Add(PixelFactory.Single(1.0, 0.0, 0.0));
        for (int i = 0; i < 15; i++) pix.Add(PixelFactory.Single(0.0, 0.0, 1.0));

        var r = new GmmSegmenter(k: 2, seed: 0).Segment(pix);
        // First 15 and last 15 should each share the same label
        int l0 = r.Labels[0];
        Assert.All(r.Labels[..15], l => Assert.Equal(l0, l));
        int l1 = r.Labels[15];
        Assert.All(r.Labels[15..30], l => Assert.Equal(l1, l));
        Assert.NotEqual(l0, l1);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DbscanSegmenter tests
// ─────────────────────────────────────────────────────────────────────────────

public class DbscanSegmenterTests
{
    [Fact]
    public void Segment_EmptyInput_ReturnsEmpty()
    {
        var r = new DbscanSegmenter(0.5, 2).Segment(new List<PixelFeature>());
        Assert.Empty(r.Labels);
    }

    [Fact]
    public void Segment_LabelCountMatchesPixelCount()
    {
        var pix = PixelFactory.Clustered(20, 3);
        var r   = new DbscanSegmenter(0.5, 2).Segment(pix);
        Assert.Equal(20, r.Labels.Length);
    }

    [Fact]
    public void Segment_TwoDenseClusters_TwoLabels()
    {
        // Two tight groups at (0,0,0) and (10,10,10)
        var pix = new List<PixelFeature>();
        for (int i = 0; i < 10; i++)
            pix.Add(PixelFactory.Single(0.0 + i * 0.01, 0.0, 0.0));
        for (int i = 0; i < 10; i++)
            pix.Add(PixelFactory.Single(10.0 + i * 0.01, 0.0, 0.0));

        var r = new DbscanSegmenter(epsilon: 0.5, minPoints: 3).Segment(pix);
        Assert.Equal(2, r.ClassCount);
        int lbl0 = r.Labels[0];
        Assert.All(r.Labels[..10], l => Assert.Equal(lbl0, l));
        int lbl1 = r.Labels[10];
        Assert.All(r.Labels[10..20], l => Assert.Equal(lbl1, l));
        Assert.NotEqual(lbl0, lbl1);
    }

    [Fact]
    public void Segment_IsolatedPoints_LabelledNoise()
    {
        // Each point 100 apart — all noise with minPoints=2
        var pix = new List<PixelFeature>();
        for (int i = 0; i < 5; i++)
            pix.Add(PixelFactory.Single(i * 100.0, 0.0));

        var r = new DbscanSegmenter(epsilon: 0.5, minPoints: 2).Segment(pix);
        Assert.All(r.Labels, l => Assert.Equal(-1, l));
        Assert.Equal(0, r.ClassCount);
    }

    [Fact]
    public void InvalidEpsilon_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DbscanSegmenter(0.0, 2));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// SamPropagator tests
// ─────────────────────────────────────────────────────────────────────────────

public class SamPropagatorTests
{
    static Endmember MakeEndmember(string name, params double[] spectrum) =>
        new() { Name = name, Spectrum = spectrum };

    [Fact]
    public void SpectralAngle_IdenticalVectors_ReturnsZero()
    {
        double angle = SamPropagator.SpectralAngle(new[] { 1.0, 2.0, 3.0 },
                                                    new[] { 1.0, 2.0, 3.0 });
        Assert.Equal(0.0, angle, 10);
    }

    [Fact]
    public void SpectralAngle_OrthogonalVectors_ReturnsHalfPi()
    {
        double angle = SamPropagator.SpectralAngle(new[] { 1.0, 0.0 },
                                                    new[] { 0.0, 1.0 });
        Assert.Equal(Math.PI / 2.0, angle, 10);
    }

    [Fact]
    public void SpectralAngle_ZeroVector_ReturnsHalfPi()
    {
        double angle = SamPropagator.SpectralAngle(new[] { 0.0, 0.0 },
                                                    new[] { 1.0, 0.0 });
        Assert.Equal(Math.PI / 2.0, angle, 10);
    }

    [Fact]
    public void SpectralAngle_ScaledVector_SameAngle()
    {
        // SAM is illumination-invariant: scaling one vector by 100× shouldn't change angle
        double a1 = SamPropagator.SpectralAngle(new[] { 1.0, 2.0, 3.0 },
                                                 new[] { 1.0, 2.0, 3.0 });
        double a2 = SamPropagator.SpectralAngle(new[] { 1.0, 2.0, 3.0 },
                                                 new[] { 100.0, 200.0, 300.0 });
        Assert.Equal(a1, a2, 10);
    }

    [Fact]
    public void Classify_ExactMatch_AssignsCorrectEndmember()
    {
        var em  = new[] { MakeEndmember("A", 1.0, 0.0), MakeEndmember("B", 0.0, 1.0) };
        var sam = new SamPropagator(em, threshold_rad: 0.05);

        var pix = new List<PixelFeature>
        {
            new() { Row = 0, Column = 0, Features = new[] { 1.0, 0.0 } },
            new() { Row = 1, Column = 0, Features = new[] { 0.0, 1.0 } },
        };

        var results = sam.Classify(pix);
        Assert.Equal(0, results[0].AssignedEndmember);
        Assert.Equal(1, results[1].AssignedEndmember);
    }

    [Fact]
    public void Classify_BeyondThreshold_AssignsMinus1()
    {
        var em  = new[] { MakeEndmember("A", 1.0, 0.0) };
        // Threshold very small so pixel (0.5, 0.5) is outside
        var sam = new SamPropagator(em, threshold_rad: 0.001);

        var pix = new List<PixelFeature>
        {
            new() { Row = 0, Column = 0, Features = new[] { 0.5, 0.5 } },
        };
        var results = sam.Classify(pix);
        Assert.Equal(-1, results[0].AssignedEndmember);
    }

    [Fact]
    public void Classify_AnglesLength_EqualsEndmemberCount()
    {
        var em  = new[] { MakeEndmember("A", 1.0, 0.0), MakeEndmember("B", 0.0, 1.0) };
        var sam = new SamPropagator(em, threshold_rad: 1.0);
        var pix = new List<PixelFeature>
        {
            new() { Row = 0, Column = 0, Features = new[] { 0.7, 0.3 } },
        };
        var results = sam.Classify(pix);
        Assert.Equal(2, results[0].Angles_rad.Length);
    }

    [Fact]
    public void ToSegmentationResult_LabelsMatch()
    {
        var samResults = new[]
        {
            new SamPixelResult { Row=0, Column=0, AssignedEndmember=0, MinAngle_rad=0.0, Angles_rad=new[]{0.0,1.5} },
            new SamPixelResult { Row=0, Column=1, AssignedEndmember=-1, MinAngle_rad=1.6, Angles_rad=new[]{1.6,1.6} },
        };
        var seg = SamPropagator.ToSegmentationResult(samResults, 2);
        Assert.Equal(new[] { 0, -1 }, seg.Labels);
        Assert.Equal(2, seg.ClassCount);
    }

    [Fact]
    public void Constructor_EmptyEndmembers_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SamPropagator(Array.Empty<Endmember>()));
    }

    [Fact]
    public void Constructor_ZeroThreshold_Throws()
    {
        var em = new[] { MakeEndmember("A", 1.0) };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SamPropagator(em, threshold_rad: 0.0));
    }
}
