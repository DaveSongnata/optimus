using Optimus.Core.Audit;
using Xunit;

namespace Optimus.Core.Tests.Audit;

/// <summary>
/// The colour audit's job is to surface what causes "the print came out a different colour" —
/// mixed colour spaces, unexpected spot plates, registration colour used as art.
/// </summary>
public class ColorAuditTests
{
    private static ColorRecord Cmyk(string components, ColorUsage usage = ColorUsage.Fill) =>
        new ColorRecord { Model = ColorModel.Cmyk, Components = components, Usage = usage };

    private static ColorRecord Rgb(string hex, ColorUsage usage = ColorUsage.Fill) =>
        new ColorRecord { Model = ColorModel.Rgb, Hex = hex, Usage = usage };

    private static ColorRecord Spot(string name) =>
        new ColorRecord { Model = ColorModel.Spot, SpotName = name };

    [Fact]
    public void Identical_colours_are_counted_once_but_usage_is_tallied()
    {
        var audit = new ColorAudit();
        audit.Add(Cmyk("C0 M100 Y100 K0"));
        audit.Add(Cmyk("C0 M100 Y100 K0"));
        audit.Add(Cmyk("C0 M100 Y100 K0", ColorUsage.Outline));

        Assert.Single(audit.Unique);
        Assert.Equal(3, audit.TimesUsed(Cmyk("C0 M100 Y100 K0")));
    }

    [Fact]
    public void Different_models_with_the_same_value_are_different_colours()
    {
        var audit = new ColorAudit();
        audit.Add(new ColorRecord { Model = ColorModel.Cmyk, Hex = "#FF0000" });
        audit.Add(new ColorRecord { Model = ColorModel.Rgb, Hex = "#FF0000" });

        Assert.Equal(2, audit.Unique.Count);
    }

    /// <summary>The classic print complaint, and the audit must lead with it.</summary>
    [Fact]
    public void Mixing_rgb_and_cmyk_is_reported_first()
    {
        var audit = new ColorAudit();
        audit.Add(Cmyk("C0 M0 Y0 K100"));
        audit.Add(Rgb("#3366FF"));

        Assert.True(audit.MixesRgbAndCmyk);
        Assert.Contains("mistura RGB", audit.Findings()[0]);
    }

    [Fact]
    public void A_single_space_document_is_not_flagged_as_mixed()
    {
        var audit = new ColorAudit();
        audit.Add(Cmyk("C0 M0 Y0 K100"));
        audit.Add(Cmyk("C100 M0 Y0 K0"));

        Assert.False(audit.MixesRgbAndCmyk);
    }

    /// <summary>Spot names come from the customer's own file — we ship no Pantone table.</summary>
    [Fact]
    public void Spot_colours_are_listed_by_the_name_in_the_file()
    {
        var audit = new ColorAudit();
        audit.Add(Spot("PANTONE 185 C"));
        audit.Add(Spot("PANTONE Reflex Blue C"));

        Assert.Equal(2, audit.SpotColors().Count);
        string finding = string.Join(" ", audit.Findings());
        Assert.Contains("PANTONE 185 C", finding);
        Assert.Contains("spot", finding);
    }

    [Fact]
    public void A_colour_with_a_spot_name_counts_as_spot_whatever_its_model()
    {
        var record = new ColorRecord { Model = ColorModel.Cmyk, SpotName = "Ouro Metálico" };
        Assert.True(record.IsSpot);
    }

    /// <summary>Registration colour prints on EVERY plate — intentional only for crop marks.</summary>
    [Fact]
    public void Registration_colour_is_called_out()
    {
        var audit = new ColorAudit();
        audit.Add(new ColorRecord { Model = ColorModel.Registration, Hex = "#000000" });

        Assert.Contains("REGISTRO", string.Join(" ", audit.Findings()));
    }

    /// <summary>
    /// Texture and mesh fills have no colour API. The audit says how many it could not inspect rather
    /// than pretending it saw everything.
    /// </summary>
    [Fact]
    public void Uninspectable_fills_are_declared_not_hidden()
    {
        var audit = new ColorAudit { NotInspectable = 4 };
        audit.Add(Cmyk("C0 M0 Y0 K100"));

        string finding = string.Join(" ", audit.Findings());
        Assert.Contains("4 objeto", finding);
        Assert.Contains("textura", finding);
    }

    [Fact]
    public void A_clean_document_gets_a_clean_finding()
    {
        var audit = new ColorAudit();
        audit.Add(Cmyk("C0 M0 Y0 K100"));
        audit.Add(Cmyk("C100 M0 Y0 K0"));

        string finding = string.Join(" ", audit.Findings());
        Assert.Contains("2 cor(es) distinta(s)", finding);
        Assert.DoesNotContain("mistura", finding);
    }

    [Fact]
    public void Empty_audit_does_not_throw_and_says_something()
    {
        var audit = new ColorAudit();
        Assert.NotEmpty(audit.Findings());
        Assert.Empty(audit.Unique);
    }

    [Fact]
    public void Null_colour_is_ignored()
    {
        var audit = new ColorAudit();
        audit.Add(null!);
        Assert.Empty(audit.Unique);
    }

    [Fact]
    public void Counts_per_model_are_correct()
    {
        var audit = new ColorAudit();
        audit.Add(Cmyk("C0 M0 Y0 K100"));
        audit.Add(Cmyk("C100 M0 Y0 K0"));
        audit.Add(Rgb("#FFFFFF"));
        audit.Add(Spot("PANTONE 300 C"));

        Assert.Equal(2, audit.CountOf(ColorModel.Cmyk));
        Assert.Equal(1, audit.CountOf(ColorModel.Rgb));
        Assert.Equal(1, audit.CountOf(ColorModel.Spot));
        Assert.Equal(0, audit.CountOf(ColorModel.Lab));
    }
}
