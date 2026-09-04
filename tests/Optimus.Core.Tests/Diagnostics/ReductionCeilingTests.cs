using Optimus.Core.Diagnostics;
using Xunit;

namespace Optimus.Core.Tests.Diagnostics;

/// <summary>
/// The honest ceiling. This is the class that stops the product promising "50%" on a file where
/// 50% is arithmetically impossible — the requirement the client's own files forced (O4/P1).
/// </summary>
public class ReductionCeilingTests
{
    /// <summary>Composition of the real client file `arquivo.cdr` (measured on disk).</summary>
    private static CdrComposition BitmapHeavy() => CdrComposition.FromBytes(
        totalBytes: 4_529_421,
        new[]
        {
            (CdrComponent.Raster, 2_858_062L),
            (CdrComponent.Vector, 1_568_303L),
            (CdrComponent.Preview, 83_604L),
            (CdrComponent.Metadata, 5_590L),
        });

    /// <summary>Composition of the real client file `arquivo2.cdr` (measured on disk).</summary>
    private static CdrComposition VectorHeavy() => CdrComposition.FromBytes(
        totalBytes: 7_910_283,
        new[]
        {
            (CdrComponent.Vector, 7_646_361L),
            (CdrComponent.Preview, 129_193L),
            (CdrComponent.Embedded, 124_671L),
            (CdrComponent.Metadata, 7_284L),
            (CdrComponent.Fonts, 298L),
        });

    [Fact]
    public void Lossless_ceiling_never_includes_raster()
    {
        ReductionCeiling c = ReductionCeiling.For(BitmapHeavy());

        // Preview + metadata are removable without touching artwork; raster is NOT lossless.
        Assert.True(c.LosslessPercent > 0);
        Assert.True(c.LosslessPercent < 10,
            $"lossless ceiling should be small on this file, got {c.LosslessPercent}");
    }

    /// <summary>
    /// The headline finding from the client's own file: 63% of it is raster, so even deleting ALL
    /// geometry leaves ~37%. Claiming 50% without resampling would be a lie.
    /// </summary>
    [Fact]
    public void Bitmap_heavy_file_cannot_reach_50_percent_without_resampling()
    {
        ReductionCeiling c = ReductionCeiling.For(BitmapHeavy());

        Assert.True(c.RasterDominated);
        Assert.True(c.CeilingWithoutRasterPercent < 50,
            $"ceiling without touching raster must be under 50%, got {c.CeilingWithoutRasterPercent}");
        Assert.True(c.FiftyPercentRequiresResampling);
    }

    [Fact]
    public void Vector_heavy_file_is_not_raster_dominated()
    {
        ReductionCeiling c = ReductionCeiling.For(VectorHeavy());

        Assert.False(c.RasterDominated);
        Assert.False(c.FiftyPercentRequiresResampling);
    }

    /// <summary>
    /// Node reduction is worth ~2% of file bytes (9 bytes per node; coordinates ≈4.6% of the
    /// document). The ceiling must reflect that, so nobody sells curve work as a size win (O8).
    /// </summary>
    [Fact]
    public void Node_reduction_contributes_only_a_few_percent()
    {
        ReductionCeiling c = ReductionCeiling.For(VectorHeavy());

        Assert.True(c.NodeReductionPercent <= 5,
            $"halving nodes must not be presented as a large size win, got {c.NodeReductionPercent}");
    }

    [Fact]
    public void Resampling_ceiling_is_at_least_the_lossless_ceiling()
    {
        foreach (CdrComposition comp in new[] { BitmapHeavy(), VectorHeavy() })
        {
            ReductionCeiling c = ReductionCeiling.For(comp);
            Assert.True(c.CeilingWithResamplingPercent >= c.CeilingWithoutRasterPercent);
        }
    }

    [Fact]
    public void Empty_composition_yields_zero_and_does_not_divide_by_zero()
    {
        ReductionCeiling c = ReductionCeiling.For(
            CdrComposition.FromBytes(0, new (CdrComponent, long)[0]));

        Assert.Equal(0, c.LosslessPercent);
        Assert.Equal(0, c.CeilingWithoutRasterPercent);
        Assert.Equal(0, c.CeilingWithResamplingPercent);
        Assert.False(c.RasterDominated);
    }

    [Fact]
    public void Percentages_are_never_negative_nor_above_100()
    {
        foreach (CdrComposition comp in new[] { BitmapHeavy(), VectorHeavy() })
        {
            ReductionCeiling c = ReductionCeiling.For(comp);
            foreach (double p in new[] { c.LosslessPercent, c.CeilingWithoutRasterPercent,
                                         c.CeilingWithResamplingPercent, c.NodeReductionPercent })
            {
                Assert.InRange(p, 0, 100);
            }
        }
    }
}
