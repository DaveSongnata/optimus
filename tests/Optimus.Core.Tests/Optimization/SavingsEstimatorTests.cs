using System;
using System.IO;
using System.Linq;
using Optimus.Core.Diagnostics;
using Optimus.Core.Optimization;
using Xunit;

namespace Optimus.Core.Tests.Optimization;

/// <summary>
/// The estimator's job is to tell the operator what a slider position will buy BEFORE it is applied.
/// Its hard rule: never promise a saving on a component the file does not contain.
/// </summary>
public class SavingsEstimatorTests
{
    private static string? Sample(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "docs", relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static SavingItem Item(SavingsEstimate e, string label) =>
        e.Items.First(i => i.Label == label);

    // ── the "do not promise what is not there" rule ───────────────────────────────

    /// <summary>
    /// `kaneki.cdr` — the operator's actual working file — has NO embedded profile. The colour slider
    /// must report itself inapplicable, with a reason, instead of implying a win.
    /// </summary>
    [Fact]
    public void Colour_slider_is_inapplicable_when_the_file_has_no_profile()
    {
        var noIcc = CdrComposition.FromBytes(1_000_000, new[]
        {
            (CdrComponent.Vector, 900_000L),
            (CdrComponent.Preview, 90_000L),
        });

        SavingsEstimate e = SavingsEstimator.Estimate(
            noIcc, new OptimizationSettings { Color = ColorFidelity.NoProfile });

        SavingItem colour = Item(e, "Perfil de cor");
        Assert.False(colour.Applicable);
        Assert.Equal(0, colour.Bytes);
        Assert.NotEqual("", colour.NotApplicableReason);
    }

    [Fact]
    public void Image_slider_is_inapplicable_when_the_file_has_no_images()
    {
        var noRaster = CdrComposition.FromBytes(1_000_000, new[] { (CdrComponent.Vector, 950_000L) });

        SavingsEstimate e = SavingsEstimator.Estimate(
            noRaster, new OptimizationSettings { Image = ImageQuality.Screen });

        Assert.False(Item(e, "Imagens").Applicable);
    }

    // ── the ICC-dominated case: the biggest win in the product ────────────────────

    [Fact]
    public void Dropping_the_profile_on_an_icc_dominated_file_estimates_a_huge_saving()
    {
        var iccHeavy = CdrComposition.FromBytes(1_405_181, new[]
        {
            (CdrComponent.IccProfile, 1_369_722L),
            (CdrComponent.Vector, 11_820L),
            (CdrComponent.Preview, 12_068L),
        });

        SavingsEstimate e = SavingsEstimator.Estimate(
            iccHeavy, new OptimizationSettings { Color = ColorFidelity.NoProfile });

        Assert.True(Item(e, "Perfil de cor").Applicable);
        Assert.True(e.TotalPercent > 90, $"expected a dominant saving, got {e.TotalPercent}%");
        Assert.True(e.AnyLossy, "dropping the profile is lossy and must be flagged");
    }

    [Fact]
    public void Economical_colour_saves_less_than_dropping_the_profile()
    {
        var iccHeavy = CdrComposition.FromBytes(1_405_181, new[]
        {
            (CdrComponent.IccProfile, 1_369_722L),
            (CdrComponent.Vector, 11_820L),
        });

        long economical = SavingsEstimator
            .Estimate(iccHeavy, new OptimizationSettings { Color = ColorFidelity.Economical }).TotalBytes;
        long dropped = SavingsEstimator
            .Estimate(iccHeavy, new OptimizationSettings { Color = ColorFidelity.NoProfile }).TotalBytes;

        Assert.True(economical < dropped);
        Assert.True(economical > 0);
    }

    /// <summary>Keeping colour at maximum must cost nothing and claim nothing.</summary>
    [Fact]
    public void Maximum_fidelity_claims_no_colour_saving()
    {
        var iccHeavy = CdrComposition.FromBytes(1_405_181, new[] { (CdrComponent.IccProfile, 1_369_722L) });

        SavingsEstimate e = SavingsEstimator.Estimate(
            iccHeavy, new OptimizationSettings { Color = ColorFidelity.Maximum });

        Assert.Equal(0, Item(e, "Perfil de cor").Bytes);
    }

    // ── invariants that keep the number honest ───────────────────────────────────

    [Fact]
    public void Never_estimates_more_than_the_file_contains()
    {
        var comp = CdrComposition.FromBytes(100_000, new[]
        {
            (CdrComponent.IccProfile, 50_000L),
            (CdrComponent.Raster, 30_000L),
            (CdrComponent.Preview, 10_000L),
            (CdrComponent.Vector, 8_000L),
            (CdrComponent.Metadata, 2_000L),
        });

        SavingsEstimate e = SavingsEstimator.Estimate(comp, OptimizationSettings.Preset(OptimizationPreset.MaxSavings));

        Assert.True(e.TotalBytes <= comp.TotalBytes);
        Assert.InRange(e.TotalPercent, 0, 100);
        Assert.Equal(comp.TotalBytes - e.TotalBytes, e.ProjectedBytes);
    }

    [Fact]
    public void Safe_preset_never_reports_a_lossy_saving()
    {
        var comp = CdrComposition.FromBytes(100_000, new[]
        {
            (CdrComponent.IccProfile, 50_000L),
            (CdrComponent.Raster, 30_000L),
            (CdrComponent.Preview, 10_000L),
            (CdrComponent.Vector, 8_000L),
        });

        SavingsEstimate e = SavingsEstimator.Estimate(comp, OptimizationSettings.Preset(OptimizationPreset.Safe));

        Assert.False(e.AnyLossy);
        Assert.True(e.TotalBytes > 0, "the preview alone is a free win");
    }

    [Fact]
    public void Max_savings_estimates_more_than_safe()
    {
        var comp = CdrComposition.FromBytes(1_000_000, new[]
        {
            (CdrComponent.IccProfile, 400_000L),
            (CdrComponent.Raster, 400_000L),
            (CdrComponent.Preview, 100_000L),
            (CdrComponent.Vector, 100_000L),
        });

        long safe = SavingsEstimator.Estimate(comp, OptimizationSettings.Preset(OptimizationPreset.Safe)).TotalBytes;
        long max = SavingsEstimator.Estimate(comp, OptimizationSettings.Preset(OptimizationPreset.MaxSavings)).TotalBytes;

        Assert.True(max > safe);
    }

    [Fact]
    public void Unavailable_composition_yields_an_empty_estimate()
    {
        SavingsEstimate e = SavingsEstimator.Estimate(CdrComposition.Unavailable(), new OptimizationSettings());
        Assert.Empty(e.Items);
        Assert.Equal(0, e.TotalBytes);
    }

    // ── against the operator's real working file ─────────────────────────────────

    /// <summary>
    /// End-to-end on `kaneki.cdr`: measured composition → estimate. It has no profile, so the honest
    /// answer for THIS file is that colour buys nothing and the free wins are small.
    /// </summary>
    [Fact]
    public void Real_working_file_estimate_is_honest_about_having_no_profile()
    {
        string? path = Sample(Path.Combine("otimizacoes_arquivos", "kaneki.cdr"));
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path);
        Assert.True(r.Composition.Available, r.Note);

        SavingsEstimate e = SavingsEstimator.Estimate(
            r.Composition, OptimizationSettings.Preset(OptimizationPreset.MaxSavings));

        // No embedded profile in this file — the slider must not claim anything.
        Assert.False(Item(e, "Perfil de cor").Applicable);

        // The preview IS there and is a free win (~2% of this file).
        SavingItem preview = Item(e, "Miniatura embutida");
        Assert.True(preview.Applicable);
        Assert.True(preview.Bytes > 100_000, $"preview should be ~163 KB, got {preview.Bytes}");
        Assert.False(preview.Lossy);

        // And the total must stay modest — this file's weight is object payload, not removable parts.
        Assert.InRange(e.TotalPercent, 1, 30);
    }
}
