using System;
using System.Collections.Generic;
using System.Linq;
using Optimus.Core.Audit;

namespace Optimus.Core.Palettes
{
    /// <summary>One row of the colour table the operator sees: a colour, how much it's used, and
    /// whether it's already standardised.</summary>
    public sealed class ColorTableRow
    {
        public ColorRecord Color { get; set; } = new ColorRecord();
        public int TimesUsed { get; set; }
        public double UsagePercent { get; set; }

        /// <summary>Name from the registered palette, or empty when this colour isn't registered anywhere.</summary>
        public string RegisteredAs { get; set; } = "";
        public string RegisteredPalette { get; set; } = "";
        public bool InPalette => RegisteredAs.Length > 0;

        /// <summary>
        /// Name of a DIFFERENT registered colour this one is visually indistinguishable from — set
        /// only when this colour is NOT itself registered. This does not merge or relax matching: the
        /// colour still shows FORA DA PALETA, it just says why instead of leaving the operator to
        /// conclude the palette check is broken. Two real-file causes, both covered:
        ///
        /// <para>
        /// (a) SAME rendered hex, different model — a CMYK ink (say C0 M60 Y100 K0) and an RGB screen
        /// colour both read back the identical <c>Color.HexValue</c> ("F58634"), because that field
        /// expresses the RGB-ish rendering regardless of the underlying model (O19). Genuinely
        /// different keys — a CMYK recipe and an RGB value are different colour specifications, same
        /// reasoning as O27's "chapa vs rico".
        /// </para>
        /// <para>
        /// (b) NEAR-identical numeric RGB after a mode conversion — measured on a real file: converting
        /// a CMYK orange to RGB via "Converter cores" did not reproduce the exact byte-for-byte RGB
        /// already registered for that same colour (CorelDRAW's quick preview hex and its actual
        /// colour-managed CMYK→RGB conversion don't necessarily agree to the last unit). The two swatches
        /// are pixel-identical to the eye and a few units apart numerically — comparing the (undocumented,
        /// per O19) hex STRING literally misses this, so the check compares the reliable NUMERIC RGB
        /// reading instead, within a small tolerance.
        /// </para>
        /// </summary>
        public string SimilarTo { get; set; } = "";
    }

    /// <summary>
    /// Cross-references an audited document's colours against the shop's registered palettes:
    /// usage share per colour, and which colours aren't standardised (the ALERT from the whiteboard).
    /// Pure logic — no COM, everything already read by <see cref="Optimus.Interop.ColorCollector"/>.
    /// </summary>
    public static class PaletteMatcher
    {
        /// <summary>How far apart two RGB channels may be and still count as the same colour to the
        /// eye. 3 out of 255 catches rounding drift from a colour-mode conversion (measured cause)
        /// without risking a false "same colour" on two shades a designer chose on purpose.</summary>
        private const int NearMatchMaxChannelDelta = 3;

        public static List<ColorTableRow> BuildTable(ColorAudit audit, PaletteRegistry registry)
        {
            var rows = new List<ColorTableRow>();
            if (audit == null) return rows;

            Dictionary<string, PaletteColor> registered = registry?.ActiveColorsByKey() ?? new Dictionary<string, PaletteColor>();
            List<PaletteColor> activeColors = registry?.Active?.Colors ?? new List<PaletteColor>();
            int total = audit.Unique.Sum(c => audit.TimesUsed(c));

            foreach (ColorRecord color in audit.Unique.OrderByDescending(c => audit.TimesUsed(c)))
            {
                int used = audit.TimesUsed(color);
                var row = new ColorTableRow
                {
                    Color = color,
                    TimesUsed = used,
                    UsagePercent = total > 0 ? Math.Round(used * 100.0 / total, 1) : 0,
                };

                if (registered.TryGetValue(color.Key, out PaletteColor? match))
                {
                    row.RegisteredAs = match!.Name;
                    row.RegisteredPalette = OwningPaletteName(registry!, match);
                }
                else
                {
                    PaletteColor? similar = FindVisuallySimilar(activeColors, color);
                    if (similar != null) row.SimilarTo = similar.Name;
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// A registered colour that LOOKS like <paramref name="color"/> even though its key doesn't
        /// match — checked two ways, exact hex string first (cheap, catches the CMYK/RGB same-render
        /// case), then numeric RGB proximity (catches conversion rounding drift; see
        /// <see cref="ColorTableRow.SimilarTo"/>'s remarks for both real-file cases).
        /// </summary>
        private static PaletteColor? FindVisuallySimilar(List<PaletteColor> activeColors, ColorRecord color)
        {
            string hex = ColorKeyFormat.NormalizeHex(color.Hex);
            foreach (PaletteColor candidate in activeColors)
                if (hex.Length > 0 && hex == ColorKeyFormat.NormalizeHex(candidate.Hex)) return candidate;

            if (!color.RgbKnown || color.Rgb == null || color.Rgb.Length != 3) return null;
            foreach (PaletteColor candidate in activeColors)
                if (TryParseHexRgb(candidate.Hex, out int r, out int g, out int b)
                    && Math.Abs(color.Rgb[0] - r) <= NearMatchMaxChannelDelta
                    && Math.Abs(color.Rgb[1] - g) <= NearMatchMaxChannelDelta
                    && Math.Abs(color.Rgb[2] - b) <= NearMatchMaxChannelDelta)
                    return candidate;

            return null;
        }

        private static bool TryParseHexRgb(string hex, out int r, out int g, out int b)
        {
            r = g = b = 0;
            string digits = ColorKeyFormat.NormalizeHex(hex);
            if (digits.Length != 6) return false;
            try
            {
                r = Convert.ToInt32(digits.Substring(0, 2), 16);
                g = Convert.ToInt32(digits.Substring(2, 2), 16);
                b = Convert.ToInt32(digits.Substring(4, 2), 16);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Colours used in the file that are NOT part of any registered palette — the alert.</summary>
        public static List<ColorRecord> NotInAnyPalette(ColorAudit audit, PaletteRegistry registry)
        {
            var alerts = new List<ColorRecord>();
            if (audit == null) return alerts;

            Dictionary<string, PaletteColor> registered = registry?.ActiveColorsByKey() ?? new Dictionary<string, PaletteColor>();
            foreach (ColorRecord color in audit.Unique)
                if (!registered.ContainsKey(color.Key)) alerts.Add(color);

            return alerts;
        }

        private static string OwningPaletteName(PaletteRegistry registry, PaletteColor color)
        {
            foreach (ColorPalette palette in registry.Palettes)
                if (palette.Colors.Contains(color)) return palette.Name;
            return "";
        }
    }
}
