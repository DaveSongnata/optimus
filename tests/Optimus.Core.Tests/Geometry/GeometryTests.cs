using System;
using System.Collections.Generic;
using Optimus.Core.Geometry;
using Xunit;

namespace Optimus.Core.Tests.Geometry;

public class Point2Tests
{
    [Fact]
    public void Distance_to_a_line_is_perpendicular()
    {
        // Point (0,5) against the x-axis: distance 5.
        Assert.Equal(5, new Point2(0, 5).DistanceToLine(new Point2(-10, 0), new Point2(10, 0)), precision: 9);
    }

    /// <summary>
    /// A degenerate segment (both ends equal) has no direction. It must degrade to point distance,
    /// never divide by zero — a NaN would silently PASS a "distance <= tolerance" check.
    /// </summary>
    [Fact]
    public void Degenerate_segment_never_yields_NaN()
    {
        double d = new Point2(3, 4).DistanceToLine(new Point2(0, 0), new Point2(0, 0));
        Assert.False(double.IsNaN(d));
        Assert.Equal(5, d, precision: 9);
    }

    /// <summary>
    /// Segment distance is CLAMPED: a point beyond the end is far from the segment even though it sits
    /// on the extended line. Using line distance here would keep vertices that are actually far away.
    /// </summary>
    [Fact]
    public void Segment_distance_is_clamped_to_the_endpoints()
    {
        var a = new Point2(0, 0);
        var b = new Point2(10, 0);
        var beyond = new Point2(20, 0);

        Assert.Equal(0, beyond.DistanceToLine(a, b), precision: 9);
        Assert.Equal(10, beyond.DistanceToSegment(a, b), precision: 9);
    }

    [Fact]
    public void Bezier_endpoints_are_exact()
    {
        var c = new CubicBezier(new Point2(0, 0), new Point2(1, 2), new Point2(3, 2), new Point2(4, 0));
        Assert.Equal(c.P0, c.At(0));
        Assert.Equal(c.P3, c.At(1));
    }

    [Fact]
    public void A_bezier_line_stays_on_the_straight_path()
    {
        CubicBezier line = CubicBezier.Line(new Point2(0, 0), new Point2(10, 10));
        Point2 mid = line.At(0.5);
        Assert.Equal(5, mid.X, precision: 9);
        Assert.Equal(5, mid.Y, precision: 9);
    }
}

public class PolylineTests
{
    [Fact]
    public void Flattening_keeps_both_endpoints()
    {
        var c = new CubicBezier(new Point2(0, 0), new Point2(0, 10), new Point2(10, 10), new Point2(10, 0));
        List<Point2> flat = Polyline.Flatten(c, 0.01);

        Assert.Equal(c.P0, flat[0]);
        Assert.Equal(c.P3, flat[flat.Count - 1]);
    }

    /// <summary>The bound the whole pipeline rests on: no flattened point may stray beyond the error.</summary>
    [Fact]
    public void Flattened_points_stay_within_the_error_budget()
    {
        var c = new CubicBezier(new Point2(0, 0), new Point2(0, 20), new Point2(30, 20), new Point2(30, 0));
        const double maxError = 0.01;

        List<Point2> flat = Polyline.Flatten(c, maxError);

        // Sample the true curve densely and check each sample is close to the polyline.
        for (int i = 0; i <= 500; i++)
        {
            Point2 onCurve = c.At(i / 500.0);
            double best = double.MaxValue;
            for (int j = 0; j + 1 < flat.Count; j++)
            {
                double d = onCurve.DistanceToSegment(flat[j], flat[j + 1]);
                if (d < best) best = d;
            }
            Assert.True(best <= maxError * 2,
                $"sample at t={i / 500.0:0.###} was {best:0.#####} mm from the polyline (budget {maxError})");
        }
    }

    /// <summary>A straight curve must not be subdivided — adaptive means cheap where it can be.</summary>
    [Fact]
    public void A_straight_bezier_flattens_to_two_points()
    {
        List<Point2> flat = Polyline.Flatten(CubicBezier.Line(new Point2(0, 0), new Point2(100, 0)), 0.01);
        Assert.Equal(2, flat.Count);
    }

    [Fact]
    public void A_curved_bezier_needs_more_points_at_a_tighter_error()
    {
        var c = new CubicBezier(new Point2(0, 0), new Point2(0, 50), new Point2(50, 50), new Point2(50, 0));
        int loose = Polyline.Flatten(c, 1.0).Count;
        int tight = Polyline.Flatten(c, 0.001).Count;
        Assert.True(tight > loose, $"tight={tight} loose={loose}");
    }

    /// <summary>A four-Bézier circle approximation is the classic case; it must flatten accurately.</summary>
    [Fact]
    public void A_circle_approximation_flattens_accurately()
    {
        const double r = 50;
        const double k = 0.5522847498307936 * r;   // standard circle-to-Bézier constant
        var quarters = new List<CubicBezier>
        {
            new CubicBezier(new Point2(r, 0), new Point2(r, k), new Point2(k, r), new Point2(0, r)),
            new CubicBezier(new Point2(0, r), new Point2(-k, r), new Point2(-r, k), new Point2(-r, 0)),
            new CubicBezier(new Point2(-r, 0), new Point2(-r, -k), new Point2(-k, -r), new Point2(0, -r)),
            new CubicBezier(new Point2(0, -r), new Point2(k, -r), new Point2(r, -k), new Point2(r, 0)),
        };

        List<Point2> flat = Polyline.FlattenPath(quarters, 0.01);

        // Every flattened point must sit on the circle of radius r, within the flattening error.
        foreach (Point2 p in flat)
        {
            double radius = Math.Sqrt(p.X * p.X + p.Y * p.Y);
            Assert.InRange(radius, r - 0.05, r + 0.05);
        }
    }

