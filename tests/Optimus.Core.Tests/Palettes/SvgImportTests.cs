using System.Collections.Generic;
using Optimus.Core.Palettes;
using Xunit;

namespace Optimus.Core.Tests.Palettes
{
    /// <summary>
    /// The SVG is treated as a SOURCE OF COLOURS, nothing more. These tests pin the two decisions
    /// that are easy to get wrong under pressure: that a gradient is NOT special-cased away, and that
    /// opacity does not invent a second colour.
    /// </summary>
    public class SvgImportTests
    {
        private static string Svg(string body) =>
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">" + body + "</svg>";

        [Fact]
        public void Reads_fill_stroke_and_stop_color_not_only_fill()
        {
            SvgColorScan scan = SvgColorExtractor.Scan(Svg(
                "<rect fill=\"#FF0000\" stroke=\"#0000FF\"/>" +
                "<linearGradient><stop stop-color=\"#00FF00\"/></linearGradient>"));

            Assert.True(scan.Parsed);
            Assert.Equal(new[] { "FF0000", "0000FF", "00FF00" }, scan.Hex);
        }

        [Fact]
        public void The_same_colour_many_times_enters_the_palette_once()
        {
            SvgColorScan scan = SvgColorExtractor.Scan(Svg(
                "<rect fill=\"#FF0000\"/><rect fill=\"#0000FF\"/><rect fill=\"#ff0000\"/>" +
                "<rect fill=\"red\"/><rect fill=\"#F00\"/><rect fill=\"rgb(255,0,0)\"/>"));

            Assert.Equal(new[] { "FF0000", "0000FF" }, scan.Hex);
            // The count of MENTIONS survives, so the screen can say "6 menções, 2 cores" instead of
            // implying the file only ever named two.
            Assert.Equal(6, scan.Mentions);
        }

        [Fact]
        public void Opacity_does_not_create_a_second_colour()
        {
            SvgColorScan scan = SvgColorExtractor.Scan(Svg(
                "<rect fill=\"#FF0000\" fill-opacity=\".5\"/>" +
                "<rect fill=\"rgba(255,0,0,0.5)\"/>" +
                "<rect fill=\"#FF000080\"/>"));

            Assert.Equal(new[] { "FF0000" }, scan.Hex);
        }

        [Fact]
        public void Colours_declared_in_a_style_block_are_found()
        {
            // Illustrator's default export puts every colour in a <style> block and none in an
            // attribute. Reading attributes alone would find NOTHING in a real logo file.
            SvgColorScan scan = SvgColorExtractor.Scan(Svg(
                "<style>.cls-1{fill:#E30613;}.cls-2{fill:#1D1D1B;stroke:#FFFFFF}</style>" +
                "<rect class=\"cls-1\"/>"));

            Assert.Equal(new[] { "E30613", "1D1D1B", "FFFFFF" }, scan.Hex);
        }

        [Fact]
        public void Inline_style_attribute_is_read()
        {
            SvgColorScan scan = SvgColorExtractor.Scan(Svg(
                "<rect style=\"fill:#123456;stroke:rgb(10%,20%,30%)\"/>"));

            Assert.Equal(new[] { "123456", "1A334D" }, scan.Hex);
        }

        [Fact]
        public void None_transparent_and_gradient_references_are_not_colours()
        {
            SvgColorScan scan = SvgColorExtractor.Scan(Svg(
                "<rect fill=\"none\" stroke=\"transparent\"/>" +
                "<rect fill=\"url(#grad)\"/><rect fill=\"currentColor\"/>"));

            Assert.True(scan.Parsed);
            Assert.Empty(scan.Hex);
            Assert.Equal(PaletteChange.NoColors, scan.Status);
        }

        [Fact]
        public void A_gradient_with_many_stops_is_not_rejected_for_being_large()
        {
            // Explicitly NOT filtered by count (§12). A file may legitimately hold hundreds of
            // colours, and a "more than N means gradient" rule would throw away real work.
            var body = new System.Text.StringBuilder("<linearGradient>");
            for (int i = 0; i < 120; i++) body.Append("<stop stop-color=\"#").Append(i.ToString("X2")).Append("0000\"/>");
            body.Append("</linearGradient>");

            SvgColorScan scan = SvgColorExtractor.Scan(Svg(body.ToString()));

            Assert.Equal(PaletteChange.Ok, scan.Status);
            Assert.Equal(120, scan.Hex.Count);
        }

        [Fact]
        public void A_file_that_is_not_svg_is_reported_not_silently_empty()
        {
            Assert.False(SvgColorExtractor.Scan("isto nao e xml").Parsed);
            Assert.False(SvgColorExtractor.Scan("<html><body/></html>").Parsed);
            Assert.Equal(PaletteChange.UnreadableFile, SvgColorExtractor.Scan("<svg").Status);
        }

        [Fact]
        public void A_doctype_from_illustrator_does_not_break_the_read()
        {
            // The default XML reader PROHIBITS DTDs and throws on this line, which Illustrator has
            // been writing for twenty years. A real client logo would have imported as "invalid".
            string svg = "<?xml version=\"1.0\"?>" +
                "<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" " +
                "\"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">" +
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect fill=\"#ABCDEF\"/></svg>";

            SvgColorScan scan = SvgColorExtractor.Scan(svg);

            Assert.True(scan.Parsed);
            Assert.Equal(new[] { "ABCDEF" }, scan.Hex);
        }

        [Fact]
        public void Hsl_is_a_colour_too()
        {
            Assert.Equal("FF0000", SvgColorExtractor.Normalize("hsl(0, 100%, 50%)"));
            Assert.Equal("00FF00", SvgColorExtractor.Normalize("hsl(120, 100%, 50%)"));
            Assert.Equal("808080", SvgColorExtractor.Normalize("hsl(0, 0%, 50.2%)"));
        }

        [Fact]
        public void Imported_colours_start_with_no_name()
        {
            PaletteRegistry registry = PaletteRegistry.InMemory();
            SvgColorScan scan = SvgColorExtractor.Scan(Svg("<rect fill=\"#FF0000\" stroke=\"#0000FF\"/>"));

            ColorPalette? palette = registry.ImportPalette("INTERCLASSE 2026", ToColors(scan), out PaletteChange status);

            Assert.Equal(PaletteChange.Ok, status);
            Assert.NotNull(palette);
            Assert.Equal(2, palette!.Colors.Count);
            Assert.All(palette.Colors, c => Assert.Equal("", c.Name));
        }

        [Fact]
        public void Import_into_an_existing_name_is_refused_before_anything_is_written()
        {
            PaletteRegistry registry = PaletteRegistry.InMemory();
            registry.AddPalette("Cliente X");
            SvgColorScan scan = SvgColorExtractor.Scan(Svg("<rect fill=\"#FF0000\"/>"));

            ColorPalette? palette = registry.ImportPalette("cliente x", ToColors(scan), out PaletteChange status);

            Assert.Equal(PaletteChange.NameTaken, status);
            Assert.Null(palette);
            Assert.Single(registry.Palettes);
            Assert.Empty(registry.Palettes[0].Colors);
        }

        private static List<PaletteColor> ToColors(SvgColorScan scan)
        {
            var list = new List<PaletteColor>();
            foreach (string hex in scan.Hex)
                list.Add(new PaletteColor { Name = "", Model = Core.Audit.ColorModel.Rgb, Hex = hex });
            return list;
        }
    }
}
