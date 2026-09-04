using System;
using System.Collections.Generic;

namespace Optimus.Core.Geometry
{
    /// <summary>
    /// Fits the fewest cubic Béziers that stay within a tolerance of a point sequence — the
    /// "refit" step of <b>flatten → simplify → refit</b>.
    ///
    /// <para>
    /// Algorithm: Philip J. Schneider, "An Algorithm for Automatically Fitting Digitized Curves",
    /// Graphics Gems (1990) — the same approach behind Inkscape's Simplify and potrace. Fit one cubic
    /// to the whole span; if the worst error exceeds the tolerance, split at the worst point and
    /// recurse. Interior joins are given the chord tangent so neighbouring segments meet smoothly.
    /// </para>
    /// <para>
    /// REUSE DECISION (plan task 1): no permissive-licence .NET package was confirmed that fits
    /// netstandard2.0 with zero dependencies, and the algorithm is a published, widely reimplemented
    /// ~200 lines that needs its own tests either way. Implemented here rather than taking a
    /// dependency whose licence I could not verify — bundling only permissive code is a house rule.
    /// </para>
    /// <para>
    /// This is the CHALLENGER, not the default: CorelDRAW's own <c>AutoReduce</c> stays in charge
    /// until this measures better on the same file at the same tolerance (verdict V2).
    /// </para>
    /// </summary>
    public static class BezierFitter
    {
        /// <summary>Newton-Raphson passes used to improve each point's parameter estimate.</summary>
        private const int RefineIterations = 4;

        /// <summary>Recursion cap; each level at least halves the span.</summary>
        private const int MaxDepth = 16;

        /// <summary>
        /// Fits <paramref name="points"/> within <paramref name="toleranceMm"/>. Fewer than three
        /// points cannot be curved, so they come back as straight segments — a two-node path is left
        /// exactly as it was (R4.8).
        /// </summary>
        public static List<CubicBezier> Fit(IList<Point2> points, double toleranceMm)
        {
            var result = new List<CubicBezier>();
            if (points == null || points.Count < 2) return result;
            if (toleranceMm <= 0) toleranceMm = 1e-6;

            if (points.Count == 2)
            {
                result.Add(CubicBezier.Line(points[0], points[1]));
                return result;
            }

            Point2 leftTangent = Normalize(Subtract(points[1], points[0]));
            Point2 rightTangent = Normalize(Subtract(points[points.Count - 2], points[points.Count - 1]));
            FitRecursive(points, 0, points.Count - 1, leftTangent, rightTangent, toleranceMm, 0, result);
            return result;
        }

        /// <summary>
        /// Fits a CLOSED path, guaranteeing it comes back closed: the last segment's end point is
        /// snapped to the first segment's start. A closed shape that opens is a visible defect —
        /// its fill disappears.
        /// </summary>
        public static List<CubicBezier> FitClosed(IList<Point2> points, double toleranceMm)
        {
            List<CubicBezier> fitted = Fit(points, toleranceMm);
            if (fitted.Count == 0) return fitted;

            CubicBezier last = fitted[fitted.Count - 1];
            Point2 start = fitted[0].P0;
            if (!last.P3.Equals(start))
            {
                last.P3 = start;
                fitted[fitted.Count - 1] = last;
            }
            return fitted;
        }

        /// <summary>Worst distance from the original points to the fitted curves — the proof of R4.2.</summary>
        public static double MaxDeviation(IList<Point2> points, IList<CubicBezier> curves, int samplesPerCurve = 24)
        {
            if (points == null || curves == null || curves.Count == 0) return 0;

            // Sample the fitted curves into a dense polyline, then measure point-to-polyline distance.
            var sampled = new List<Point2>();
            foreach (CubicBezier c in curves)
            {
                for (int i = 0; i <= samplesPerCurve; i++) sampled.Add(c.At(i / (double)samplesPerCurve));
            }

            double worst = 0;
            foreach (Point2 p in points)
            {
                double best = double.MaxValue;
                for (int i = 0; i + 1 < sampled.Count; i++)
                {
                    double d = p.DistanceToSegment(sampled[i], sampled[i + 1]);
                    if (d < best) best = d;
                    if (best == 0) break;
                }
                if (best > worst) worst = best;
            }
            return worst;
        }

