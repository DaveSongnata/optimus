using System;
using System.Collections.Generic;
using System.Text;

namespace Optimus.Core.Diagnostics
{
    /// <summary>
    /// Extracts the font names a document references from <c>font/fontTable.dat</c>.
    ///
    /// <para>
    /// That entry holds NAMES, not outlines — 696 bytes in a real client file, listing
    /// "Man City Dragon 2324", "MS Gothic", "Arial", "Impact" as UTF-16LE strings. Two consequences:
    /// embedded fonts are NOT a size lever in such a file (and must not be sold as one), and this is
    /// the used-font list available WITHOUT opening CorelDRAW — which the font/accent audit needs.
    /// </para>
    /// <para>
    /// The record layout is undocumented, so names are recovered by scanning for runs of printable
    /// UTF-16LE characters instead of by trusting an assumed structure. That is deliberately
    /// conservative: it can pick up a stray token, so callers treat the result as "referenced names"
    /// and verify against CorelDRAW when exactness matters.
    /// </para>
    /// </summary>
    public static class FontTableReader
    {
        private const int MinNameChars = 3;
        private const int MaxNameChars = 96;

        public static List<string> ReadNames(byte[] buffer)
        {
            var names = new List<string>();
            if (buffer == null || buffer.Length < MinNameChars * 2) return names;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var run = new StringBuilder();

            for (int i = 0; i + 1 < buffer.Length; i += 2)
            {
                // UTF-16LE printable ASCII: low byte in range, high byte zero.
                char c = (char)(buffer[i] | (buffer[i + 1] << 8));
                bool printable = buffer[i + 1] == 0 && buffer[i] >= 0x20 && buffer[i] <= 0x7E;

                if (printable)
                {
                    if (run.Length < MaxNameChars) run.Append(c);
                    continue;
                }

                Flush(run, names, seen);
            }
            Flush(run, names, seen);
            return names;
        }

        private static void Flush(StringBuilder run, List<string> names, HashSet<string> seen)
        {
            if (run.Length >= MinNameChars)
            {
                string candidate = run.ToString().Trim();
                if (LooksLikeFontName(candidate) && seen.Add(candidate)) names.Add(candidate);
            }
            run.Clear();
        }

        /// <summary>
        /// Rejects the non-name tokens the table also contains — script/language labels, version
        /// strings and the tool banner ("Fontself Maker 3.5.8") seen in a real file.
        /// </summary>
        private static bool LooksLikeFontName(string s)
        {
            if (s.Length < MinNameChars) return false;
            if (s.StartsWith("Version", StringComparison.OrdinalIgnoreCase)) return false;
            if (s.IndexOf("Fontself", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            switch (s.ToLowerInvariant())
            {
                case "regular":
                case "bold":
                case "italic":
                case "bold italic":
                case "western":
                case "japanese":
                case "arabic":
                case "cyrillic":
                case "greek":
                case "hebrew":
                case "thai":
                case "chinese":
                case "korean":
                    return false;
            }

            // A name has at least one letter.
            foreach (char c in s) if (char.IsLetter(c)) return true;
            return false;
        }
    }
}
