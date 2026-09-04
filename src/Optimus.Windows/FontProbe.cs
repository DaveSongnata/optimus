using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Optimus.Core.Audit;

namespace Optimus.Windows
{
    /// <summary>
    /// Answers "does this font really have ç?" by reading the font's own cmap table.
    ///
    /// <para>
    /// Uses WPF's <see cref="GlyphTypeface"/>, whose <c>CharacterToGlyphMap</c> IS the cmap. That
    /// choice is deliberate: zero new dependencies and zero licence questions. SixLabors.Fonts 2.x is
    /// net6+ and split-licensed, SharpFont drags FreeType's FTL/GPL in, HarfBuzz is a shaping engine
    /// (wrong tool), and GDI's route is BMP-only and substitutes silently.
    /// </para>
    /// <para>
    /// FOUR GUARDS, each for a way this API lies — all four were confirmed empirically:
    /// </para>
    /// <list type="number">
    /// <item><b>Symbol fonts:</b> Wingdings reports TRUE for U+00E7, because symbol cmaps alias
    /// U+F0xx onto U+00xx. Guarded with <c>GlyphTypeface.Symbol</c>.</item>
    /// <item><b>Silent substitution:</b> <c>new Typeface("Arial Narrow")</c> succeeds by resolving to
    /// Arial. Guarded by requiring the requested name to match the resolved family's own names.</item>
    /// <item><b>Cost:</b> enumerating <c>CharacterToGlyphMap</c> materialises all of Unicode
    /// (~138 ms/font); <c>TryGetValue</c> is ~11 ms. Only TryGetValue is used.</item>
    /// <item><b>TTC collections:</b> the face index lives in <c>FontUri.Fragment</c>
    /// (<c>cambria.ttc#1</c>); the registry cannot express it.</item>
    /// </list>
    /// </summary>
    public sealed class FontProbe
    {
        /// <summary>Font names installed on this machine, lowercased, built once.</summary>
        private readonly HashSet<string> _installed;

        /// <summary>
        /// <paramref name="alsoAvailable"/> are font names CorelDRAW can use even though Windows has
        /// not installed them — what Corel Font Manager activates out of a watched folder. Without
        /// this, such a font is reported "NÃO INSTALADA, está sendo substituída", which is a false
        /// alarm and exactly backwards: the font is present and works. CorelDRAW's own FontList is the
        /// authority on what CorelDRAW can set type in; the Windows list is only a fallback.
        /// </summary>
        public FontProbe(IEnumerable<string>? alsoAvailable = null)
        {
            _installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (alsoAvailable != null)
                foreach (string n in alsoAvailable)
                    if (!string.IsNullOrWhiteSpace(n)) _installed.Add(n.Trim());
            try
            {
                foreach (FontFamily family in Fonts.SystemFontFamilies)
                {
                    foreach (string name in family.FamilyNames.Values) _installed.Add(name);
                    foreach (string name in family.FamilyNames.Values)
                        foreach (Typeface face in family.GetTypefaces())
                            foreach (string faceName in face.FaceNames.Values)
                                _installed.Add(name + " " + faceName);
                }
            }
            catch { /* an unreadable font collection leaves the set partial, never throws */ }
        }

        /// <summary>Whether a font NAME is genuinely installed (not merely resolvable).</summary>
        public bool IsInstalled(string fontName) =>
            !string.IsNullOrWhiteSpace(fontName) && _installed.Contains(fontName.Trim());

        /// <summary>
        /// Full pt-BR coverage check for one font name, as CorelDRAW reports it.
        /// </summary>
        public GlyphCoverage Check(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return new GlyphCoverage { FontName = fontName ?? "", Verdict = FontVerdict.NotInstalled };

            string requested = fontName.Trim();

            GlyphTypeface? glyphs = null;
            string resolvedFamily = "";
            try
            {
                var typeface = new Typeface(requested);
                if (typeface.TryGetGlyphTypeface(out GlyphTypeface gt))
                {
                    glyphs = gt;
                    resolvedFamily = FirstName(typeface.FontFamily);
                }
            }
            catch { /* fall through to the not-installed verdict */ }

            if (glyphs == null)
                return new GlyphCoverage { FontName = requested, Verdict = FontVerdict.NotInstalled };

            // GUARD 2: Typeface() happily substitutes. "Arial Narrow" resolves to Arial and would
            // otherwise be reported as present, so the document would print in the wrong font
            // while the audit said everything was fine.
            if (!IsInstalled(requested))
            {
                return new GlyphCoverage
                {
                    FontName = requested,
                    Verdict = FontVerdict.NotInstalled,
                    SubstitutedBy = resolvedFamily,
                    FontFile = SafeUri(glyphs),
                };
            }

            // GUARD 1: symbol cmaps alias the Latin-1 range; accent rules cannot apply.
            bool isSymbol = SafeSymbol(glyphs);

            // GUARD 3: TryGetValue only — never enumerate the map.
            GlyphCoverage coverage = GlyphVerdict.Evaluate(requested, isSymbol,
                codepoint => Covers(glyphs, codepoint));

            // GUARD 4: FontUri.Fragment carries the TTC face index.
            coverage.FontFile = SafeUri(glyphs);
            return coverage;
        }

        /// <summary>Checks many fonts, reusing the installed-name set.</summary>
        public List<GlyphCoverage> CheckAll(IEnumerable<string> fontNames)
        {
            var results = new List<GlyphCoverage>();
            if (fontNames == null) return results;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in fontNames)
            {
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name.Trim())) continue;
                results.Add(Check(name));
            }
            return results;
        }

        private static bool Covers(GlyphTypeface glyphs, int codepoint)
        {
            try
            {
                // A cmap entry mapping to glyph 0 is .notdef — which DRAWS A HOLLOW BOX, so it must
                // count as missing, not as covered.
                return glyphs.CharacterToGlyphMap.TryGetValue(codepoint, out ushort glyphIndex)
                       && glyphIndex != 0;
            }
            catch { return false; }
        }

        private static bool SafeSymbol(GlyphTypeface glyphs)
        {
            try { return glyphs.Symbol; }
            catch { return false; }
        }

        private static string SafeUri(GlyphTypeface glyphs)
        {
            try
            {
                Uri uri = glyphs.FontUri;
                // Fragment holds the face index inside a .ttc collection.
                return string.IsNullOrEmpty(uri.Fragment)
                    ? uri.LocalPath
                    : uri.LocalPath + uri.Fragment;
            }
            catch { return ""; }
        }

        private static string FirstName(FontFamily family)
        {
            try { return family.FamilyNames.Values.FirstOrDefault() ?? family.Source; }
            catch { return ""; }
        }
    }
}
