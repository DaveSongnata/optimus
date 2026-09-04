using Optimus.Core.Model;
using Xunit;

namespace Optimus.Core.Tests.Model;

/// <summary>
/// The inventory is what tells us whether the traversal actually saw the document. Optimus v1.0
/// reported success while touching nothing, because it never descended into PowerClips and skipped
/// every shape that was not already a curve. These tests pin that behaviour down.
/// </summary>
public class ShapeInventoryTests
{
    private static ShapeRecord Rec(CorelShapeKind kind, int nodes = 0, bool inPowerClip = false) =>
        new ShapeRecord(kind, nodes, inPowerClip);

    [Fact]
    public void Empty_inventory_reports_nothing()
    {
        var inv = ShapeInventory.From(new ShapeRecord[0]);

        Assert.Equal(0, inv.TotalShapes);
        Assert.Equal(0, inv.TotalNodes);
        Assert.Equal(0, inv.ShapesInPowerClip);
        Assert.Empty(inv.CountByKind);
    }

    [Fact]
    public void Counts_shapes_and_nodes_by_kind()
    {
        var inv = ShapeInventory.From(new[]
        {
            Rec(CorelShapeKind.Curve, nodes: 10),
            Rec(CorelShapeKind.Curve, nodes: 25),
            Rec(CorelShapeKind.Bitmap),
            Rec(CorelShapeKind.Text),
        });

        Assert.Equal(4, inv.TotalShapes);
        Assert.Equal(35, inv.TotalNodes);
        Assert.Equal(2, inv.CountByKind[CorelShapeKind.Curve]);
        Assert.Equal(1, inv.CountByKind[CorelShapeKind.Bitmap]);
        Assert.Equal(1, inv.CountByKind[CorelShapeKind.Text]);
    }

    /// <summary>
    /// The v1.0 failure mode: a design file is full of PowerClips and none of their contents were
    /// ever visited. The inventory MUST surface how much art lives inside them.
    /// </summary>
    [Fact]
    public void Tracks_shapes_found_inside_power_clips()
    {
        var inv = ShapeInventory.From(new[]
        {
            Rec(CorelShapeKind.Curve, nodes: 4),
            Rec(CorelShapeKind.Curve, nodes: 8, inPowerClip: true),
            Rec(CorelShapeKind.Bitmap, inPowerClip: true),
        });

        Assert.Equal(3, inv.TotalShapes);
        Assert.Equal(2, inv.ShapesInPowerClip);
        Assert.Equal(12, inv.TotalNodes);
    }

    [Fact]
    public void Counts_shapes_carrying_reducible_geometry()
    {
        var inv = ShapeInventory.From(new[]
        {
            Rec(CorelShapeKind.Curve, nodes: 10),
            Rec(CorelShapeKind.Rectangle),
            Rec(CorelShapeKind.Bitmap),
            Rec(CorelShapeKind.Curve, nodes: 3),
        });

        Assert.Equal(2, inv.ReducibleShapes);
    }

    /// <summary>A bitmap-heavy file must be visible as such straight from the inventory.</summary>
    [Fact]
    public void Reports_bitmap_count_for_strategy_selection()
    {
        var inv = ShapeInventory.From(new[]
        {
            Rec(CorelShapeKind.Bitmap),
            Rec(CorelShapeKind.Bitmap, inPowerClip: true),
            Rec(CorelShapeKind.Curve, nodes: 5),
        });

        Assert.Equal(2, inv.CountByKind[CorelShapeKind.Bitmap]);
    }
}
