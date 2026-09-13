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
        /// Name of a DIFFERENT registered colour that renders as the exact same hex — set only when
        /// this colour is NOT itself registered. Real-file case: a CMYK ink (say C0 M60 Y100 K0) and
        /// an RGB screen colour the shop already registered both read back the identical
        /// <c>Color.HexValue</c> ("F58634"), because that field expresses the RGB-ish rendering
        /// regardless of the underlying model (O19). They are genuinely different keys — a CMYK recipe
        /// and an RGB value are different colour specifications, same reasoning as O27's "chapa vs
        /// rico" — but an operator staring at two identical-looking swatches, one flagged registered
        /// and the other not, has no way to tell that apart from the swatch alone. This is that
        /// explanation, not a merge: the colour still shows FORA DA PALETA, just with a reason.
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
        public static List<ColorTableRow> BuildTable(ColorAudit audit, PaletteRegistry registry)
        {
            var rows = new List<ColorTableRow>();
            if (audit == null) return rows;

            Dictionary<string, PaletteColor> registered = registry?.ActiveColorsByKey() ?? new Dictionary<string, PaletteColor>();
            Dictionary<string, PaletteColor> registeredByHex = ActiveColorsByHex(registry);
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
                else if (registeredByHex.TryGetValue(ColorKeyFormat.NormalizeHex(color.Hex), out PaletteColor? similar))
                {
                    row.SimilarTo = similar!.Name;
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>Active palette's colours keyed by rendered hex alone (model/components ignored) —
        /// used ONLY to explain a FORA DA PALETA colour, never to decide whether one is registered.</summary>
        private static Dictionary<string, PaletteColor> ActiveColorsByHex(PaletteRegistry? registry)
        {
            var map = new Dictionary<string, PaletteColor>();
            ColorPalette? active = registry?.Active;
            if (active == null) return map;

            foreach (PaletteColor color in active.Colors)
            {
                string hex = ColorKeyFormat.NormalizeHex(color.Hex);
                if (hex.Length > 0 && !map.ContainsKey(hex)) map[hex] = color;
            }
            return map;
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
