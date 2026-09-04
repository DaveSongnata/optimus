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
    }
}
