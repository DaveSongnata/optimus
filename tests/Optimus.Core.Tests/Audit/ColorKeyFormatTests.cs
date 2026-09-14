using Optimus.Core.Audit;
using Optimus.Core.Palettes;
using Xunit;

namespace Optimus.Core.Tests.Audit
{
    /// <summary>
    /// Pins down the exact "impossible" bug reported on a real file (2026-09): an operator converted
    /// a CMYK orange to RGB, the audit read it back as <c>#F58634</c> (<c>Color.HexValue</c> "já vem
    /// com '#' embutido"), and the palette's own entry for that same orange — typed by hand earlier —
    /// held <c>F58634</c> with no '#'. Same colour, two spellings, and the literal-string comparison
    /// that used to sit in both <c>Key</c> getters told the operator it was FORA DA PALETA.
    /// </summary>
    public class ColorKeyFormatTests
    {
        [Fact]
        public void A_hash_prefixed_hex_and_a_bare_hex_are_the_same_key()
        {
            var audited = new ColorRecord { Model = ColorModel.Rgb, Hex = "#F58634" };
            var registered = new PaletteColor { Model = ColorModel.Rgb, Hex = "F58634" };

            Assert.Equal(audited.Key, registered.Key);
        }

        [Fact]
        public void Hex_case_does_not_change_the_key()
        {
            var audited = new ColorRecord { Model = ColorModel.Rgb, Hex = "#f58634" };
            var registered = new PaletteColor { Model = ColorModel.Rgb, Hex = "F58634" };

            Assert.Equal(audited.Key, registered.Key);
        }

        [Fact]
        public void ColorRecord_and_PaletteColor_keys_are_built_by_the_same_shared_formula()
        {
            var record = new ColorRecord
            {
                Model = ColorModel.Cmyk,
                Hex = "#F58634",
                Components = "C0 M50 Y90 K0",
            };
            var palette = new PaletteColor
            {
                Model = ColorModel.Cmyk,
                Hex = "#F58634",
                Components = "C0 M50 Y90 K0",
            };

            Assert.Equal(record.Key, palette.Key);
            Assert.Equal(ColorKeyFormat.Build(record.Model, record.SpotName, record.Hex, record.Components), record.Key);
        }

        [Fact]
        public void Spot_name_case_does_not_change_the_key()
        {
            // Not yet observed on a real file — PaletteColorAdd/PaletteCapture always copy SpotName
            // verbatim from the SAME document reading, so both sides agree today. But
            // PaletteRegistry's own dictionaries and `==` checks are case-SENSITIVE (unlike
            // ColorAudit's), so this is the same bug class waiting for a manually-typed spot name to
            // trigger it. Normalizing here means no comparer choice can reopen it.
            var audited = new ColorRecord { Model = ColorModel.Spot, SpotName = "PANTONE 172 C" };
            var registered = new PaletteColor { Model = ColorModel.Spot, SpotName = "Pantone 172 C" };

            Assert.Equal(audited.Key, registered.Key);
        }

        [Fact]
        public void A_different_colour_value_still_produces_a_different_key()
        {
            // Normalizing hex must not become a lenient match — a real difference still disagrees.
            var audited = new ColorRecord { Model = ColorModel.Rgb, Hex = "#F58634" };
            var registered = new PaletteColor { Model = ColorModel.Rgb, Hex = "0000FF" };

            Assert.NotEqual(audited.Key, registered.Key);
        }

        [Fact]
        public void An_eight_digit_hex_drops_its_trailing_alpha_pair()
        {
            // Same rule the JS colour picker's own hexClean() already applies to ITS output — ink has
            // no transparency, and without this an opaque colour whose reader appended "FF" would
            // fail to match itself.
            Assert.Equal("F58634", ColorKeyFormat.NormalizeHex("F58634FF"));
            Assert.Equal("F58634", ColorKeyFormat.NormalizeHex("#F58634FF"));
        }

        [Fact]
        public void NormalizeHex_strips_any_non_hex_character_not_just_a_leading_hash()
        {
            Assert.Equal("F58634", ColorKeyFormat.NormalizeHex(" #F5-86.34 "));
        }
    }
}
