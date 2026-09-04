using System;
using System.Globalization;
using System.Text;

namespace Optimus.Core.Voice
{
    /// <summary>What a spoken sentence should make Optimus do.</summary>
    public enum VoiceIntent
    {
        Unknown,
        Analyze,
        Audit,
        Optimize,
        SetLanguagePt,
        SetLanguageEs,
        SetLanguageEn,
    }

    /// <summary>
    /// Turns a speech-to-text transcript into one of a SMALL, fixed set of commands.
    ///
    /// <para>
    /// Deliberately not a general NLU: a live demo on a trade-show floor needs a matcher that never
    /// surprises the operator, so it is a short, ordered list of keyword checks over a normalised
    /// transcript — accent-insensitive (Whisper's punctuation/casing/accent choices vary run to run)
    /// and tolerant of the filler words a spoken sentence carries ("pode otimizar o arquivo pra mim",
    /// "vamos auditar as cores"). Pure logic, zero I/O, so the whole decision surface is testable
    /// without a microphone or a network call — the one part of the voice feature that must not
    /// misfire on stage gets to be the one part covered by fast, deterministic tests.
    /// </para>
    /// <para>
    /// Order matters: a language switch ("muda pra inglês") is checked BEFORE the action keywords,
    /// because a sentence like "troca o idioma para auditar melhor" would otherwise misfire on
    /// "auditar". Keeping the command set small (four actions) is itself a reliability choice — every
    /// extra intent is another way a noisy transcript can pick the wrong one.
    /// </para>
    /// </summary>
    public static class VoiceCommandMatcher
    {
        public static VoiceIntent Match(string? transcript)
        {
            string t = Normalize(transcript);
            if (t.Length == 0) return VoiceIntent.Unknown;

            if (Contains(t, "ingles", "english")) return VoiceIntent.SetLanguageEn;
            if (Contains(t, "espanhol", "espanol", "spanish")) return VoiceIntent.SetLanguageEs;
            if (Contains(t, "portugues", "portuguese")) return VoiceIntent.SetLanguagePt;

            if (Contains(t, "otimiz")) return VoiceIntent.Optimize;              // otimizar/otimize/otimização
            if (Contains(t, "audit", "fonte", " cor ", "cores")) return VoiceIntent.Audit; // auditar / cores e fontes
            if (Contains(t, "analis") || Contains(t, "inventari")) return VoiceIntent.Analyze;

            return VoiceIntent.Unknown;
        }

        private static bool Contains(string haystack, params string[] needles)
        {
            foreach (string n in needles)
                if (haystack.Contains(n)) return true;
            return false;
        }

        /// <summary>Lowercase, strips accents, collapses punctuation to spaces, pads with spaces so a
        /// whole-word needle like <c>" cor "</c> cannot match inside "cortar".</summary>
        internal static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            string decomposed = text!.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length + 2);
            sb.Append(' ');
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                char lower = char.ToLowerInvariant(c);
                sb.Append(char.IsLetterOrDigit(lower) ? lower : ' ');
            }
            sb.Append(' ');

            // Collapse runs of spaces so "  " never breaks a padded needle match.
            string spaced = sb.ToString();
            var result = new StringBuilder(spaced.Length);
            bool lastWasSpace = false;
            foreach (char c in spaced)
            {
                if (c == ' ')
                {
                    if (lastWasSpace) continue;
                    lastWasSpace = true;
                }
                else lastWasSpace = false;
                result.Append(c);
            }
            return result.ToString();
        }
    }
}
