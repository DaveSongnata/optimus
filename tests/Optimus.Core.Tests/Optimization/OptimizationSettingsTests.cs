using System.Linq;
using Optimus.Core.Optimization;
using Xunit;

namespace Optimus.Core.Tests.Optimization;

/// <summary>
/// The four sliders that make the product a "remap" rather than a magic button. The operator picks
/// the trade-off; the product never decides for them, and every lossy position states its cost in
/// the label (Davi 2026-07-25).
/// </summary>
public class OptimizationSettingsTests
{
    // ── defaults: the safe answer for someone who does not want to think ──────────

    [Fact]
    public void Default_settings_apply_no_loss()
    {
        var s = new OptimizationSettings();

        Assert.False(s.HasDeclaredLoss);
        Assert.Empty(s.LossWarnings);
        Assert.Equal(ColorFidelity.High, s.Color);
        Assert.Equal(ImageQuality.Untouched, s.Image);
        Assert.Equal(ColorSpaceTarget.Keep, s.Space);
    }

    /// <summary>Lossless gains are on by default — there is no reason to leave free savings behind.</summary>
    [Fact]
    public void Lossless_switches_are_on_by_default()
    {
        var s = new OptimizationSettings();

        Assert.True(s.RemovePreview);
        Assert.True(s.RemoveEmptyLayers);
        Assert.True(s.RemoveCompatibilityData);
        Assert.True(s.Reserialize);
        Assert.False(s.RemoveOffPage);   // can delete the operator's parked notes
    }

    // ── colour fidelity ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ColorFidelity.Maximum, true)]
    [InlineData(ColorFidelity.High, true)]
    [InlineData(ColorFidelity.Economical, true)]
    [InlineData(ColorFidelity.NoProfile, false)]
    public void Only_the_last_colour_position_drops_the_profile(ColorFidelity f, bool embeds)
    {
        var s = new OptimizationSettings { Color = f };
        Assert.Equal(embeds, s.EmbedColorProfile);
    }

