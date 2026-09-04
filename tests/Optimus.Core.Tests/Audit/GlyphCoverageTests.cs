using System.Collections.Generic;
using System.Linq;
using Optimus.Core.Audit;
using Xunit;

namespace Optimus.Core.Tests.Audit;

/// <summary>
/// The feature that stops a job printing hollow boxes instead of "ç". Both failure directions matter:
/// a false "sem acento" teaches the operator to ignore warnings, and a false OK lets the job go to
/// press broken.
/// </summary>
public class GlyphCoverageTests
{
    private static System.Func<int, bool> Covers(params int[] missing)
    {
        var absent = new HashSet<int>(missing);
        return cp => !absent.Contains(cp);
    }

    // ── the charset itself ───────────────────────────────────────────────────────

    [Fact]
    public void Required_tier_holds_the_24_portuguese_accented_letters()
    {
        Assert.Equal(24, PtBrCharset.Required.Length);
        Assert.Contains(0x00E7, PtBrCharset.Required);   // ç
        Assert.Contains(0x00E3, PtBrCharset.Required);   // ã
        Assert.Contains(0x00C7, PtBrCharset.Required);   // Ç
    }

    /// <summary>Tiers must not overlap, or a miss would be counted twice with two severities.</summary>
    [Fact]
    public void Tiers_do_not_overlap()
    {
        var all = new List<int>();
        all.AddRange(PtBrCharset.Required);
        all.AddRange(PtBrCharset.Warn);
        all.AddRange(PtBrCharset.Info);
        all.AddRange(PtBrCharset.Combining);

        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Combining_marks_are_in_their_own_tier()
    {
        foreach (int cp in PtBrCharset.Combining)
        {
            Assert.DoesNotContain(cp, PtBrCharset.Required);
            Assert.InRange(cp, 0x0300, 0x036F);
        }
    }

    [Fact]
    public void Every_codepoint_has_a_human_description()
    {
        foreach ((int cp, CharTier _) in PtBrCharset.All())
            Assert.False(string.IsNullOrWhiteSpace(PtBrCharset.Describe(cp)));
    }

    // ── verdicts ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_complete_font_passes()
    {
        GlyphCoverage c = GlyphVerdict.Evaluate("Arial", false, Covers());

        Assert.Equal(FontVerdict.Ok, c.Verdict);
        Assert.Empty(c.MissingRequired);
        Assert.True(c.IsUsable);
    }

    /// <summary>THE case the feature exists for: a font without "ç".</summary>
    [Fact]
    public void A_font_missing_a_cedilla_fails_and_names_it()
    {
        GlyphCoverage c = GlyphVerdict.Evaluate("Fonte Bonita", false, Covers(0x00E7));

        Assert.Equal(FontVerdict.Failed, c.Verdict);
        Assert.Contains(0x00E7, c.MissingRequired);
        Assert.False(c.IsUsable);
        Assert.Contains("ç", c.Summary());
    }

    [Fact]
    public void A_font_missing_a_tilde_fails()
    {
        GlyphCoverage c = GlyphVerdict.Evaluate("Custom", false, Covers(0x00E3, 0x00F5));

        Assert.Equal(FontVerdict.Failed, c.Verdict);
        Assert.Equal(2, c.MissingRequired.Count);
    }

    /// <summary>Missing punctuation warns; it must NOT fail the font.</summary>
    [Fact]
    public void Missing_typographic_punctuation_only_warns()
    {
        GlyphCoverage c = GlyphVerdict.Evaluate("Impact", false, Covers(0x201C, 0x201D, 0x2014));

        Assert.Equal(FontVerdict.Warning, c.Verdict);
        Assert.Empty(c.MissingRequired);
        Assert.True(c.IsUsable);
    }

    /// <summary>Measured: "€" was absent in 8 of 76 real fonts. Failing on it would be noise.</summary>
    [Fact]
    public void Missing_euro_does_not_even_warn()
    {
        GlyphCoverage c = GlyphVerdict.Evaluate("Visitor TT1 BRK", false, Covers(0x20AC));

        Assert.Equal(FontVerdict.Ok, c.Verdict);
        Assert.Contains(0x20AC, c.MissingInfo);
    }

    /// <summary>
    /// Impact, Segoe UI Emoji, Ink Free and Sylfaen all lack combining marks with perfect precomposed
    /// coverage. NFC Portuguese never needs them, so failing here would be simply wrong.
    /// </summary>
    [Fact]
    public void Missing_combining_marks_never_affect_the_verdict()
    {
        GlyphCoverage c = GlyphVerdict.Evaluate("Impact", false, Covers(PtBrCharset.Combining));

        Assert.Equal(FontVerdict.Ok, c.Verdict);
        Assert.Equal(PtBrCharset.Combining.Length, c.MissingCombining.Count);
    }

    /// <summary>
    /// Wingdings answers "yes" for U+00E7 because symbol cmaps alias U+F0xx onto U+00xx. Without this
    /// guard the report would call a dingbat font Portuguese-ready.
    /// </summary>
    [Fact]
    public void A_symbol_font_is_flagged_not_judged()
    {
        GlyphCoverage c = GlyphVerdict.Evaluate("Wingdings", true, Covers());

        Assert.Equal(FontVerdict.SymbolFont, c.Verdict);
        Assert.Empty(c.MissingRequired);
        Assert.Contains("símbolos", c.Summary());
    }

    [Fact]
    public void A_font_that_is_not_installed_is_reported_as_such()
    {
        GlyphCoverage c = GlyphVerdict.Evaluate("Man City Dragon 2324", false, null!);

        Assert.Equal(FontVerdict.NotInstalled, c.Verdict);
        Assert.Contains("NÃO INSTALADA", c.Summary());
    }

    /// <summary>
    /// The silent-substitution case: the document asks for one font and the machine prints another.
    /// The summary must name the replacement so the operator can see what will actually print.
    /// </summary>
    [Fact]
    public void A_substituted_font_names_its_replacement()
    {
        var c = new GlyphCoverage
        {
            FontName = "Arial Narrow",
            Verdict = FontVerdict.NotInstalled,
            SubstitutedBy = "Arial",
        };

        Assert.Contains("Arial", c.Summary());
        Assert.Contains("substituída", c.Summary());
    }

    [Fact]
    public void The_summary_lists_at_most_a_handful_and_counts_the_rest()
    {
        GlyphCoverage c = GlyphVerdict.Evaluate("Quebrada", false, Covers(PtBrCharset.Required));

        Assert.Equal(FontVerdict.Failed, c.Verdict);
        Assert.Equal(24, c.MissingRequired.Count);
        Assert.Contains("+18", c.Summary());   // shows 6, counts the other 18
    }
}
