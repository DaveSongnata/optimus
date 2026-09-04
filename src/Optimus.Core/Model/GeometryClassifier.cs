using Optimus.Core.Corel;

namespace Optimus.Core.Model
{
    /// <summary>
    /// The rules that decide what the traversal does with each shape. Pure, so every decision is
    /// testable without CorelDRAW.
    ///
    /// <para>
    /// This is where Optimus v1.0 went wrong: it only acted on shapes that already exposed a
    /// <c>.Curve</c> and treated everything else as "skip". On a real design file — groups, effect
    /// groups and PowerClips everywhere — that means skipping nearly the whole document, which is
    /// why it achieved 0% on the client's files.
    /// </para>
    /// </summary>
    public static class GeometryClassifier
    {
        /// <summary>
        /// True when the shape stores Bézier geometry whose node count can be reduced.
        /// Parametric primitives (rectangle/ellipse/polygon) are deliberately excluded: they
        /// already store the minimum, and converting them to curves would grow the file and
        /// destroy editability.
        /// </summary>
        public static bool HasReducibleGeometry(CorelShapeKind kind) => kind == CorelShapeKind.Curve;

        /// <summary>True when the traversal must descend into the shape's children.</summary>
        public static bool IsContainer(CorelShapeKind kind)
        {
            switch (kind)
            {
                case CorelShapeKind.Group:
                case CorelShapeKind.BlendGroup:
                case CorelShapeKind.ExtrudeGroup:
                case CorelShapeKind.ContourGroup:
                case CorelShapeKind.BevelGroup:
                case CorelShapeKind.DropShadowGroup:
                case CorelShapeKind.ArtisticMediaGroup:
                case CorelShapeKind.CustomEffectGroup:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when the shape is a LIVE effect: CorelDRAW recomputes it on every redraw, so it
        /// costs interaction smoothness (Phase 4), not file size.
        /// </summary>
        public static bool IsLiveEffect(CorelShapeKind kind)
        {
            switch (kind)
            {
                case CorelShapeKind.BlendGroup:
                case CorelShapeKind.ExtrudeGroup:
                case CorelShapeKind.ContourGroup:
                case CorelShapeKind.BevelGroup:
                case CorelShapeKind.DropShadowGroup:
                case CorelShapeKind.CustomEffectGroup:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when the shape is not a formal container but can still WRAP other art, so the walk
        /// must try to descend into it anyway.
        ///
        /// <para>
        /// Symbols are the dangerous case: a symbol instance can wrap a bitmap, and treating it as
        /// a leaf makes the tool report "0 images" for a document that visibly contains one.
        /// <see cref="CorelShapeKind.Unknown"/> is included deliberately — an unmapped type must
        /// be probed rather than silently dropped.
        /// </para>
        /// </summary>
        public static bool MayContainArt(CorelShapeKind kind)
        {
            switch (kind)
            {
                case CorelShapeKind.Symbol:
                case CorelShapeKind.Custom:
                case CorelShapeKind.Perfect:
                case CorelShapeKind.Unknown:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the shape carries raster data (drives the bitmap strategy).</summary>
        public static bool CarriesRaster(CorelShapeKind kind) =>
            kind == CorelShapeKind.Bitmap || kind == CorelShapeKind.Ole || kind == CorelShapeKind.Eps;

        /// <summary>Maps a raw <c>cdrShapeType</c> value from COM onto <see cref="CorelShapeKind"/>.</summary>
        public static CorelShapeKind FromCorelShapeType(int raw)
        {
            switch (raw)
            {
                case CorelConstants.CdrRectangleShape: return CorelShapeKind.Rectangle;
                case CorelConstants.CdrEllipseShape: return CorelShapeKind.Ellipse;
                case CorelConstants.CdrCurveShape: return CorelShapeKind.Curve;
                case CorelConstants.CdrPolygonShape: return CorelShapeKind.Polygon;
                case CorelConstants.CdrBitmapShape: return CorelShapeKind.Bitmap;
                case CorelConstants.CdrTextShape: return CorelShapeKind.Text;
                case CorelConstants.CdrGroupShape: return CorelShapeKind.Group;
                case CorelConstants.CdrBlendGroupShape: return CorelShapeKind.BlendGroup;
                case CorelConstants.CdrExtrudeGroupShape: return CorelShapeKind.ExtrudeGroup;
                case CorelConstants.CdrOleObjectShape: return CorelShapeKind.Ole;
                case CorelConstants.CdrContourGroupShape: return CorelShapeKind.ContourGroup;
                case CorelConstants.CdrBevelGroupShape: return CorelShapeKind.BevelGroup;
                case CorelConstants.CdrDropShadowGroupShape: return CorelShapeKind.DropShadowGroup;
                case CorelConstants.CdrArtisticMediaGroupShape: return CorelShapeKind.ArtisticMediaGroup;
                case CorelConstants.CdrMeshFillShape: return CorelShapeKind.MeshFill;
                case CorelConstants.CdrCustomEffectGroupShape: return CorelShapeKind.CustomEffectGroup;
                case CorelConstants.CdrSymbolShape: return CorelShapeKind.Symbol;
                case CorelConstants.CdrCustomShape: return CorelShapeKind.Custom;
                case CorelConstants.CdrPerfectShape: return CorelShapeKind.Perfect;
                case CorelConstants.CdrEpsShape: return CorelShapeKind.Eps;
                default: return CorelShapeKind.Unknown;
            }
        }
    }
}
