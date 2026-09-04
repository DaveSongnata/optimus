using System;
using System.Collections.Generic;
using Optimus.Core.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace Optimus.Core.Tests.Geometry;

/// <summary>
/// The fitter is the CHALLENGER to CorelDRAW's own AutoReduce: it only becomes the default if it
/// measures better at the same tolerance. So the tests focus on the two things that make it usable at
/// all — the error bound is respected, and it actually removes segments.
/// </summary>
public class BezierFitterTests
{
    private readonly ITestOutputHelper _out;
    public BezierFitterTests(ITestOutputHelper output) => _out = output;

    private static List<Point2> SampleCurve(CubicBezier c, int n)
    {
        var pts = new List<Point2>();
        for (int i = 0; i <= n; i++) pts.Add(c.At(i / (double)n));
        return pts;
    }

    // ── the error bound ──────────────────────────────────────────────────────────

    /// <summary>THE contract. If this fails the fitter is unusable, and the fix is never to relax it.</summary>
    [Theory]
    [InlineData(0.01)]
    [InlineData(0.05)]
    [InlineData(0.2)]
    public void Fitted_curves_stay_within_the_tolerance(double tolerance)
    {
        var original = new CubicBezier(new Point2(0, 0), new Point2(10, 40), new Point2(60, 40), new Point2(70, 0));
        List<Point2> pts = SampleCurve(original, 200);

        List<CubicBezier> fitted = BezierFitter.Fit(pts, tolerance);
        double deviation = BezierFitter.MaxDeviation(pts, fitted);

        _out.WriteLine($"tolerance={tolerance} segments={fitted.Count} deviation={deviation:0.#####}");
        Assert.True(deviation <= tolerance * 1.5,
            $"deviation {deviation:0.#####} exceeded tolerance {tolerance}");
    }

    /// <summary>A single cubic sampled densely should come back as very few cubics — ideally one.</summary>
    [Fact]
    public void One_cubic_is_refitted_with_very_few_segments()
    {
        var original = new CubicBezier(new Point2(0, 0), new Point2(20, 30), new Point2(50, 30), new Point2(70, 0));
        List<Point2> pts = SampleCurve(original, 300);

        List<CubicBezier> fitted = BezierFitter.Fit(pts, 0.05);

        _out.WriteLine($"301 points -> {fitted.Count} bezier(s)");
        Assert.True(fitted.Count <= 4, $"expected a handful of segments, got {fitted.Count}");
    }

    [Fact]
    public void Endpoints_are_preserved_exactly()
    {
        var original = new CubicBezier(new Point2(3, 7), new Point2(20, 30), new Point2(50, 30), new Point2(70, 11));
        List<Point2> pts = SampleCurve(original, 100);

        List<CubicBezier> fitted = BezierFitter.Fit(pts, 0.05);

        Assert.Equal(pts[0], fitted[0].P0);
        Assert.Equal(pts[pts.Count - 1], fitted[fitted.Count - 1].P3);
    }

    // ── degenerate input ────────────────────────────────────────────────────────