    /// <summary>
    /// Dropping the profile changes printed colour, so it must be announced — measured: 29 of 29 real
    /// files actually reference the profile they embed, so this is never a free cleanup.
    /// </summary>
    [Fact]
    public void Dropping_the_profile_declares_the_loss()
    {
        var s = new OptimizationSettings { Color = ColorFidelity.NoProfile };

        Assert.True(s.HasDeclaredLoss);
        Assert.Contains(s.LossWarnings, w => w.IndexOf("cor", System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    // ── image quality → effective DPI target ─────────────────────────────────────

    [Theory]
    [InlineData(ImageQuality.Untouched, null)]
    [InlineData(ImageQuality.FinePrint, 300)]
    [InlineData(ImageQuality.LargeFormat, 200)]
    [InlineData(ImageQuality.Screen, 150)]
    public void Image_quality_maps_to_a_dpi_target(ImageQuality q, int? dpi)
    {
        Assert.Equal(dpi, new OptimizationSettings { Image = q }.TargetDpi);
    }

    [Fact]
    public void Touching_images_declares_the_loss_and_leaving_them_does_not()
    {
        Assert.False(new OptimizationSettings { Image = ImageQuality.Untouched }.HasDeclaredLoss);
        Assert.True(new OptimizationSettings { Image = ImageQuality.Screen }.HasDeclaredLoss);
    }

    // ── drawing weight → node tolerance in millimetres ──────────────────────────

    [Theory]
    [InlineData(DrawingWeight.PreserveDetail, 0.01)]
    [InlineData(DrawingWeight.Balanced, 0.03)]
    [InlineData(DrawingWeight.MaxFluidity, 0.08)]
    public void Drawing_weight_maps_to_a_millimetre_tolerance(DrawingWeight w, double mm)
    {
        Assert.Equal(mm, new OptimizationSettings { Weight = w }.ToleranceMm!.Value, precision: 4);
    }

    /// <summary>
    /// "Untouched" must mean literally no change to the curves — this position exists because a test
    /// caught the Safe preset still simplifying geometry, which made its promise false.
    /// </summary>
    [Fact]
    public void Untouched_drawing_means_no_tolerance_at_all()
    {
        var s = new OptimizationSettings { Weight = DrawingWeight.Untouched };
        Assert.Null(s.ToleranceMm);
        Assert.False(s.SimplifyDrawing);
    }

    /// <summary>The tolerance is a PHYSICAL distance — the unit bug that shipped in v1.0 came from
    /// treating it as unitless, so the contract is pinned here.</summary>
    [Fact]
    public void Tolerance_is_always_in_millimetres_and_positive_when_present()
    {
        foreach (DrawingWeight w in System.Enum.GetValues(typeof(DrawingWeight)).Cast<DrawingWeight>())
        {
            double? mm = new OptimizationSettings { Weight = w }.ToleranceMm;
            if (mm.HasValue) Assert.InRange(mm.Value, 0.001, 1.0);
        }
    }

    // ── colour space conversion: the dangerous one ───────────────────────────────

    [Fact]
    public void Converting_colour_space_requires_confirmation()
    {
        Assert.False(new OptimizationSettings { Space = ColorSpaceTarget.Keep }.RequiresConfirmation);
        Assert.True(new OptimizationSettings { Space = ColorSpaceTarget.Cmyk }.RequiresConfirmation);
        Assert.True(new OptimizationSettings { Space = ColorSpaceTarget.Rgb }.RequiresConfirmation);
    }

    [Fact]
    public void Converting_colour_space_declares_the_loss()
    {
        var s = new OptimizationSettings { Space = ColorSpaceTarget.Cmyk };
        Assert.True(s.HasDeclaredLoss);
        Assert.NotEmpty(s.LossWarnings);
    }

    // ── presets ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Safe_preset_applies_no_loss_at_all()
    {
        OptimizationSettings s = OptimizationSettings.Preset(OptimizationPreset.Safe);

        Assert.False(s.HasDeclaredLoss);
        Assert.False(s.RequiresConfirmation);
        Assert.True(s.EmbedColorProfile);
        Assert.Null(s.TargetDpi);
        Assert.True(s.RemovePreview);   // free
    }

    [Fact]
    public void Balanced_preset_is_the_default()
    {
        OptimizationSettings s = OptimizationSettings.Preset(OptimizationPreset.Balanced);
        var d = new OptimizationSettings();

        Assert.Equal(d.Color, s.Color);
        Assert.Equal(d.Image, s.Image);
        Assert.Equal(d.Space, s.Space);
    }

    /// <summary>Max savings is allowed to hurt — but it must SAY so, on every count.</summary>
    [Fact]
    public void Max_savings_preset_declares_every_loss_it_incurs()
    {
        OptimizationSettings s = OptimizationSettings.Preset(OptimizationPreset.MaxSavings);

        Assert.True(s.HasDeclaredLoss);
        Assert.False(s.EmbedColorProfile);
        Assert.NotNull(s.TargetDpi);
        Assert.True(s.LossWarnings.Count >= 2);
    }

    /// <summary>Never convert colour space silently, not even at maximum savings.</summary>
    [Fact]
    public void Max_savings_still_does_not_convert_colour_space_by_itself()
    {
        Assert.Equal(ColorSpaceTarget.Keep, OptimizationSettings.Preset(OptimizationPreset.MaxSavings).Space);
    }

    /// <summary>Sliders are independent: moving one must not disturb another.</summary>
    [Fact]
    public void Sliders_are_independent()
    {
        var s = OptimizationSettings.Preset(OptimizationPreset.Safe);
        s.Weight = DrawingWeight.MaxFluidity;

        Assert.Equal(ColorFidelity.Maximum, s.Color);
        Assert.Equal(ImageQuality.Untouched, s.Image);
        Assert.Equal(0.08, s.ToleranceMm!.Value, precision: 4);
    }
}
