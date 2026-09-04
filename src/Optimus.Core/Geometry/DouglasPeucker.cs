using System;
using System.Collections.Generic;

namespace Optimus.Core.Geometry
{
    /// <summary>
    /// Ramer-Douglas-Peucker polyline decimation with a guaranteed error bound.
    ///
    /// <para>
    /// Operates on POLYLINES — never on Bézier control points. It keeps the endpoints and any vertex
    /// whose perpendicular distance to the current chord exceeds the tolerance, so the guarantee is:
    /// every REMOVED vertex lies within <c>toleranceMm</c> of the kept path.
    /// </para>
    /// <para>
    /// Iterative rather than recursive: a path flattened from a complex curve can hold tens of
    /// thousands of points, and recursion on that risks a stack overflow inside CorelDRAW's process —
    /// where a crash takes the operator's unsaved work with it.
    /// </para>
    /// </summary>
    public static class DouglasPeucker
    {
        public static List<Point2> Simplify(IList<Point2> points, double toleranceMm)
        {
            var result = new List<Point2>();
            if (points == null || points.Count == 0) return result;
            if (points.Count <= 2) { result.AddRange(points); return result; }
            if (toleranceMm <= 0) { result.AddRange(points); return result; }

            var keep = new bool[points.Count];
            keep[0] = true;
            keep[points.Count - 1] = true;

            var pending = new Stack<(int First, int Last)>();
            pending.Push((0, points.Count - 1));

            while (pending.Count > 0)
            {
                (int first, int last) = pending.Pop();
                if (last <= first + 1) continue;

                double worst = -1;
                int worstIndex = -1;
                for (int i = first + 1; i < last; i++)
                {
                    double d = points[i].DistanceToSegment(points[first], points[last]);
                    if (d > worst) { worst = d; worstIndex = i; }
                }

                if (worst > toleranceMm && worstIndex > 0)
                {
                    keep[worstIndex] = true;
                    pending.Push((first, worstIndex));
                    pending.Push((worstIndex, last));
                }
            }

            for (int i = 0; i < points.Count; i++)
                if (keep[i]) result.Add(points[i]);
            return result;
        }

        /// <summary>
        /// Worst deviation of the ORIGINAL points from the simplified path. This is the proof that a
        /// simplification honoured its tolerance — the number the verification gate checks.
        /// </summary>
        public static double MaxDeviation(IList<Point2> original, IList<Point2> simplified)
        {
            if (original == null || simplified == null || simplified.Count < 2 || original.Count == 0)
                return 0;

            double worst = 0;
            foreach (Point2 p in original)
            {
                double best = double.MaxValue;
                for (int i = 0; i + 1 < simplified.Count; i++)
                {
                    double d = p.DistanceToSegment(simplified[i], simplified[i + 1]);
                    if (d < best) best = d;
                    if (best == 0) break;
                }
                if (best > worst) worst = best;
            }
            return worst;
        }
    }
}
