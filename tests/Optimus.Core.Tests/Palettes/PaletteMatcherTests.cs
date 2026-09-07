using Optimus.Core.Audit;
using Optimus.Core.Palettes;
using Xunit;

namespace Optimus.Core.Tests.Palettes
{
    public class PaletteMatcherTests
    {
        private static ColorAudit AuditWith(params (ColorModel model, string hex, int times)[] colors)
        {
            var audit = new ColorAudit();
            foreach ((ColorModel model, string hex, int times) c in colors)
                for (int i = 0; i < c.times; i++)
                    audit.Add(new ColorRecord { Model = c.model, Hex = c.hex });
            return audit;
        }

        [Fact]
        public void BuildTable_computes_usage_percent_relative_to_total_occurrences()
        {
            ColorAudit audit = AuditWith((ColorModel.Rgb, "FF0000", 3), (ColorModel.Rgb, "00FF00", 1));
            var rows = PaletteMatcher.BuildTable(audit, PaletteRegistry.InMemory());

            Assert.Equal(2, rows.Count);
            Assert.Equal(75.0, rows[0].UsagePercent); // 3 of 4 total occurrences, sorted by usage desc
            Assert.Equal(25.0, rows[1].UsagePercent);
        }

        [Fact]
        public void BuildTable_marks_registered_colors_with_their_palette_name()
        {
            ColorAudit audit = AuditWith((ColorModel.Rgb, "FF0000", 1));
            PaletteRegistry registry = PaletteRegistry.InMemory();
            registry.AddPalette("Paleta Cliente X");
            registry.AddColor("Paleta Cliente X", new PaletteColor { Name = "vermelho", Model = ColorModel.Rgb, Hex = "FF0000" });

            var rows = PaletteMatcher.BuildTable(audit, registry);

            Assert.True(rows[0].InPalette);
            Assert.Equal("vermelho", rows[0].RegisteredAs);
            Assert.Equal("Paleta Cliente X", rows[0].RegisteredPalette);
        }

        [Fact]
        public void NotInAnyPalette_flags_colors_absent_from_every_registered_palette()
        {
            ColorAudit audit = AuditWith((ColorModel.Rgb, "FF0000", 1), (ColorModel.Rgb, "0000FF", 1));
            PaletteRegistry registry = PaletteRegistry.InMemory();
            registry.AddPalette("P1");
            registry.AddColor("P1", new PaletteColor { Name = "vermelho", Model = ColorModel.Rgb, Hex = "FF0000" });

            var alerts = PaletteMatcher.NotInAnyPalette(audit, registry);

            Assert.Single(alerts);
            Assert.Equal("0000FF", alerts[0].Hex);
        }

        [Fact]
        public void Empty_registry_flags_every_color_as_not_registered()
        {
            ColorAudit audit = AuditWith((ColorModel.Cmyk, "010101", 1));
            var alerts = PaletteMatcher.NotInAnyPalette(audit, PaletteRegistry.InMemory());
            Assert.Single(alerts);
        }

        [Fact]
        public void A_cmyk_color_registered_with_full_identity_matches_the_audited_shape()
        {
            // Mirrors the correct path (PaletteColorAdd, OptimusBridge): Model, Hex AND Components
            // copied straight from the ColorRecord the audit produced — never re-typed.
            var audit = new ColorAudit();
            audit.Add(new ColorRecord { Model = ColorModel.Cmyk, Hex = "F58634", Components = "C0 M50 Y90 K0" });

            PaletteRegistry registry = PaletteRegistry.InMemory();
            registry.AddPalette("Cliente X");
            registry.AddColor("Cliente X", new PaletteColor
            {
                Name = "laranja",
                Model = ColorModel.Cmyk,
                Hex = "F58634",
                Components = "C0 M50 Y90 K0",
            });

            Assert.Empty(PaletteMatcher.NotInAnyPalette(audit, registry));
        }

        [Fact]
        public void A_cmyk_color_registered_as_a_bare_rgb_hex_never_matches_the_cmyk_original()
        {
            // The exact trap that was in palDocConfirm() (index.html): picking a CMYK swatch straight
            // from the document's colour grid and re-adding it to the palette using only its on-screen
            // RGB hex (Model forced to Rgb, Components dropped) builds a DIFFERENT key from the one the
            // audit reads off that same shape. An operator who picked "the exact colour" from the file
            // still sees it flagged FORA DA PALETA on the next analysis — this pins the behaviour down
            // so the matcher never grows a lenient hex-only fallback that would hide the mistake instead
            // of forcing the caller to preserve the model and CMYK components.
            var audit = new ColorAudit();
            audit.Add(new ColorRecord { Model = ColorModel.Cmyk, Hex = "F58634", Components = "C0 M50 Y90 K0" });

            PaletteRegistry registry = PaletteRegistry.InMemory();
            registry.AddPalette("Cliente X");
            registry.AddColor("Cliente X", new PaletteColor { Name = "laranja", Model = ColorModel.Rgb, Hex = "F58634" });

            Assert.Single(PaletteMatcher.NotInAnyPalette(audit, registry));
        }
    }
}
