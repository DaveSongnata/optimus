using System;
using System.IO;
using System.Linq;
using Optimus.Core.Diagnostics;
using Xunit;

namespace Optimus.Core.Tests.Diagnostics;

/// <summary>
/// Locks down the correction that mattered most in Phase 2.
///
/// <para>
/// The first implementation reported "no ICC profile" because it scanned the document streams for
/// the ICC header signature. In a modern .cdr the profile is not in the streams at all — it is a
/// plain ZIP entry under <c>color/profiles/</c>. Worse, the classifier's generic <c>color/</c> rule
/// filed a 1.37 MB profile as "metadata". On a real client file that is <b>97.3% of the bytes</b>
/// reported in the wrong bucket, which would have made the size report useless for exactly the
/// files where the biggest win exists.
/// </para>
/// </summary>
public class IccAndFontsTests
{
    private static string? Sample(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "docs", "Arquivos_teste", name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // ── classification ────────────────────────────────────────────────────────────

    /// <summary>
    /// The ordering bug: <c>color/profiles/…</c> must be ICC, NOT metadata. It has to be tested
    /// before the generic <c>color/</c> rule, and this test fails if that order is ever reversed.
    /// </summary>
    [Theory]
    [InlineData("color/profiles/cmyk/isocoated_v2_eci.icc")]
    [InlineData("color/profiles/rgb/srgb color space profile.icm")]
    [InlineData("color/profiles/grayscale/dot gain 20%.icc")]
    public void Icc_profiles_are_their_own_component_not_metadata(string path)
    {
        Assert.Equal(CdrComponent.IccProfile, CdrEntryClassifier.Classify(path));
    }

    [Theory]
    [InlineData("color/color.xml")]
    [InlineData("color/docPalette.xml")]
    public void Other_color_entries_remain_metadata(string path)
    {
        Assert.Equal(CdrComponent.Metadata, CdrEntryClassifier.Classify(path));
    }

    // ── embedded-font probe ───────────────────────────────────────────────────────

    /// <summary>Header verified across ten real blobs: u32=1, u32=0x00000803, kind, UTF-16LE text.</summary>
    [Fact]
    public void Recognises_the_corel_font_embedding_header()
    {
        byte[] head =
        {
            0x01, 0x00, 0x00, 0x00,
            0x03, 0x08, 0x00, 0x00,
            0x02,
            (byte)'V', 0x00, (byte)'e', 0x00,
        };
        Assert.True(EmbeddedFontProbe.LooksLikeFontEmbedding(head));
    }

    [Fact]
    public void Rejects_payloads_that_are_not_font_embeddings()
    {
        Assert.False(EmbeddedFontProbe.LooksLikeFontEmbedding(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0, 0, 0, 0, 0 }));
        Assert.False(EmbeddedFontProbe.LooksLikeFontEmbedding(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.False(EmbeddedFontProbe.LooksLikeFontEmbedding(new byte[0]));
        Assert.False(EmbeddedFontProbe.LooksLikeFontEmbedding(null!));
    }

    // ── colour context: embedded vs actually used ─────────────────────────────────

    [Fact]
    public void Parses_embedded_profiles_and_object_usage()
    {
        const string xml =
            "<Color><ColorContext>" +
            "<ColorProfile id=\"Rgb\">sRGB IEC61966-2.1</ColorProfile>" +
            "<ColorProfile id=\"Cmyk\">ISO Coated v2 (ECI)</ColorProfile>" +
            "<ColorModel>Cmyk</ColorModel><RenderingIntent>RelativeColorimetric</RenderingIntent>" +
            "</ColorContext><HasRgbObjects>true</HasRgbObjects>" +
            "<HasCmykObjects>true</HasCmykObjects>" +
            "<HasGrayscaleObjects>false</HasGrayscaleObjects></Color>";

        ColorContext ctx = ColorContextReader.Parse(xml);

        Assert.True(ctx.HasProfile("Cmyk"));
        Assert.Equal("ISO Coated v2 (ECI)", ctx.Profiles["Cmyk"]);
        Assert.Equal("Cmyk", ctx.ColorModel);
        Assert.True(ctx.HasCmykObjects);
        Assert.False(ctx.HasGrayscaleObjects);
    }

    /// <summary>
    /// An embedded profile for a space the document does not use is the only arguably-free removal.
    /// Rare in practice — 29 of 29 real files used the space they embedded — so the product must not
    /// present ICC removal as free.
    /// </summary>
    [Fact]
    public void Detects_a_profile_embedded_for_an_unused_space()
    {
        ColorContext ctx = ColorContextReader.Parse(
            "<Color><ColorContext><ColorProfile id=\"Cmyk\">X</ColorProfile></ColorContext>" +
            "<HasCmykObjects>false</HasCmykObjects></Color>");

        Assert.True(ctx.IsProfileUnused("Cmyk"));
    }

    [Fact]
    public void A_used_profile_is_not_reported_as_unused()
    {
        ColorContext ctx = ColorContextReader.Parse(
            "<Color><ColorContext><ColorProfile id=\"Cmyk\">X</ColorProfile></ColorContext>" +
            "<HasCmykObjects>true</HasCmykObjects></Color>");

        Assert.False(ctx.IsProfileUnused("Cmyk"));
    }

    [Fact]
    public void A_file_with_no_embedded_profile_reports_none()
    {
        ColorContext ctx = ColorContextReader.Parse(
            "<Color><ColorContext><ColorModel>Rgb</ColorModel></ColorContext>" +
            "<HasRgbObjects>true</HasRgbObjects></Color>");

        Assert.False(ctx.HasProfile("Cmyk"));
        Assert.False(ctx.IsProfileUnused("Cmyk"));
    }

    [Fact]
    public void Malformed_or_empty_color_xml_does_not_throw()
    {
        Assert.False(ColorContextReader.Parse("").HasProfile("Cmyk"));
        Assert.False(ColorContextReader.Parse("<Color><ColorProfile id=").HasProfile("Cmyk"));
        Assert.False(ColorContextReader.Parse(null!).HasProfile("Cmyk"));
    }

    // ── the real ICC-dominated client files ───────────────────────────────────────

    /// <summary>Guards against a vacuous green if the fixtures go missing.</summary>
    [Fact]
    public void Icc_fixtures_are_present()
    {
        Assert.NotNull(Sample("icc-heavy.cdr"));
        Assert.NotNull(Sample("icc-heavy2.cdr"));
    }

    /// <summary>
    /// `icc-heavy.cdr`: 1.367.132 of 1.405.181 bytes is one CMYK profile, while the whole drawing is
    /// 0.9%. If this ever reports as metadata again, the report is lying.
    /// </summary>
    [Fact]
    public void Icc_dominated_file_is_measured_as_icc_dominated()
    {
        string? path = Sample("icc-heavy.cdr");
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path);

        Assert.True(r.Composition.Available, r.Note);
        Assert.InRange(r.Composition.PercentOf(CdrComponent.IccProfile), 90, 99.5);
        Assert.True(r.Composition.PercentOf(CdrComponent.Metadata) < 5,
            "the profile must NOT be counted as metadata");
        Assert.True(r.IccBytesFound > 1_000_000);
        Assert.NotEmpty(r.IccProfileNames);

        ReductionCeiling c = ReductionCeiling.For(r.Composition);
        Assert.Equal("icc", c.DominantLever);
        Assert.True(c.IccPercent > 90);
        // The huge win is NOT lossless: the profile is referenced by the document's objects.
        Assert.True(c.LosslessPercent < 10);
        Assert.True(c.CeilingWithoutRasterPercent > 50);
    }

    [Fact]
    public void Second_icc_file_confirms_the_pattern()
    {
        string? path = Sample("icc-heavy2.cdr");
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path);
        Assert.InRange(r.Composition.PercentOf(CdrComponent.IccProfile), 85, 99);
        Assert.Equal("icc", ReductionCeiling.For(r.Composition).DominantLever);
    }

