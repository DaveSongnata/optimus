using System.Collections.Generic;

namespace Optimus.Core.Audit
{
    /// <summary>Verdict for one font.</summary>
    public enum FontVerdict
    {
        /// <summary>Covers everything Portuguese requires.</summary>
        Ok,

        /// <summary>Covers the required letters but misses common punctuation.</summary>
        Warning,

        /// <summary>Missing a required accented letter — this is the case that ruins a print job.</summary>
        Failed,

        /// <summary>Not installed on this machine: CorelDRAW is substituting something else.</summary>
        NotInstalled,

        /// <summary>A symbol font (Wingdings and friends). Accent rules do not apply.</summary>
        SymbolFont,
    }

    /// <summary>What one font covers, and what it is missing.</summary>
    public sealed class GlyphCoverage
    {
        public string FontName { get; set; } = "";
        public FontVerdict Verdict { get; set; } = FontVerdict.Ok;

        /// <summary>Codepoints absent from the font, per tier.</summary>
        public List<int> MissingRequired { get; } = new List<int>();
        public List<int> MissingWarn { get; } = new List<int>();
        public List<int> MissingInfo { get; } = new List<int>();
        public List<int> MissingCombining { get; } = new List<int>();

        /// <summary>Where the font file lives, when resolvable (includes the TTC face index).</summary>
        public string FontFile { get; set; } = "";

        /// <summary>
        /// Set when the requested name resolved to a DIFFERENT font. A silent substitution is the
        /// dangerous case: the operator's document says one font, the machine prints another.
        /// </summary>
        public string SubstitutedBy { get; set; } = "";

        public bool IsUsable => Verdict == FontVerdict.Ok || Verdict == FontVerdict.Warning;

        /// <summary>One-line summary in the operator's language.</summary>
        public string Summary()
        {
            switch (Verdict)
            {
                case FontVerdict.NotInstalled:
                    return string.IsNullOrEmpty(SubstitutedBy)
                        ? "NÃO INSTALADA nesta máquina — o CorelDRAW está trocando por outra fonte."
                        : $"NÃO INSTALADA — está sendo substituída por \"{SubstitutedBy}\".";
                case FontVerdict.SymbolFont:
                    return "Fonte de símbolos — as regras de acento não se aplicam.";
                case FontVerdict.Failed:
                    return "SEM ACENTO: faltam " + Describe(MissingRequired) + ".";
                case FontVerdict.Warning:
                    return "Acentos OK, mas faltam " + Describe(MissingWarn) + ".";
                default:
                    return "Acentos do português OK.";
            }
        }

        private static string Describe(List<int> codepoints)
        {
            var parts = new List<string>();
            for (int i = 0; i < codepoints.Count && i < 6; i++)
                parts.Add(PtBrCharset.Describe(codepoints[i]));
            string text = string.Join(", ", parts);
            if (codepoints.Count > 6) text += $" (+{codepoints.Count - 6})";
            return text;
        }
    }

    /// <summary>
    /// Turns per-codepoint results into a verdict.
    ///
    /// <para>
    /// Pure and separate from the font-file reading on purpose: the tier rules are the part that must
    /// be right (a false "sem acento" trains the operator to ignore warnings, and a false OK lets a
    /// job print with boxes instead of "ç"), so they are tested without needing any font installed.
    /// </para>
    /// </summary>
    public static class GlyphVerdict
    {
        /// <summary>
        /// <paramref name="covered"/> answers "does this font contain this codepoint?".
        /// A symbol font short-circuits: its cmap aliases U+F0xx onto U+00xx, so it would falsely
        /// report having "ç".
        /// </summary>
        public static GlyphCoverage Evaluate(string fontName, bool isSymbolFont, System.Func<int, bool> covered)
        {
            var result = new GlyphCoverage { FontName = fontName };

            if (isSymbolFont)
            {
                result.Verdict = FontVerdict.SymbolFont;
                return result;
            }
            if (covered == null)
            {
                result.Verdict = FontVerdict.NotInstalled;
                return result;
            }

            foreach ((int codepoint, CharTier tier) in PtBrCharset.All())
            {
                if (covered(codepoint)) continue;
                switch (tier)
                {
                    case CharTier.Required: result.MissingRequired.Add(codepoint); break;
                    case CharTier.Warn: result.MissingWarn.Add(codepoint); break;
                    case CharTier.Info: result.MissingInfo.Add(codepoint); break;
                    default: result.MissingCombining.Add(codepoint); break;
                }
            }

            // Combining marks are never a failure, and Info never downgrades the verdict.
            result.Verdict = result.MissingRequired.Count > 0 ? FontVerdict.Failed
                           : result.MissingWarn.Count > 0 ? FontVerdict.Warning
                           : FontVerdict.Ok;
            return result;
        }
    }
}
