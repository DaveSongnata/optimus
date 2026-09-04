using System;
using System.Collections.Generic;

namespace Optimus.Core.Diagnostics
{
    /// <summary>Which colour spaces a document embeds a profile for, and which it actually uses.</summary>
    public sealed class ColorContext
    {
        /// <summary>Profile name per space id ("Cmyk", "Rgb", "Grayscale"). Present = EMBEDDED.</summary>
        public Dictionary<string, string> Profiles { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The document's primary colour model.</summary>
        public string ColorModel { get; set; } = "";

        public bool HasRgbObjects { get; set; }
        public bool HasCmykObjects { get; set; }
        public bool HasGrayscaleObjects { get; set; }

        public bool HasProfile(string spaceId) => Profiles.ContainsKey(spaceId);

        /// <summary>
        /// True when a profile is embedded for a space in which the document has NO objects — the
        /// only case where dropping it is arguably free.
        ///
        /// <para>
        /// HONESTY: across 29 profile-bearing files in the real corpus, an embedded CMYK profile came
        /// with <c>HasCmykObjects=true</c> EVERY time. Corel embeds spaces it uses. So this rarely
        /// fires, and ICC stripping must NOT be sold as free — the honest lever is re-embedding a
        /// smaller/standard profile, which is a colour-management decision, not a cleanup.
        /// </para>
        /// </summary>
        public bool IsProfileUnused(string spaceId)
        {
            if (!HasProfile(spaceId)) return false;
            switch (spaceId.ToLowerInvariant())
            {
                case "cmyk": return !HasCmykObjects;
                case "rgb": return !HasRgbObjects;
                case "grayscale": return !HasGrayscaleObjects;
                default: return false;
            }
        }
    }

    /// <summary>
    /// Parses <c>color/color.xml</c> — a ~250 byte entry that answers "is this huge embedded profile
    /// actually referenced?" without any guessing.
    ///
    /// <para>
    /// Real content from a client file whose profile is 97.3% of its bytes:
    /// <c>&lt;ColorProfile id="Cmyk"&gt;ISO Coated v2 (ECI)&lt;/ColorProfile&gt; …
    /// &lt;HasCmykObjects&gt;true&lt;/HasCmykObjects&gt;</c>. A file with no embedded profile has a
    /// <c>&lt;ColorContext&gt;</c> with no <c>&lt;ColorProfile&gt;</c> children at all.
    /// </para>
    /// <para>
    /// Deliberately a small hand-rolled scan rather than an XML parser: the input is a fixed, tiny,
    /// machine-written document, and Core must stay dependency-free.
    /// </para>
    /// </summary>
    public static class ColorContextReader
    {
        public static ColorContext Parse(string xml)
        {
            var ctx = new ColorContext();
            if (string.IsNullOrWhiteSpace(xml)) return ctx;

            foreach ((string id, string name) in ReadProfiles(xml))
                if (!ctx.Profiles.ContainsKey(id)) ctx.Profiles[id] = name;

            ctx.ColorModel = ReadElement(xml, "ColorModel");
            ctx.HasRgbObjects = ReadBool(xml, "HasRgbObjects");
            ctx.HasCmykObjects = ReadBool(xml, "HasCmykObjects");
            ctx.HasGrayscaleObjects = ReadBool(xml, "HasGrayscaleObjects");
            return ctx;
        }

        private static IEnumerable<(string Id, string Name)> ReadProfiles(string xml)
        {
            const string open = "<ColorProfile";
            int i = 0;
            while (true)
            {
                int start = xml.IndexOf(open, i, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;

                int tagEnd = xml.IndexOf('>', start);
                if (tagEnd < 0) break;

                string tag = xml.Substring(start, tagEnd - start);
                string id = ReadAttribute(tag, "id");

                int close = xml.IndexOf("</ColorProfile>", tagEnd, StringComparison.OrdinalIgnoreCase);
                string name = close > tagEnd ? xml.Substring(tagEnd + 1, close - tagEnd - 1).Trim() : "";

                if (id.Length > 0) yield return (id, name);
                i = close > 0 ? close + 1 : tagEnd + 1;
            }
        }

        private static string ReadAttribute(string tag, string attribute)
        {
            int at = tag.IndexOf(attribute + "=\"", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return "";
            int from = at + attribute.Length + 2;
            int to = tag.IndexOf('"', from);
            return to > from ? tag.Substring(from, to - from) : "";
        }

        private static string ReadElement(string xml, string name)
        {
            int start = xml.IndexOf("<" + name + ">", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return "";
            start += name.Length + 2;
            int end = xml.IndexOf("</" + name + ">", start, StringComparison.OrdinalIgnoreCase);
            return end > start ? xml.Substring(start, end - start).Trim() : "";
        }

        private static bool ReadBool(string xml, string name) =>
            string.Equals(ReadElement(xml, name), "true", StringComparison.OrdinalIgnoreCase);
    }
}
