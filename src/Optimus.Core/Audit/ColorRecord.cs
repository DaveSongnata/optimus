using System;
using System.Collections.Generic;

namespace Optimus.Core.Audit
{
    /// <summary>Colour model, mirroring the verified <c>cdrColorType</c> values.</summary>
    public enum ColorModel
    {
        Unknown = 0,
        Pantone = 1,
        Cmyk = 2,
        Rgb = 5,
        Gray = 9,
        Lab = 12,
        Registration = 20,
        Spot = 25,
        Mixed = 99,
    }

    /// <summary>Where in the artwork a colour was found.</summary>
    public enum ColorUsage { Fill, Outline, Gradient, Pattern }

    /// <summary>One colour found in the document.</summary>
    public sealed class ColorRecord
    {
        public ColorModel Model { get; set; } = ColorModel.Unknown;
        public ColorUsage Usage { get; set; } = ColorUsage.Fill;

        /// <summary>Hex form for display, from <c>Color.HexValue</c>.</summary>
        public string Hex { get; set; } = "";

        /// <summary>
        /// The colour as it actually looks on screen, 0-255 each, read off a COPY converted to RGB.
        ///
        /// <para>
        /// NOT derived from <see cref="Hex"/>: <c>Color.HexValue</c>'s exact string format is
        /// undocumented, and trusting it painted every one of 128 swatches blank white on a real file
        /// (measured 2026-08-15) — the UI cannot show a colour it cannot spell. Numeric components
        /// have no format to get wrong, and they work for spot and Pantone too, where a hex string may
        /// not exist at all.
        /// </para>
        /// </summary>
        public int[] Rgb { get; set; } = new int[3];

        /// <summary>True once <see cref="Rgb"/> holds a real reading rather than the default zeros —
        /// so the UI can show "unknown" instead of painting an invented black.</summary>
        public bool RgbKnown { get; set; }

        /// <summary>CMYK components when the model is CMYK, else empty.</summary>
        public string Components { get; set; } = "";

        /// <summary>
        /// Spot/Pantone name AS RECORDED IN THE CUSTOMER'S OWN FILE. Never looked up in a table we
        /// ship — Pantone has enforced against redistributed colour libraries, and echoing the name
        /// already present in the document is a different act from selling a lookup table.
        /// </summary>
        public string SpotName { get; set; } = "";

        public bool IsSpot => Model == ColorModel.Spot || SpotName.Length > 0;

        /// <summary>Identity for de-duplication: same model + same value is the same colour.</summary>
        public string Key => $"{Model}|{(SpotName.Length > 0 ? SpotName : Hex + Components)}";
    }

    /// <summary>
    /// Aggregated colour audit of a document.
    ///
    /// <para>
    /// Print-relevant on purpose: a job that mixes RGB and CMYK is the classic cause of "the colour
    /// came out different", so mixed-space usage is surfaced rather than buried.
    /// </para>
    /// </summary>
    public sealed class ColorAudit
    {
        private readonly Dictionary<string, ColorRecord> _unique =
            new Dictionary<string, ColorRecord>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, int> _counts =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Shapes whose colours could not be read (texture and mesh fills have no API).</summary>
        public int NotInspectable { get; set; }

        public int ShapesInspected { get; set; }

        public void Add(ColorRecord color)
        {
            if (color == null) return;
            string key = color.Key;
            if (!_unique.ContainsKey(key)) _unique[key] = color;
            _counts.TryGetValue(key, out int n);
            _counts[key] = n + 1;
        }

        public IReadOnlyCollection<ColorRecord> Unique => _unique.Values;

        public int TimesUsed(ColorRecord color) =>
            color != null && _counts.TryGetValue(color.Key, out int n) ? n : 0;

        public int CountOf(ColorModel model)
        {
            int n = 0;
            foreach (ColorRecord c in _unique.Values) if (c.Model == model) n++;
            return n;
        }

        public List<ColorRecord> SpotColors()
        {
            var spots = new List<ColorRecord>();
            foreach (ColorRecord c in _unique.Values) if (c.IsSpot) spots.Add(c);
            return spots;
        }

        /// <summary>
        /// True when the document mixes RGB and CMYK — the classic reason a print comes out with
        /// different colours than the screen showed.
        /// </summary>
        public bool MixesRgbAndCmyk => CountOf(ColorModel.Rgb) > 0 && CountOf(ColorModel.Cmyk) > 0;

        /// <summary>Plain-language findings for the operator, most important first.</summary>
        public List<string> Findings()
        {
            var notes = new List<string>();

            if (MixesRgbAndCmyk)
                notes.Add($"Este arquivo mistura RGB ({CountOf(ColorModel.Rgb)} cores) e CMYK "
                        + $"({CountOf(ColorModel.Cmyk)} cores) — causa comum de cor impressa diferente da tela.");

            List<ColorRecord> spots = SpotColors();
            if (spots.Count > 0)
            {
                var names = new List<string>();
                foreach (ColorRecord s in spots)
                    if (s.SpotName.Length > 0 && names.Count < 6) names.Add(s.SpotName);
                notes.Add($"{spots.Count} cor(es) especial/spot"
                        + (names.Count > 0 ? ": " + string.Join(", ", names) : "")
                        + ". Confirme com a gráfica se todas serão impressas como spot.");
            }

            if (CountOf(ColorModel.Registration) > 0)
                notes.Add("Há objeto em cor de REGISTRO — ele sai em todas as chapas. "
                        + "Normalmente isso é intencional só em marcas de corte.");

            if (NotInspectable > 0)
                notes.Add($"{NotInspectable} objeto(s) com preenchimento de textura ou malha: "
                        + "o CorelDRAW não expõe essas cores, então não foram inspecionadas.");

            if (notes.Count == 0) notes.Add($"{Unique.Count} cor(es) distinta(s), sem problemas aparentes.");
            return notes;
        }
    }
}