    /// <summary>The colour context is read from the real file and shows the profile IS referenced —
    /// which is why the UI must call this a trade-off, not a cleanup.</summary>
    [Fact]
    public void Real_file_shows_the_embedded_profile_is_actually_used()
    {
        string? path = Sample("icc-heavy.cdr");
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path);

        Assert.True(r.ColorContext.HasProfile("Cmyk"));
        Assert.True(r.ColorContext.HasCmykObjects);
        Assert.False(r.ColorContext.IsProfileUnused("Cmyk"));
    }

    /// <summary>Preview PNGs are STORED — incompressible, so they are pure overhead worth naming.</summary>
    [Fact]
    public void Preview_entries_are_stored_uncompressed()
    {
        string? path = Sample("icc-heavy.cdr");
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path);
        CdrContainerReader.EntryInfo? preview =
            r.Entries.FirstOrDefault(e => e.Component == CdrComponent.Preview);

        Assert.NotNull(preview);
        Assert.True(preview!.Stored, "previews are stored with no compression in real files");
    }

    /// <summary>The Optimus test files genuinely have NO profile — the earlier conclusion was right
    /// for them, and wrong only as a generalisation. Both facts are now locked.</summary>
    [Fact]
    public void Optimus_sample_files_still_have_no_icc()
    {
        foreach (string name in new[] { "arquivo.cdr", "arquivo2.cdr" })
        {
            string? path = Sample(name);
            if (path == null) continue;

            CdrContainerReader.Result r = new CdrContainerReader().Read(path);
            Assert.Equal(0, r.Composition.Of(CdrComponent.IccProfile));
        }
    }
}
