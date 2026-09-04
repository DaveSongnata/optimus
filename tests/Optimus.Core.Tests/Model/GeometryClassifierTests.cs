using Optimus.Core.Model;
using Xunit;

namespace Optimus.Core.Tests.Model;

/// <summary>
/// Decides what the traversal does with each shape. This is the rule Optimus v1.0 got wrong: it
/// only ever touched shapes that were ALREADY curves and treated everything else as "skip",
/// which on a real design file means skipping almost everything.
/// </summary>
public class GeometryClassifierTests
{
    [Theory]
    [InlineData(CorelShapeKind.Curve)]
    public void Curves_carry_reducible_geometry(CorelShapeKind kind)
    {
        Assert.True(GeometryClassifier.HasReducibleGeometry(kind));
    }

    [Theory]
    [InlineData(CorelShapeKind.Bitmap)]
    [InlineData(CorelShapeKind.Text)]
    [InlineData(CorelShapeKind.Ole)]
    [InlineData(CorelShapeKind.Eps)]
    public void Non_geometric_shapes_are_not_reducible(CorelShapeKind kind)
    {
        Assert.False(GeometryClassifier.HasReducibleGeometry(kind));
    }

    /// <summary>
    /// Rectangles/ellipses/polygons are parametric: they already store the minimum and converting
    /// them to curves to "reduce" them would ADD data and destroy editability.
    /// </summary>
    [Theory]
    [InlineData(CorelShapeKind.Rectangle)]
    [InlineData(CorelShapeKind.Ellipse)]
    [InlineData(CorelShapeKind.Polygon)]
    public void Parametric_shapes_are_left_alone(CorelShapeKind kind)
    {
        Assert.False(GeometryClassifier.HasReducibleGeometry(kind));
    }

    [Theory]
    [InlineData(CorelShapeKind.Group)]
    [InlineData(CorelShapeKind.BlendGroup)]
    [InlineData(CorelShapeKind.ContourGroup)]
    [InlineData(CorelShapeKind.DropShadowGroup)]
    public void Containers_must_be_descended_into(CorelShapeKind kind)
    {
        Assert.True(GeometryClassifier.IsContainer(kind));
    }

    [Theory]
    [InlineData(CorelShapeKind.Curve)]
    [InlineData(CorelShapeKind.Bitmap)]
    [InlineData(CorelShapeKind.Text)]
    public void Leaves_are_not_containers(CorelShapeKind kind)
    {
        Assert.False(GeometryClassifier.IsContainer(kind));
    }

    /// <summary>Effect groups are containers AND count as live effects (fluidity cost, Phase 4).</summary>
    [Theory]
    [InlineData(CorelShapeKind.BlendGroup)]
    [InlineData(CorelShapeKind.ExtrudeGroup)]
    [InlineData(CorelShapeKind.ContourGroup)]
    [InlineData(CorelShapeKind.DropShadowGroup)]
    [InlineData(CorelShapeKind.BevelGroup)]
    public void Effect_groups_are_flagged_as_live_effects(CorelShapeKind kind)
    {
        Assert.True(GeometryClassifier.IsLiveEffect(kind));
    }

    [Theory]
    [InlineData(CorelShapeKind.Group)]
    [InlineData(CorelShapeKind.Curve)]
    public void Plain_groups_and_curves_are_not_live_effects(CorelShapeKind kind)
    {
        Assert.False(GeometryClassifier.IsLiveEffect(kind));
    }

    /// <summary>
    /// A symbol instance can WRAP a bitmap. Treating it as a leaf makes the tool report
    /// "0 images" for a document that visibly contains one — found against a real client file.
    /// </summary>
    [Theory]
    [InlineData(CorelShapeKind.Symbol)]
    [InlineData(CorelShapeKind.Custom)]
    [InlineData(CorelShapeKind.Perfect)]
    public void Art_wrapping_shapes_must_be_probed_for_children(CorelShapeKind kind)
    {
        Assert.True(GeometryClassifier.MayContainArt(kind));
    }

    /// <summary>An unmapped shape type must be probed, never silently dropped.</summary>
    [Fact]
    public void Unknown_shapes_are_probed_not_dropped()
    {
        Assert.True(GeometryClassifier.MayContainArt(CorelShapeKind.Unknown));
    }

    [Theory]
    [InlineData(CorelShapeKind.Curve)]
    [InlineData(CorelShapeKind.Bitmap)]
    [InlineData(CorelShapeKind.Text)]
    [InlineData(CorelShapeKind.Rectangle)]
    public void Real_leaves_are_not_probed_for_children(CorelShapeKind kind)
    {
        Assert.False(GeometryClassifier.MayContainArt(kind));
        Assert.False(GeometryClassifier.IsContainer(kind));
    }

    /// <summary>Maps the raw `cdrShapeType` int from COM onto our enum. Values from the typelib.</summary>
    [Theory]
    [InlineData(3, CorelShapeKind.Curve)]
    [InlineData(5, CorelShapeKind.Bitmap)]
    [InlineData(6, CorelShapeKind.Text)]
    [InlineData(7, CorelShapeKind.Group)]
    [InlineData(12, CorelShapeKind.Ole)]
    [InlineData(21, CorelShapeKind.Custom)]   // was unmapped until a real document hit it
    [InlineData(23, CorelShapeKind.Symbol)]
    [InlineData(26, CorelShapeKind.Perfect)]
    [InlineData(27, CorelShapeKind.Eps)]
    [InlineData(999, CorelShapeKind.Unknown)]
    public void Maps_corel_shape_type_ints(int raw, CorelShapeKind expected)
    {
        Assert.Equal(expected, GeometryClassifier.FromCorelShapeType(raw));
    }
}
