using Optimus.Core.Diagnostics;
using Xunit;

namespace Optimus.Core.Tests.Diagnostics;

/// <summary>
/// Effective DPI = pixels ÷ the size the image is actually PLACED at, which is the only figure that
/// says whether pixels are being wasted. Nominal DPI does not: a 4000 px image dropped into a 5 cm
/// box carries ~2000 effective DPI, and ~85% of those pixels can never be seen in print.
///
/// <para>This is what makes bitmap resampling honest — we only touch images that are ABOVE the
/// print target, and leave everything at or below it alone (O3).</para>
/// </summary>
public class EffectiveDpiTests
{
    [Fact]
    public void One_inch_of_300_pixels_is_300_dpi()
    {
        Assert.Equal(300, EffectiveDpi.Compute(pixels: 300, physicalMm: 25.4), precision: 3);
    }

    /// <summary>The waste case that motivates the whole feature.</summary>
    [Fact]
    public void Four_thousand_pixels_in_fifty_millimetres_is_massively_over_resolution()
    {
        double dpi = EffectiveDpi.Compute(pixels: 4000, physicalMm: 50);
        Assert.InRange(dpi, 2030, 2035);   // 4000 / (50/25.4) ≈ 2032
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(300, 0)]
    [InlineData(300, -5)]
    [InlineData(-300, 50)]
    public void Degenerate_input_yields_zero_not_infinity(int pixels, double mm)
    {
        Assert.Equal(0, EffectiveDpi.Compute(pixels, mm));
    }

    /// <summary>At the target, nothing is done — the image is already print-correct.</summary>
    [Fact]
    public void Image_at_the_target_is_not_a_candidate()
    {
        Assert.False(EffectiveDpi.NeedsResampling(effectiveDpi: 300, targetDpi: 300));
    }

    /// <summary>
    /// A 10% margin above the target prevents pointless work (and pointless quality loss) on images
    /// that are only fractionally over because of rounding in placement.
    /// </summary>
    [Fact]
    public void Slightly_over_the_target_is_within_tolerance()
    {
        Assert.False(EffectiveDpi.NeedsResampling(effectiveDpi: 320, targetDpi: 300));
        Assert.False(EffectiveDpi.NeedsResampling(effectiveDpi: 330, targetDpi: 300));
    }

    [Fact]
    public void Clearly_over_the_target_is_a_candidate()
    {
        Assert.True(EffectiveDpi.NeedsResampling(effectiveDpi: 600, targetDpi: 300));
        Assert.True(EffectiveDpi.NeedsResampling(effectiveDpi: 2032, targetDpi: 300));
    }

    [Fact]
    public void Under_the_target_is_never_touched()
    {
        Assert.False(EffectiveDpi.NeedsResampling(effectiveDpi: 150, targetDpi: 300));
        Assert.False(EffectiveDpi.NeedsResampling(effectiveDpi: 72, targetDpi: 300));
    }

    /// <summary>New pixel dimension for the target — the value handed to Bitmap.Resample.</summary>
    [Fact]
    public void Target_pixel_size_matches_the_physical_size_at_the_target_dpi()
    {
        Assert.Equal(591, EffectiveDpi.TargetPixels(physicalMm: 50, targetDpi: 300));   // 50/25.4*300
        Assert.Equal(300, EffectiveDpi.TargetPixels(physicalMm: 25.4, targetDpi: 300));
    }

    [Fact]
    public void Target_pixel_size_is_at_least_one()
    {
        Assert.True(EffectiveDpi.TargetPixels(physicalMm: 0.01, targetDpi: 300) >= 1);
    }

    /// <summary>
    /// Savings scale with AREA, not with linear resolution: halving DPI quarters the pixel count.
    /// Reporting it linearly would understate the gain by a lot.
    /// </summary>
    [Fact]
    public void Estimated_saving_scales_with_area()
    {
        double saving = EffectiveDpi.EstimatedByteSaving(1_000_000, effectiveDpi: 600, targetDpi: 300);
        Assert.InRange(saving, 740_000, 760_000);   // 1 - (300/600)^2 = 0.75
    }

    [Fact]
    public void No_saving_claimed_when_no_resampling_is_needed()
    {
        Assert.Equal(0, EffectiveDpi.EstimatedByteSaving(1_000_000, effectiveDpi: 300, targetDpi: 300));
        Assert.Equal(0, EffectiveDpi.EstimatedByteSaving(1_000_000, effectiveDpi: 150, targetDpi: 300));
    }
}