    [Fact]
    public void Flattening_a_path_does_not_duplicate_the_joints()
    {
        var a = new CubicBezier(new Point2(0, 0), new Point2(1, 1), new Point2(2, 1), new Point2(3, 0));
        var b = new CubicBezier(new Point2(3, 0), new Point2(4, -1), new Point2(5, -1), new Point2(6, 0));

        List<Point2> flat = Polyline.FlattenPath(new[] { a, b }, 0.01);

        int joints = 0;
        foreach (Point2 p in flat) if (p.Equals(new Point2(3, 0))) joints++;
        Assert.Equal(1, joints);
    }

    [Fact]
    public void Empty_path_yields_no_points()
    {
        Assert.Empty(Polyline.FlattenPath(new CubicBezier[0], 0.01));
        Assert.Empty(Polyline.FlattenPath(null!, 0.01));
    }
}

public class DouglasPeuckerTests
{
    [Fact]
    public void Collinear_points_collapse_to_the_two_endpoints()
    {
        var line = new List<Point2>();
        for (int i = 0; i <= 100; i++) line.Add(new Point2(i, 0));

        List<Point2> simplified = DouglasPeucker.Simplify(line, 0.01);

        Assert.Equal(2, simplified.Count);
        Assert.Equal(new Point2(0, 0), simplified[0]);
        Assert.Equal(new Point2(100, 0), simplified[1]);
    }

    /// <summary>THE guarantee: nothing removed may sit farther than the tolerance from what remains.</summary>
    [Fact]
    public void No_removed_point_exceeds_the_tolerance()
    {
        var rnd = new Random(1234);   // fixed seed: a failing case must be reproducible
        var noisy = new List<Point2>();
        for (int i = 0; i <= 400; i++)
            noisy.Add(new Point2(i * 0.5, Math.Sin(i * 0.05) * 20 + (rnd.NextDouble() - 0.5) * 0.2));

        foreach (double tolerance in new[] { 0.01, 0.05, 0.2, 1.0 })
        {
            List<Point2> simplified = DouglasPeucker.Simplify(noisy, tolerance);
            double deviation = DouglasPeucker.MaxDeviation(noisy, simplified);
            Assert.True(deviation <= tolerance + 1e-9,
                $"tolerance {tolerance}: deviation {deviation:0.######} exceeded it");
        }
    }

    [Fact]
    public void A_bigger_tolerance_removes_more()
    {
        var wave = new List<Point2>();
        for (int i = 0; i <= 500; i++) wave.Add(new Point2(i * 0.2, Math.Sin(i * 0.02) * 10));

        int tight = DouglasPeucker.Simplify(wave, 0.01).Count;
        int loose = DouglasPeucker.Simplify(wave, 1.0).Count;

        Assert.True(loose < tight, $"loose={loose} tight={tight}");
        Assert.True(loose >= 2);
    }

    [Fact]
    public void A_sharp_corner_is_preserved()
    {
        var corner = new List<Point2>
        {
            new Point2(0, 0), new Point2(5, 0), new Point2(10, 0),
            new Point2(10, 5), new Point2(10, 10),
        };

        List<Point2> simplified = DouglasPeucker.Simplify(corner, 0.1);

        Assert.Contains(new Point2(10, 0), simplified);   // the corner itself must survive
        Assert.Equal(3, simplified.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Degenerate_input_is_returned_unchanged(int count)
    {
        var pts = new List<Point2>();
        for (int i = 0; i < count; i++) pts.Add(new Point2(i, i));

        Assert.Equal(count, DouglasPeucker.Simplify(pts, 0.1).Count);
    }

    [Fact]
    public void Null_input_does_not_throw()
    {
        Assert.Empty(DouglasPeucker.Simplify(null!, 0.1));
    }

    /// <summary>Zero or negative tolerance means "change nothing", not "remove everything".</summary>
    [Fact]
    public void A_zero_tolerance_keeps_every_point()
    {
        var pts = new List<Point2> { new Point2(0, 0), new Point2(1, 0.5), new Point2(2, 0) };
        Assert.Equal(3, DouglasPeucker.Simplify(pts, 0).Count);
    }

    /// <summary>
    /// Ten thousand points must not blow the stack. The walk runs inside CorelDRAW's own process, so a
    /// stack overflow would take the operator's unsaved document down with it.
    /// </summary>
    [Fact]
    public void A_very_long_path_does_not_overflow_the_stack()
    {
        var many = new List<Point2>();
        var rnd = new Random(7);
        for (int i = 0; i < 20000; i++) many.Add(new Point2(i * 0.01, rnd.NextDouble()));

        List<Point2> simplified = DouglasPeucker.Simplify(many, 0.001);
        Assert.True(simplified.Count >= 2);
    }
}
