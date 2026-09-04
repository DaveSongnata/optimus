using Optimus.Core.Diagnostics;
using Optimus.Core.Optimization;
using Xunit;

namespace Optimus.Core.Tests.Optimization;

public class CleanupRulesTests
{
    [Fact]
    public void An_empty_ordinary_layer_is_removable()
    {
        Assert.True(CleanupRules.IsRemovableLayer(new LayerInfo { Name = "Camada 1", ShapeCount = 0 }));
    }

    [Fact]
    public void A_layer_with_shapes_is_never_removable()
    {
        Assert.False(CleanupRules.IsRemovableLayer(new LayerInfo { Name = "Arte", ShapeCount = 1 }));
    }

    /// <summary>Guides/grid/master layers read as empty but are structural — deleting them damages
    /// the document.</summary>
    [Fact]
    public void A_special_layer_is_never_removable_even_when_empty()
    {
        Assert.False(CleanupRules.IsRemovableLayer(
            new LayerInfo { Name = "Guias", ShapeCount = 0, IsSpecial = true }));
    }

    [Fact]
    public void Null_layer_is_not_removable()
    {
        Assert.False(CleanupRules.IsRemovableLayer(null!));
    }

    // ── off-page detection ───────────────────────────────────────────────────────

    [Fact]
    public void A_shape_entirely_to_the_left_is_off_page()
    {
        Assert.True(CleanupRules.IsFullyOffPage(new BoundsMm(-100, 10, -50, 60), 210, 297));
    }

    [Fact]
    public void A_shape_entirely_above_is_off_page()
    {
        Assert.True(CleanupRules.IsFullyOffPage(new BoundsMm(10, 400, 60, 450), 210, 297));
    }

    [Fact]
    public void A_shape_inside_the_page_is_not_off_page()
    {
        Assert.False(CleanupRules.IsFullyOffPage(new BoundsMm(10, 10, 60, 60), 210, 297));
    }

    /// <summary>Bleed: a shape crossing the page edge must survive. Deleting it would cut artwork.</summary>
    [Fact]
    public void A_shape_crossing_the_edge_survives()
    {
        Assert.False(CleanupRules.IsFullyOffPage(new BoundsMm(-20, 10, 30, 60), 210, 297));
        Assert.False(CleanupRules.IsFullyOffPage(new BoundsMm(190, 10, 260, 60), 210, 297));
    }

    /// <summary>Art trimmed exactly to the page edge is inside, not stray.</summary>
    [Fact]
    public void A_shape_flush_with_the_edge_survives()
    {
        Assert.False(CleanupRules.IsFullyOffPage(new BoundsMm(0, 0, 210, 297), 210, 297));
    }

    [Fact]
    public void Unreadable_bounds_never_trigger_a_deletion()
    {
        Assert.False(CleanupRules.IsFullyOffPage(new BoundsMm(100, 100, 50, 50), 210, 297));
    }

    [Fact]
    public void A_degenerate_page_never_triggers_a_deletion()
    {
        Assert.False(CleanupRules.IsFullyOffPage(new BoundsMm(-100, -100, -50, -50), 0, 0));
    }
}

public class CompositionDeltaTests
{
    private static CdrComposition Comp(long total, params (CdrComponent, long)[] parts) =>
        CdrComposition.FromBytes(total, parts);

    [Fact]
    public void Reports_the_real_saving_per_component()
    {
        CdrComposition before = Comp(1_000_000, (CdrComponent.Preview, 100_000L), (CdrComponent.Vector, 880_000L));
        CdrComposition after = Comp(890_000, (CdrComponent.Preview, 0L), (CdrComponent.Vector, 880_000L));

        CompositionDelta d = CompositionDelta.Between(before, after);

        Assert.Equal(110_000, d.SavedBytes);
        Assert.Equal(11.0, d.SavedPercent);
        Assert.False(d.FileGrew);

        ComponentDelta preview = Assert.Single(d.Components, c => c.Component == CdrComponent.Preview);
        Assert.Equal(100_000, preview.SavedBytes);
        Assert.Equal(100.0, preview.SavedPercent);
    }

    /// <summary>
    /// THE regression for the v1.0 failure: the file came out bigger and nobody noticed. Growth is a
    /// first-class result, not a negative saving.
    /// </summary>
    [Fact]
    public void A_file_that_grew_is_reported_as_grown()
    {
        CdrComposition before = Comp(1_000_000, (CdrComponent.Vector, 990_000L));
        CdrComposition after = Comp(1_050_000, (CdrComponent.Vector, 1_040_000L));

        CompositionDelta d = CompositionDelta.Between(before, after);

        Assert.True(d.FileGrew);
        Assert.True(d.SavedBytes < 0);
    }

    [Fact]
    public void Components_present_only_before_or_only_after_are_both_reported()
    {
        CdrComposition before = Comp(1_000_000, (CdrComponent.IccProfile, 500_000L));
        CdrComposition after = Comp(500_000, (CdrComponent.Vector, 480_000L));

        CompositionDelta d = CompositionDelta.Between(before, after);

        Assert.Contains(d.Components, c => c.Component == CdrComponent.IccProfile && c.SavedBytes == 500_000);
        Assert.Contains(d.Components, c => c.Component == CdrComponent.Vector && c.SavedBytes == -480_000);
    }

    [Fact]
    public void Biggest_saving_is_listed_first()
    {
        CdrComposition before = Comp(1_000_000,
            (CdrComponent.IccProfile, 600_000L), (CdrComponent.Preview, 100_000L), (CdrComponent.Vector, 290_000L));
        CdrComposition after = Comp(300_000,
            (CdrComponent.IccProfile, 0L), (CdrComponent.Preview, 0L), (CdrComponent.Vector, 290_000L));

        CompositionDelta d = CompositionDelta.Between(before, after);

        Assert.Equal(CdrComponent.IccProfile, d.Components[0].Component);
    }

    [Fact]
    public void Null_input_does_not_throw()
    {
        CompositionDelta d = CompositionDelta.Between(null!, null!);
        Assert.Equal(0, d.SavedBytes);
        Assert.Empty(d.Components);
    }
}
