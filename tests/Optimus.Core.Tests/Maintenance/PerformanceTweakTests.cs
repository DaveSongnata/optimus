using System;
using System.Collections.Generic;
using System.Linq;
using Optimus.Core.I18n;
using Optimus.Core.Maintenance;
using Xunit;

namespace Optimus.Core.Tests.Maintenance;

/// <summary>
/// Guards on the performance catalogue. The risk here is not a crash — it is scope creep into the folklore
/// that every "PC booster" ships, so most of these tests assert on what is deliberately ABSENT.
/// </summary>
public class PerformanceTweakTests
{
    [Fact]
    public void Every_tweak_has_an_id_a_label_and_a_real_explanation()
    {
        foreach (PerformanceTweak t in PerformanceTweaks.All())
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Id));
            Assert.False(string.IsNullOrWhiteSpace(t.Label));
            Assert.True(t.Description.Length > 60, t.Id + " precisa explicar o que muda");
            Assert.False(string.IsNullOrWhiteSpace(t.OptimizedValue), t.Id + " não diz no que fica");
        }
    }

    [Fact]
    public void Tweak_ids_are_unique()
    {
        var ids = PerformanceTweaks.All().Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    /// <summary>
    /// A tweak that gives something up has to SAY what — that is the whole product philosophy: the loss
    /// belongs in the label, not in a footnote.
    /// </summary>
    [Fact]
    public void Anything_that_needs_confirmation_states_its_tradeoff()
    {
        foreach (PerformanceTweak t in PerformanceTweaks.All())
            if (t.Risk != RiskLevel.Safe)
                Assert.True(t.Tradeoff.Length > 40, t.Id + " precisa declarar o que se troca");
    }

    /// <summary>The security trade must be named as such, not softened into a performance benefit.</summary>
    [Fact]
    public void The_antivirus_exclusion_names_the_security_tradeoff()
    {
        PerformanceTweak defender = PerformanceTweaks.All()
            .Single(t => t.Id == PerformanceTweaks.DefenderExclusions);

        Assert.Equal(RiskLevel.NeedsConfirmation, defender.Risk);
        Assert.Contains("SEGURANÇA", defender.Tradeoff, StringComparison.OrdinalIgnoreCase);
        Assert.False(defender.DefaultOn, "nunca marcado por padrão");
    }

    /// <summary>
    /// The animation tweak must promise that ClearType and thumbnails survive. Windows' own "best
    /// performance" preset kills both, and for a designer that is damage, not optimisation.
    /// </summary>
    [Fact]
    public void Turning_off_animations_promises_to_keep_cleartype_and_thumbnails()
    {
        PerformanceTweak animations = PerformanceTweaks.All()
            .Single(t => t.Id == PerformanceTweaks.Animations);

        Assert.Contains("ClearType", animations.Tradeoff);
        Assert.Contains("miniaturas", animations.Tradeoff, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The three staples of "PC booster" software stay out: service disabling, pagefile surgery and
    /// process-priority boosting. Same evidence bar as decision O6.
    /// </summary>
    [Fact]
    public void The_folklore_tweaks_are_absent()
    {
        var forbidden = new[]
        {
            "servi", "service",        // disabling Windows services
            "pagina", "pagefile", "swap",
            "prioridade", "priority",
            "ram", "prefetch", "superfetch",
        };

        foreach (PerformanceTweak t in PerformanceTweaks.All())
            foreach (string needle in forbidden)
                Assert.False(t.Id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0,
                             t.Id + " parece um ajuste de folclore");
    }

    /// <summary>Only genuinely free wins may be ticked by default.</summary>
    [Fact]
    public void Only_safe_tweaks_are_on_by_default()
    {
        foreach (PerformanceTweak t in PerformanceTweaks.All())
            if (t.DefaultOn) Assert.Equal(RiskLevel.Safe, t.Risk);
    }

    // ── state model ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TweakState.Default, true)]
    [InlineData(TweakState.Optimized, false)]
    [InlineData(TweakState.NotApplicable, false)]
    [InlineData(TweakState.Unknown, false)]
    public void Only_a_default_state_is_worth_offering(TweakState state, bool actionable)
    {
        Assert.Equal(actionable, new PerformanceTweak { State = state }.IsActionable);
    }

    /// <summary>
    /// A tweak that could not be READ must never be offered: "optimising" something we did not measure is
    /// the same defect as reporting a saving we did not achieve.
    /// </summary>
    [Fact]
    public void An_unreadable_tweak_is_never_actionable()
    {
        Assert.False(new PerformanceTweak { State = TweakState.Unknown }.IsActionable);
    }

    // ── translation ─────────────────────────────────────────────────────────────

    [Fact]
    public void Every_tweak_is_fully_translated_in_all_three_languages()
    {
        foreach (PerformanceTweak t in PerformanceTweaks.All())
            foreach (Language language in LocalizedStrings.Languages)
                foreach (string part in new[] { "label", "desc", "tradeoff" })
                {
                    string key = "mnt.tweak." + t.Id + "." + part;
                    Assert.True(LocalizedStrings.HasExplicit(language, key),
                                key + " falta em " + language);
                }
    }

    /// <summary>The "what we deliberately do not do" note is part of the product's honesty, in every language.</summary>
    [Fact]
    public void The_rejected_tweaks_note_exists_in_every_language()
    {
        foreach (Language language in LocalizedStrings.Languages)
            Assert.True(LocalizedStrings.Get(language, "mnt.perf.rejected").Length > 100);
    }
}
