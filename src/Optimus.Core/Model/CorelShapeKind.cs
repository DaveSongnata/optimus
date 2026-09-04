namespace Optimus.Core.Model
{
    /// <summary>
    /// The shape kinds Optimus reasons about, mirroring <c>cdrShapeType</c> from the typelib but
    /// kept as our own enum so Core never depends on COM. Mapping lives in
    /// <see cref="GeometryClassifier.FromCorelShapeType"/>.
    /// </summary>
    public enum CorelShapeKind
    {
        Unknown = 0,

        // Parametric primitives — already minimal, converting them to curves would ADD data.
        Rectangle,
        Ellipse,
        Polygon,

        /// <summary>The only kind that carries reducible Bézier geometry.</summary>
        Curve,

        Bitmap,
        Text,

        /// <summary>Plain group — a container, not an effect.</summary>
        Group,

        // Effect groups: containers AND live effects (recomputed on every redraw → fluidity cost).
        BlendGroup,
        ExtrudeGroup,
        ContourGroup,
        BevelGroup,
        DropShadowGroup,
        ArtisticMediaGroup,
        CustomEffectGroup,

        /// <summary>Mesh fill — no automation API exposes its colors; report, never claim.</summary>
        MeshFill,

        /// <summary>Symbol instance — can WRAP other art, including bitmaps. Must be descended into.</summary>
        Symbol,

        /// <summary><c>cdrCustomShape</c> = 21. Was unmapped until a real file exposed the gap.</summary>
        Custom,

        /// <summary><c>cdrPerfectShape</c> = 26.</summary>
        Perfect,

        /// <summary>Carries raster payload but is not caught by a bitmap filter.</summary>
        Ole,

        /// <summary>Carries raster payload but is not caught by a bitmap filter.</summary>
        Eps,
    }
}
