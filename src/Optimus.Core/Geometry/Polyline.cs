using System;
using System.Collections.Generic;

namespace Optimus.Core.Geometry
{
    /// <summary>
    /// Flattens cubic Béziers into polylines with a bounded error.
    ///
    /// <para>
    /// This is step one of the only correct way to simplify Bézier paths: <b>flatten → simplify →
    /// refit</b>. Running Douglas-Peucker straight over Bézier CONTROL POINTS — the obvious shortcut —
    /// distorts the curve, because control points are not on the curve.
    /// </para>
    /// <para>
    /// Subdivision is ADAPTIVE: a segment is split while its control points sit farther from the
    /// chord than the allowed error, so a nearly-straight curve costs a couple of points and a tight
    /// corner gets the density it needs. The flattening error must be well BELOW the simplification
    /// tolerance, otherwise it would eat the budget the operator chose for the shape.
    /// </para>
    /// </summary>
    public static class Polyline
    {
        /// <summary>
        /// Flattening error as a fraction of the caller's tolerance. A tenth keeps the flattening
        /// invisible in the final error budget.
        /// </summary>
        public const double ErrorBudgetFraction = 0.1;

        /// <summary>Guard against pathological curves; 2^12 segments is far past any real need.</summary>
        private const int MaxDepth = 12;

        /// <summary>
        /// Flattens one Bézier. The start point is included, the end point is included, and no
        /// interior point deviates from the true curve by more than <paramref name="maxErrorMm"/>.
        /// </summary>
        public static List<Point2> Flatten(CubicBezier curve, double maxErrorMm)
        {
            if (maxErrorMm <= 0) maxErrorMm = 1e-6;
            var points = new List<Point2> { curve.P0 };
            Subdivide(curve, maxErrorMm, 0, points);
            points.Add(curve.P3);
            return points;
        }

        /// <summary>Flattens a whole path, without duplicating the shared point between segments.</summary>
        public static List<Point2> FlattenPath(IList<CubicBezier> path, double maxErrorMm)
        {
            var points = new List<Point2>();
            if (path == null || path.Count == 0) return points;

            for (int i = 0; i < path.Count; i++)
            {
                List<Point2> part = Flatten(path[i], maxErrorMm);
                int from = i == 0 ? 0 : 1;   // skip the joint already added by the previous segment
                for (int j = from; j < part.Count; j++) points.Add(part[j]);
            }
            return points;
        }

        private static void Subdivide(CubicBezier c, double maxError, int depth, List<Point2> into)
        {
            if (depth >= MaxDepth || IsFlatEnough(c, maxError))
            {
                // Flat enough: the chord already represents it within the error budget.
                return;
            }

            // de Casteljau split at t = 0.5 — exact, no re-parameterisation error.
            Point2 p01 = Point2.Lerp(c.P0, c.C1, 0.5);
            Point2 p12 = Point2.Lerp(c.C1, c.C2, 0.5);
            Point2 p23 = Point2.Lerp(c.C2, c.P3, 0.5);
            Point2 p012 = Point2.Lerp(p01, p12, 0.5);
            Point2 p123 = Point2.Lerp(p12, p23, 0.5);
            Point2 mid = Point2.Lerp(p012, p123, 0.5);

            Subdivide(new CubicBezier(c.P0, p01, p012, mid), maxError, depth + 1, into);
            into.Add(mid);
            Subdivide(new CubicBezier(mid, p123, p23, c.P3), maxError, depth + 1, into);
        }

        /// <summary>
        /// Flatness test: how far the control points stray from the chord. Using the control points
        /// bounds the true curve deviation from above (the curve lies inside their hull), so the test
        /// is conservative — it never claims flat when it is not.
        /// </summary>
        private static bool IsFlatEnough(CubicBezier c, double maxError)
        {
            double d1 = c.C1.DistanceToSegment(c.P0, c.P3);
            double d2 = c.C2.DistanceToSegment(c.P0, c.P3);
            return Math.Max(d1, d2) <= maxError;
        }
    }
}