        private static void FitRecursive(
            IList<Point2> pts, int first, int last, Point2 tan1, Point2 tan2,
            double tolerance, int depth, List<CubicBezier> into)
        {
            int count = last - first + 1;

            // Two points: a line is the exact answer.
            if (count == 2)
            {
                into.Add(CubicBezier.Line(pts[first], pts[last]));
                return;
            }

            double[] u = ChordLengthParameterize(pts, first, last);
            CubicBezier curve = GenerateBezier(pts, first, last, u, tan1, tan2);

            double error = ComputeMaxError(pts, first, last, curve, u, out int splitAt);
            if (error <= tolerance) { into.Add(curve); return; }

            // Close enough to refine rather than split: improve the parameters and retry once.
            if (error <= tolerance * 4 && depth < MaxDepth)
            {
                for (int i = 0; i < RefineIterations; i++)
                {
                    u = Reparameterize(pts, first, last, u, curve);
                    curve = GenerateBezier(pts, first, last, u, tan1, tan2);
                    error = ComputeMaxError(pts, first, last, curve, u, out splitAt);
                    if (error <= tolerance) { into.Add(curve); return; }
                }
            }

            if (depth >= MaxDepth || splitAt <= first || splitAt >= last)
            {
                // Cannot split further: keep the best fit we have rather than looping forever.
                into.Add(curve);
                return;
            }

            // Split at the worst point, giving the join a centre tangent so the halves meet smoothly.
            Point2 centre = Normalize(Subtract(pts[splitAt - 1], pts[splitAt + 1]));
            FitRecursive(pts, first, splitAt, tan1, centre, tolerance, depth + 1, into);
            FitRecursive(pts, splitAt, last, Negate(centre), tan2, tolerance, depth + 1, into);
        }

        /// <summary>Least-squares fit of one cubic with the given end tangents (Schneider §Bezier).</summary>
        private static CubicBezier GenerateBezier(
            IList<Point2> pts, int first, int last, double[] u, Point2 tan1, Point2 tan2)
        {
            int n = last - first + 1;
            var a0 = new Point2[n];
            var a1 = new Point2[n];

            for (int i = 0; i < n; i++)
            {
                a0[i] = Scale(tan1, B1(u[i]));
                a1[i] = Scale(tan2, B2(u[i]));
            }

            double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
            Point2 p0 = pts[first], p3 = pts[last];

            for (int i = 0; i < n; i++)
            {
                c00 += Dot(a0[i], a0[i]);
                c01 += Dot(a0[i], a1[i]);
                c11 += Dot(a1[i], a1[i]);

                // The part of the point not explained by the fixed endpoints.
                Point2 target = Subtract(pts[first + i],
                    Add(Add(Scale(p0, B0(u[i])), Scale(p0, B1(u[i]))),
                        Add(Scale(p3, B2(u[i])), Scale(p3, B3(u[i])))));

                x0 += Dot(a0[i], target);
                x1 += Dot(a1[i], target);
            }

            double det = c00 * c11 - c01 * c01;
            double alphaL = 0, alphaR = 0;
            if (Math.Abs(det) > 1e-12)
            {
                alphaL = (c11 * x0 - c01 * x1) / det;
                alphaR = (c00 * x1 - c01 * x0) / det;
            }

            // Degenerate or negative solution: fall back to Wu/Barsky's heuristic (a third of the
            // chord), which is what Schneider prescribes and keeps the curve sane.
            double segLength = p0.DistanceTo(p3);
            double epsilon = 1e-6 * segLength;
            if (alphaL < epsilon || alphaR < epsilon)
            {
                alphaL = alphaR = segLength / 3.0;
            }

            return new CubicBezier(p0, Add(p0, Scale(tan1, alphaL)), Add(p3, Scale(tan2, alphaR)), p3);
        }

