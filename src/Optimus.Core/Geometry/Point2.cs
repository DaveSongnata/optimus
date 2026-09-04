using System;

namespace Optimus.Core.Geometry
{
    /// <summary>A point in document space, in MILLIMETRES. The unit is part of the contract.</summary>
    public struct Point2 : IEquatable<Point2>
    {
        public double X;
        public double Y;

        public Point2(double x, double y) { X = x; Y = y; }

        public double DistanceTo(Point2 other)
        {
            double dx = X - other.X, dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static Point2 Lerp(Point2 a, Point2 b, double t) =>
            new Point2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>
        /// Perpendicular distance from this point to the infinite line through a and b. When a and b
        /// coincide the line is undefined, so it degrades to the point distance rather than dividing
        /// by zero — a degenerate segment must never produce NaN and silently pass a tolerance check.
        /// </summary>
        public double DistanceToLine(Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= double.Epsilon) return DistanceTo(a);

            double cross = Math.Abs(dy * (X - a.X) - dx * (Y - a.Y));
            return cross / Math.Sqrt(lengthSquared);
        }

        /// <summary>
        /// Distance to the SEGMENT a→b (clamped), which is what a tolerance check needs: a point past
        /// the end of a segment is far from it, even if it sits on the extended line.
        /// </summary>
        public double DistanceToSegment(Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= double.Epsilon) return DistanceTo(a);

            double t = ((X - a.X) * dx + (Y - a.Y) * dy) / lengthSquared;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return DistanceTo(new Point2(a.X + t * dx, a.Y + t * dy));
        }

        public bool Equals(Point2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object? obj) => obj is Point2 p && Equals(p);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 1);
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    /// <summary>A cubic Bézier segment: start, two controls, end. Millimetres.</summary>
    public struct CubicBezier
    {
        public Point2 P0, C1, C2, P3;

        public CubicBezier(Point2 p0, Point2 c1, Point2 c2, Point2 p3)
        {
            P0 = p0; C1 = c1; C2 = c2; P3 = p3;
        }

        /// <summary>Point at parameter t via de Casteljau — numerically stable for t in [0,1].</summary>
        public Point2 At(double t)
        {
            Point2 a = Point2.Lerp(P0, C1, t);
            Point2 b = Point2.Lerp(C1, C2, t);
            Point2 c = Point2.Lerp(C2, P3, t);
            Point2 d = Point2.Lerp(a, b, t);
            Point2 e = Point2.Lerp(b, c, t);
            return Point2.Lerp(d, e, t);
        }

        /// <summary>A straight segment expressed as a Bézier, so callers handle one type.</summary>
        public static CubicBezier Line(Point2 from, Point2 to) =>
            new CubicBezier(from, Point2.Lerp(from, to, 1.0 / 3), Point2.Lerp(from, to, 2.0 / 3), to);
    }
}
