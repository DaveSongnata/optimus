using System;

namespace Optimus.Core.Optimization
{
    /// <summary>A layer as the cleanup rules see it — plain data, no COM.</summary>
    public sealed class LayerInfo
    {
        public string Name { get; set; } = "";
        public int ShapeCount { get; set; }

        /// <summary>Guides, grid, desktop and master-page layers: structural, never deletable.</summary>
        public bool IsSpecial { get; set; }
    }

    /// <summary>A shape's bounding box in millimetres, lower-left origin.</summary>
    public struct BoundsMm
    {
        public double Left, Bottom, Right, Top;

        public BoundsMm(double left, double bottom, double right, double top)
        {
            Left = left; Bottom = bottom; Right = right; Top = top;
        }
    }

    /// <summary>
    /// The structural-cleanup decisions, pure and therefore fully testable without CorelDRAW.
    ///
    /// <para>
    /// Both rules here are deliberately conservative, because both can destroy work: a "special"
    /// layer is never removed even when it reads as empty, and a shape only counts as off-page when it
    /// is ENTIRELY outside — anything crossing the page edge is part of a bleed and stays.
    /// </para>
    /// </summary>
    public static class CleanupRules
    {
        /// <summary>
        /// Tolerance in millimetres before a shape counts as outside the page. Small and positive so
        /// artwork trimmed exactly to the page edge is never mistaken for stray content.
        /// </summary>
        public const double EdgeToleranceMm = 0.01;

        public static bool IsRemovableLayer(LayerInfo layer)
        {
            if (layer == null) return false;
            if (layer.IsSpecial) return false;      // guides/grid/master: structural
            return layer.ShapeCount == 0;
        }

        /// <summary>
        /// True only when the shape lies COMPLETELY outside the page rectangle (0,0)–(width,height).
        /// A shape that crosses the boundary is bleed and must survive.
        /// </summary>
        public static bool IsFullyOffPage(BoundsMm shape, double pageWidthMm, double pageHeightMm)
        {
            if (pageWidthMm <= 0 || pageHeightMm <= 0) return false;

            // Degenerate/unreadable bounds: never delete on the strength of bad data.
            if (shape.Right < shape.Left || shape.Top < shape.Bottom) return false;

            return shape.Right < -EdgeToleranceMm
                || shape.Left > pageWidthMm + EdgeToleranceMm
                || shape.Top < -EdgeToleranceMm
                || shape.Bottom > pageHeightMm + EdgeToleranceMm;
        }
    }
}