        /// <summary>Parameters proportional to accumulated chord length — Schneider's initial guess.</summary>
        private static double[] ChordLengthParameterize(IList<Point2> pts, int first, int last)
        {
            int n = last - first + 1;
            var u = new double[n];
            u[0] = 0;
            for (int i = 1; i < n; i++)
                u[i] = u[i - 1] + pts[first + i].DistanceTo(pts[first + i - 1]);

            double total = u[n - 1];
            if (total <= 0) { for (int i = 0; i < n; i++) u[i] = i / (double)(n - 1); return u; }
            for (int i = 1; i < n; i++) u[i] /= total;
            return u;
        }

        /// <summary>One Newton-Raphson pass moving each parameter toward its closest point.</summary>
        private static double[] Reparameterize(
            IList<Point2> pts, int first, int last, double[] u, CubicBezier c)
        {
            var result = new double[u.Length];
            for (int i = 0; i < u.Length; i++)
                result[i] = NewtonRaphsonRootFind(c, pts[first + i], u[i]);
            return result;
        }

        private static double NewtonRaphsonRootFind(CubicBezier c, Point2 p, double u)
        {
            Point2 onCurve = c.At(u);
            Point2 d1 = Derivative1(c, u);
            Point2 d2 = Derivative2(c, u);

            Point2 diff = Subtract(onCurve, p);
            double numerator = Dot(diff, d1);
            double denominator = Dot(d1, d1) + Dot(diff, d2);
            if (Math.Abs(denominator) < 1e-12) return u;

            double improved = u - numerator / denominator;
            if (double.IsNaN(improved) || double.IsInfinity(improved)) return u;
            return improved < 0 ? 0 : improved > 1 ? 1 : improved;
        }

        private static double ComputeMaxError(
            IList<Point2> pts, int first, int last, CubicBezier c, double[] u, out int splitAt)
        {
            splitAt = (last - first + 1) / 2 + first;
            double maxError = 0;

            for (int i = 1; i < last - first; i++)
            {
                double dist = c.At(u[i]).DistanceTo(pts[first + i]);
                if (dist > maxError) { maxError = dist; splitAt = first + i; }
            }
            return maxError;
        }

        // ── Bernstein basis and derivatives ──────────────────────────────────────────
        private static double B0(double u) { double t = 1 - u; return t * t * t; }
        private static double B1(double u) { double t = 1 - u; return 3 * u * t * t; }
        private static double B2(double u) { double t = 1 - u; return 3 * u * u * t; }
        private static double B3(double u) => u * u * u;

        private static Point2 Derivative1(CubicBezier c, double u)
        {
            Point2 a = Scale(Subtract(c.C1, c.P0), 3);
            Point2 b = Scale(Subtract(c.C2, c.C1), 3);
            Point2 d = Scale(Subtract(c.P3, c.C2), 3);
            double t = 1 - u;
            return Add(Add(Scale(a, t * t), Scale(b, 2 * u * t)), Scale(d, u * u));
        }

        private static Point2 Derivative2(CubicBezier c, double u)
        {
            Point2 a = Scale(Subtract(c.C1, c.P0), 3);
            Point2 b = Scale(Subtract(c.C2, c.C1), 3);
            Point2 d = Scale(Subtract(c.P3, c.C2), 3);
            Point2 e = Scale(Subtract(b, a), 2);
            Point2 f = Scale(Subtract(d, b), 2);
            return Add(Scale(e, 1 - u), Scale(f, u));
        }

        // ── small vector helpers ─────────────────────────────────────────────────────
        private static Point2 Add(Point2 a, Point2 b) => new Point2(a.X + b.X, a.Y + b.Y);
        private static Point2 Subtract(Point2 a, Point2 b) => new Point2(a.X - b.X, a.Y - b.Y);
        private static Point2 Scale(Point2 a, double s) => new Point2(a.X * s, a.Y * s);
        private static Point2 Negate(Point2 a) => new Point2(-a.X, -a.Y);
        private static double Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;

        private static Point2 Normalize(Point2 a)
        {
            double len = Math.Sqrt(a.X * a.X + a.Y * a.Y);
            return len <= double.Epsilon ? new Point2(0, 0) : new Point2(a.X / len, a.Y / len);
        }
    }
}