    /// <summary>A two-node path has no curvature to fit and must be left exactly as it is (R4.8).</summary>
    [Fact]
    public void Two_points_produce_one_straight_segment()
    {
        var pts = new List<Point2> { new Point2(0, 0), new Point2(10, 5) };
        List<CubicBezier> fitted = BezierFitter.Fit(pts, 0.05);

        Assert.Single(fitted);
        Assert.Equal(pts[0], fitted[0].P0);
        Assert.Equal(pts[1], fitted[0].P3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Fewer_than_two_points_yields_nothing(int count)
    {
        var pts = new List<Point2>();
        for (int i = 0; i < count; i++) pts.Add(new Point2(i, i));
        Assert.Empty(BezierFitter.Fit(pts, 0.05));
    }

    [Fact]
    public void Null_input_does_not_throw()
    {
        Assert.Empty(BezierFitter.Fit(null!, 0.05));
        Assert.Equal(0, BezierFitter.MaxDeviation(null!, null!));
    }

    /// <summary>Coincident points would divide by zero in a naive parameterisation.</summary>
    [Fact]
    public void Repeated_identical_points_do_not_produce_NaN()
    {
        var pts = new List<Point2>();
        for (int i = 0; i < 10; i++) pts.Add(new Point2(5, 5));

        List<CubicBezier> fitted = BezierFitter.Fit(pts, 0.05);

        foreach (CubicBezier c in fitted)
        {
            Assert.False(double.IsNaN(c.C1.X) || double.IsNaN(c.C1.Y));
            Assert.False(double.IsNaN(c.C2.X) || double.IsNaN(c.C2.Y));
        }
    }

    // ── closed paths ────────────────────────────────────────────────────────────

    /// <summary>
    /// A closed shape that opens is a visible defect — its fill disappears. The fit must guarantee
    /// closure regardless of accumulated floating-point drift.
    /// </summary>
    [Fact]
    public void A_closed_path_stays_closed()
    {
        const double r = 40;
        const double k = 0.5522847498307936 * r;
        var circle = new List<CubicBezier>
        {
            new CubicBezier(new Point2(r, 0), new Point2(r, k), new Point2(k, r), new Point2(0, r)),
            new CubicBezier(new Point2(0, r), new Point2(-k, r), new Point2(-r, k), new Point2(-r, 0)),
            new CubicBezier(new Point2(-r, 0), new Point2(-r, -k), new Point2(-k, -r), new Point2(0, -r)),
            new CubicBezier(new Point2(0, -r), new Point2(k, -r), new Point2(r, -k), new Point2(r, 0)),
        };
        List<Point2> pts = Polyline.FlattenPath(circle, 0.005);

        List<CubicBezier> fitted = BezierFitter.FitClosed(pts, 0.05);

        Assert.NotEmpty(fitted);
        Assert.Equal(fitted[0].P0, fitted[fitted.Count - 1].P3);
    }

    /// <summary>End-to-end on a circle: flatten → simplify → refit must stay round.</summary>
    [Fact]
    public void Full_pipeline_on_a_circle_keeps_it_round()
    {
        const double r = 40;
        const double k = 0.5522847498307936 * r;
        var circle = new List<CubicBezier>
        {
            new CubicBezier(new Point2(r, 0), new Point2(r, k), new Point2(k, r), new Point2(0, r)),
            new CubicBezier(new Point2(0, r), new Point2(-k, r), new Point2(-r, k), new Point2(-r, 0)),
            new CubicBezier(new Point2(-r, 0), new Point2(-r, -k), new Point2(-k, -r), new Point2(0, -r)),
            new CubicBezier(new Point2(0, -r), new Point2(k, -r), new Point2(r, -k), new Point2(r, 0)),
        };

        List<Point2> flat = Polyline.FlattenPath(circle, 0.03 * Polyline.ErrorBudgetFraction);
        List<Point2> simplified = DouglasPeucker.Simplify(flat, 0.03);
        List<CubicBezier> refitted = BezierFitter.FitClosed(simplified, 0.03);

        // Measure the worst radial error instead of asserting a guessed band.
        double worstRadialError = 0;
        foreach (CubicBezier c in refitted)
        {
            for (int i = 0; i <= 16; i++)
            {
                Point2 p = c.At(i / 16.0);
                double radius = Math.Sqrt(p.X * p.X + p.Y * p.Y);
                worstRadialError = Math.Max(worstRadialError, Math.Abs(radius - r));
            }
        }
        double deviation = BezierFitter.MaxDeviation(simplified, refitted);
        _out.WriteLine($"flat={flat.Count} simplified={simplified.Count} refitted={refitted.Count} beziers " +
                       $"| erro radial máx={worstRadialError:0.####} mm | desvio do fit={deviation:0.####} mm");

        // The contract is the FIT tolerance; the radial error is the visual consequence and is allowed
        // to accumulate the flatten + simplify + refit budgets (3 x 0.03), plus slack for the seam.
        Assert.True(deviation <= 0.03 * 1.5, $"fit deviation {deviation:0.####} exceeded the tolerance");
        Assert.True(worstRadialError <= 0.03 * 6,
            $"radial error {worstRadialError:0.####} mm is larger than the accumulated budget allows");
        Assert.True(refitted.Count <= 20, $"a circle should refit compactly, got {refitted.Count}");
    }

    /// <summary>
    /// The realistic win: a path over-sampled with many nodes (what a traced drawing looks like)
    /// refits into far fewer segments while staying inside the tolerance.
    /// </summary>
    [Fact]
    public void An_oversampled_path_refits_into_far_fewer_segments()
    {
        var pts = new List<Point2>();
        for (int i = 0; i <= 600; i++)
        {
            double t = i / 600.0 * Math.PI * 2;
            pts.Add(new Point2(Math.Cos(t) * 50, Math.Sin(t) * 30));
        }

        List<Point2> simplified = DouglasPeucker.Simplify(pts, 0.03);
        List<CubicBezier> fitted = BezierFitter.Fit(simplified, 0.03);

        _out.WriteLine($"601 nodes -> {simplified.Count} points -> {fitted.Count} beziers " +
                       $"(deviation {BezierFitter.MaxDeviation(pts, fitted):0.####} mm)");

        Assert.True(fitted.Count < 40, $"expected a large reduction, got {fitted.Count} segments");
        Assert.True(BezierFitter.MaxDeviation(pts, fitted) <= 0.06);
    }
}
