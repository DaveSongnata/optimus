using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Optimus.Core.Palettes
{
    /// <summary>What one SVG turned out to contain.</summary>
    public sealed class SvgColorScan
    {
        /// <summary>The file was readable as XML with an &lt;svg&gt; root.</summary>
        public bool Parsed;

        /// <summary>Distinct colours, uppercase RRGGBB, in the order the document mentions them.</summary>
        public List<string> Hex = new List<string>();

        /// <summary>How many colour mentions were found before de-duplication — the honest way to
        /// say "47 mentions, 6 colours" instead of implying the file only had six.</summary>
        public int Mentions;

        public PaletteChange Status =
            PaletteChange.UnreadableFile;
    }

    /// <summary>
    /// Reads an SVG purely as a SOURCE OF COLOURS.
    ///
    /// <para>
    /// Deliberately not an SVG importer: no geometry, no metadata convention, no JSON smuggled into
    /// the file. A shop already has the client's logo as an SVG; that file is the fastest honest way
    /// to get the client's colours into a palette, and nothing about it needs to be prepared first.
    /// </para>
    /// <para>
    /// Every property that paints something counts, not only <c>fill</c> — a two-colour mark drawn
    /// with a stroke would otherwise import as one colour. Gradient stops count as well, and are NOT
    /// filtered by any "too many colours, probably a gradient" rule: a legitimate illustration can
    /// hold hundreds of distinct colours, and guessing at intent would reject real files to protect
    /// against a mistake the operator is allowed to make.
    /// </para>
    /// <para>
    /// Opacity is not part of a colour's identity (§14): <c>#FF0000</c> at 50% is the same ink as
    /// <c>#FF0000</c>. Alpha is parsed and discarded rather than rejected, so <c>#FF0000</c>,
    /// <c>rgba(255,0,0,.5)</c> and <c>#FF000080</c> all land on one entry.
    /// </para>
    /// </summary>
    public static class SvgColorExtractor
    {
        // Properties that put visible colour on the page. "color" is here because it is what
        // currentColor resolves to, and a file that sets it is naming a real colour.
        private static readonly HashSet<string> PaintProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fill", "stroke", "stop-color", "flood-color", "lighting-color",
            "color", "solid-color", "background-color", "border-color",
        };

        // Values that name the ABSENCE of a colour, or a reference to something painted elsewhere.
        // A url(#grad) is skipped because the gradient's own stops are visited on their own.
        private static readonly HashSet<string> NotAColor = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "none", "transparent", "currentcolor", "inherit", "initial", "unset", "revert",
            "context-fill", "context-stroke",
        };

        public static SvgColorScan Scan(string svgText)
        {
            var scan = new SvgColorScan();
            if (string.IsNullOrWhiteSpace(svgText)) return scan;

            XDocument doc;
            try
            {
                // DtdProcessing.Ignore, not the default Prohibit: Illustrator has written SVGs with a
                // <!DOCTYPE svg PUBLIC ...> line for twenty years, and the default reader throws on
                // them. XmlResolver stays null so nothing on the network is ever fetched.
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Ignore,
                    XmlResolver = null,
                    IgnoreComments = true,
                };
                using (var reader = XmlReader.Create(new StringReader(svgText), settings))
                    doc = XDocument.Load(reader);
            }
            catch (Exception) { return scan; }

            if (doc.Root == null ||
                !string.Equals(doc.Root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
                return scan;

            scan.Parsed = true;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Take(string? value)
            {
                string? hex = Normalize(value);
                if (hex == null) return;
                scan.Mentions++;
                if (seen.Add(hex)) scan.Hex.Add(hex);
            }

            foreach (XElement element in doc.Root.DescendantsAndSelf())
            {
                foreach (XAttribute attribute in element.Attributes())
                {
                    string name = attribute.Name.LocalName;
                    if (PaintProps.Contains(name)) Take(attribute.Value);
                    else if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
                        TakeFromCss(attribute.Value, Take);
                }

                // A <style> block is where an exported logo usually keeps its colours — .cls-1{fill:#e30613}
                // is Illustrator's default output, and reading only attributes would find nothing at all.
                if (string.Equals(element.Name.LocalName, "style", StringComparison.OrdinalIgnoreCase))
                    TakeFromCss(element.Value, Take);
            }

            scan.Status = scan.Hex.Count > 0 ? PaletteChange.Ok : PaletteChange.NoColors;
            return scan;
        }

        /// <summary>Pulls paint declarations out of CSS text — an inline style attribute or a whole
        /// &lt;style&gt; block; both are the same "prop: value;" soup for this purpose.</summary>
        private static void TakeFromCss(string css, Action<string> take)
        {
            if (string.IsNullOrEmpty(css)) return;

            foreach (string chunk in css.Split(';', '{', '}', '\n', '\r'))
            {
                int colon = chunk.IndexOf(':');
                if (colon <= 0) continue;

                string prop = chunk.Substring(0, colon).Trim();
                // A selector can precede the property in a <style> block (".cls-1{fill:#e30613").
                int lastSpace = prop.LastIndexOfAny(new[] { ' ', '\t', '.', '#', ',', '>' });
                if (lastSpace >= 0) prop = prop.Substring(lastSpace + 1);

                if (!PaintProps.Contains(prop)) continue;
                take(chunk.Substring(colon + 1).Trim());
            }
        }

        /// <summary>
        /// One colour value to uppercase RRGGBB, or null when the value does not name a colour.
        /// Alpha, wherever it appears, is dropped rather than made part of the identity (§14).
        /// </summary>
        public static string? Normalize(string? value)
        {
            if (value == null) return null;
            string v = value.Trim();
            if (v.Length == 0 || NotAColor.Contains(v)) return null;
            if (v.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return null;

            if (v[0] == '#') return FromHex(v.Substring(1));

            int paren = v.IndexOf('(');
            if (paren > 0)
            {
                string fn = v.Substring(0, paren).Trim().ToLowerInvariant();
                string args = v.Substring(paren + 1).TrimEnd(')', ' ', '\t');
                if (fn == "rgb" || fn == "rgba") return FromRgb(args);
                if (fn == "hsl" || fn == "hsla") return FromHsl(args);
                return null;
            }

            return Named(v);
        }

        private static string? FromHex(string digits)
        {
            foreach (char c in digits) if (!Uri.IsHexDigit(c)) return null;

            // 4 and 8 digits carry alpha in the last 1 or 2; both truncate to the colour itself.
            if (digits.Length == 4) digits = digits.Substring(0, 3);
            else if (digits.Length == 8) digits = digits.Substring(0, 6);

            if (digits.Length == 3)
                digits = new string(new[] { digits[0], digits[0], digits[1], digits[1], digits[2], digits[2] });

            return digits.Length == 6 ? digits.ToUpperInvariant() : null;
        }

        private static string? FromRgb(string args)
        {
            // CSS accepts both "255, 0, 0" and the space-separated "255 0 0 / 50%".
            string[] parts = args.Replace('/', ' ').Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return null;

            var channel = new int[3];
            for (int i = 0; i < 3; i++)
            {
                string p = parts[i].Trim();
                bool percent = p.EndsWith("%", StringComparison.Ordinal);
                if (percent) p = p.Substring(0, p.Length - 1);
                if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return null;
                channel[i] = Clamp255(percent ? n * 255.0 / 100.0 : n);
            }
            return Hex(channel[0], channel[1], channel[2]);
        }

        private static string? FromHsl(string args)
        {
            string[] parts = args.Replace('/', ' ').Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return null;

            if (!double.TryParse(parts[0].Trim().Replace("deg", ""), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double h)) return null;
            if (!TryPercent(parts[1], out double s)) return null;
            if (!TryPercent(parts[2], out double l)) return null;

            h = ((h % 360) + 360) % 360 / 360.0;
            double c2 = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double c1 = 2 * l - c2;

            return Hex(Clamp255(HueToChannel(c1, c2, h + 1.0 / 3.0) * 255),
                       Clamp255(HueToChannel(c1, c2, h) * 255),
                       Clamp255(HueToChannel(c1, c2, h - 1.0 / 3.0) * 255));
        }

        private static bool TryPercent(string raw, out double value)
        {
            string p = raw.Trim().TrimEnd('%');
            bool ok = double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            value = Math.Max(0, Math.Min(1, value / 100.0));
            return ok;
        }

        private static double HueToChannel(double c1, double c2, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return c1 + (c2 - c1) * 6 * t;
            if (t < 1.0 / 2.0) return c2;
            if (t < 2.0 / 3.0) return c1 + (c2 - c1) * (2.0 / 3.0 - t) * 6;
            return c1;
        }

        // AwayFromZero, not .NET's default banker's rounding: 30% of 255 is 76.5, and every browser
        // resolves rgb(10%,20%,30%) to 77, not 76. A colour that differs from the one the designer
        // saw by one step is a colour the palette will never match.
        private static int Clamp255(double v) =>
            (int)Math.Round(Math.Max(0, Math.Min(255, v)), MidpointRounding.AwayFromZero);

        private static string Hex(int r, int g, int b) =>
            r.ToString("X2", CultureInfo.InvariantCulture) +
            g.ToString("X2", CultureInfo.InvariantCulture) +
            b.ToString("X2", CultureInfo.InvariantCulture);

        private static string? Named(string name)
        {
            EnsureNames();
            return _names!.TryGetValue(name, out string? hex) ? hex : null;
        }

        private static Dictionary<string, string>? _names;

        private static void EnsureNames()
        {
            if (_names != null) return;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in NamedTable.Split('|'))
            {
                int space = entry.IndexOf(' ');
                if (space > 0) map[entry.Substring(0, space)] = entry.Substring(space + 1);
            }
            _names = map;
        }

        // The CSS named colours, packed rather than written as 148 dictionary lines — it is a lookup
        // table, not code, and a shop's SVG really does say fill="red".
        private const string NamedTable =
            "aliceblue F0F8FF|antiquewhite FAEBD7|aqua 00FFFF|aquamarine 7FFFD4|azure F0FFFF|" +
            "beige F5F5DC|bisque FFE4C4|black 000000|blanchedalmond FFEBCD|blue 0000FF|" +
            "blueviolet 8A2BE2|brown A52A2A|burlywood DEB887|cadetblue 5F9EA0|chartreuse 7FFF00|" +
            "chocolate D2691E|coral FF7F50|cornflowerblue 6495ED|cornsilk FFF8DC|crimson DC143C|" +
            "cyan 00FFFF|darkblue 00008B|darkcyan 008B8B|darkgoldenrod B8860B|darkgray A9A9A9|" +
            "darkgreen 006400|darkgrey A9A9A9|darkkhaki BDB76B|darkmagenta 8B008B|darkolivegreen 556B2F|" +
            "darkorange FF8C00|darkorchid 9932CC|darkred 8B0000|darksalmon E9967A|darkseagreen 8FBC8F|" +
            "darkslateblue 483D8B|darkslategray 2F4F4F|darkslategrey 2F4F4F|darkturquoise 00CED1|" +
            "darkviolet 9400D3|deeppink FF1493|deepskyblue 00BFFF|dimgray 696969|dimgrey 696969|" +
            "dodgerblue 1E90FF|firebrick B22222|floralwhite FFFAF0|forestgreen 228B22|fuchsia FF00FF|" +
            "gainsboro DCDCDC|ghostwhite F8F8FF|gold FFD700|goldenrod DAA520|gray 808080|green 008000|" +
            "greenyellow ADFF2F|grey 808080|honeydew F0FFF0|hotpink FF69B4|indianred CD5C5C|" +
            "indigo 4B0082|ivory FFFFF0|khaki F0E68C|lavender E6E6FA|lavenderblush FFF0F5|" +
            "lawngreen 7CFC00|lemonchiffon FFFACD|lightblue ADD8E6|lightcoral F08080|lightcyan E0FFFF|" +
            "lightgoldenrodyellow FAFAD2|lightgray D3D3D3|lightgreen 90EE90|lightgrey D3D3D3|" +
            "lightpink FFB6C1|lightsalmon FFA07A|lightseagreen 20B2AA|lightskyblue 87CEFA|" +
            "lightslategray 778899|lightslategrey 778899|lightsteelblue B0C4DE|lightyellow FFFFE0|" +
            "lime 00FF00|limegreen 32CD32|linen FAF0E6|magenta FF00FF|maroon 800000|" +
            "mediumaquamarine 66CDAA|mediumblue 0000CD|mediumorchid BA55D3|mediumpurple 9370DB|" +
            "mediumseagreen 3CB371|mediumslateblue 7B68EE|mediumspringgreen 00FA9A|" +
            "mediumturquoise 48D1CC|mediumvioletred C71585|midnightblue 191970|mintcream F5FFFA|" +
            "mistyrose FFE4E1|moccasin FFE4B5|navajowhite FFDEAD|navy 000080|oldlace FDF5E6|" +
            "olive 808000|olivedrab 6B8E23|orange FFA500|orangered FF4500|orchid DA70D6|" +
            "palegoldenrod EEE8AA|palegreen 98FB98|paleturquoise AFEEEE|palevioletred DB7093|" +
            "papayawhip FFEFD5|peachpuff FFDAB9|peru CD853F|pink FFC0CB|plum DDA0DD|powderblue B0E0E6|" +
            "purple 800080|rebeccapurple 663399|red FF0000|rosybrown BC8F8F|royalblue 4169E1|" +
            "saddlebrown 8B4513|salmon FA8072|sandybrown F4A460|seagreen 2E8B57|seashell FFF5EE|" +
            "sienna A0522D|silver C0C0C0|skyblue 87CEEB|slateblue 6A5ACD|slategray 708090|" +
            "slategrey 708090|snow FFFAFA|springgreen 00FF7F|steelblue 4682B4|tan D2B48C|teal 008080|" +
            "thistle D8BFD8|tomato FF6347|turquoise 40E0D0|violet EE82EE|wheat F5DEB3|white FFFFFF|" +
            "whitesmoke F5F5F5|yellow FFFF00|yellowgreen 9ACD32";
    }
}
