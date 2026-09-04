using System.Collections.Generic;

namespace Optimus.Core.Audit
{
    /// <summary>How badly a missing character hurts.</summary>
    public enum CharTier
    {
        /// <summary>Portuguese cannot be typeset without it. A miss FAILS the font.</summary>
        Required,

        /// <summary>Common in commercial print (º ª — “ ”). A miss is a warning.</summary>
        Warn,

        /// <summary>Occasional (ü ñ € ©). A miss is information only.</summary>
        Info,

        /// <summary>Combining marks. Reported, NEVER a failure — see the remarks.</summary>
        Combining,
    }

    /// <summary>
    /// The codepoints a font needs to set Brazilian Portuguese, in tiers.
    ///
    /// <para>
    /// Grounded in the CLDR <c>pt</c> exemplar characters, and TIERED because a flat list produces
    /// false alarms: measured across 76 installed text fonts, the required set was universally present,
    /// while "€" was missing in 8 and typographic quotes in 6. Failing a font over "€" would train the
    /// operator to ignore the warning.
    /// </para>
    /// <para>
    /// Combining marks are deliberately never a failure: Impact, Segoe UI Emoji, Ink Free and Sylfaen
    /// all lack them while having perfect precomposed coverage, and Portuguese in NFC never needs them.
    /// </para>
    /// </summary>
    public static class PtBrCharset
    {
        /// <summary>Precomposed accented letters Portuguese cannot do without (24).</summary>
        public static readonly int[] Required =
        {
            0x00E1, 0x00E0, 0x00E2, 0x00E3, 0x00E7, 0x00E9, 0x00EA, 0x00ED,
            0x00F3, 0x00F4, 0x00F5, 0x00FA,
            0x00C1, 0x00C0, 0x00C2, 0x00C3, 0x00C7, 0x00C9, 0x00CA, 0x00CD,
            0x00D3, 0x00D4, 0x00D5, 0x00DA,
        };

        /// <summary>Punctuation and marks a print job normally needs (12).</summary>
        public static readonly int[] Warn =
        {
            0x00AA, 0x00BA, 0x00B0, 0x00A7,
            0x2013, 0x2014,
            0x201C, 0x201D, 0x2018, 0x2019,
            0x2026, 0x0024,   // ellipsis and $ (for R$)
        };

        /// <summary>Occasional in proper nouns, borrowings and symbols (12).</summary>
        public static readonly int[] Info =
        {
            0x00FC, 0x00DC, 0x00F1, 0x00D1, 0x00F2, 0x00D2,
            0x20AC, 0x00A9, 0x00AE, 0x2122, 0x00AB, 0x00BB,
        };

        /// <summary>Combining diacritics — reported only. NFC Portuguese never needs them.</summary>
        public static readonly int[] Combining =
        {
            0x0301, 0x0300, 0x0302, 0x0303, 0x0308, 0x0327,
        };

        /// <summary>Human-readable name of a codepoint, for the report.</summary>
        public static string Describe(int codepoint)
        {
            switch (codepoint)
            {
                case 0x00AA: return "ª (ordinal feminino)";
                case 0x00BA: return "º (ordinal masculino)";
                case 0x00B0: return "° (grau)";
                case 0x00A7: return "§ (parágrafo)";
                case 0x2013: return "– (meia risca)";
                case 0x2014: return "— (travessão)";
                case 0x201C: return "“ (abre aspas)";
                case 0x201D: return "” (fecha aspas)";
                case 0x2018: return "‘ (abre aspas simples)";
                case 0x2019: return "’ (apóstrofo tipográfico)";
                case 0x2026: return "… (reticências)";
                case 0x0024: return "$ (usado em R$)";
                case 0x20AC: return "€ (euro)";
                case 0x00A9: return "© (copyright)";
                case 0x00AE: return "® (marca registrada)";
                case 0x2122: return "™ (trademark)";
                case 0x00AB: return "« (aspas angulares)";
                case 0x00BB: return "» (aspas angulares)";
                default:
                    return codepoint >= 0x0300 && codepoint <= 0x036F
                        ? $"marca combinante U+{codepoint:X4}"
                        : char.ConvertFromUtf32(codepoint);
            }
        }

        public static IEnumerable<(int Codepoint, CharTier Tier)> All()
        {
            foreach (int c in Required) yield return (c, CharTier.Required);
            foreach (int c in Warn) yield return (c, CharTier.Warn);
            foreach (int c in Info) yield return (c, CharTier.Info);
            foreach (int c in Combining) yield return (c, CharTier.Combining);
        }
    }
}
