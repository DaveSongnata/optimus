using System;
using System.Collections.Generic;

namespace Optimus.Core.Maintenance
{
    /// <summary>How dangerous an operation is, which decides whether it may run unattended.</summary>
    public enum RiskLevel
    {
        /// <summary>Nothing unique is lost; Windows or the app rebuilds it.</summary>
        Safe,

        /// <summary>The operator must tick it: it can remove something they wanted.</summary>
        NeedsConfirmation,

        /// <summary>Irreversible and consequential. Requires a restore point and an explicit yes.</summary>
        Dangerous,
    }

    /// <summary>A file-deletion rule, described in the operator's language.</summary>
    public sealed class CleanupRule
    {
        /// <summary>Stable id used in settings and logs.</summary>
        public string Id { get; set; } = "";

        /// <summary>What the operator reads, e.g. "Cópias de segurança do CorelDRAW".</summary>
        public string Label { get; set; } = "";

        /// <summary>Plain-language explanation of what is removed and what the risk is.</summary>
        public string Description { get; set; } = "";

        public RiskLevel Risk { get; set; } = RiskLevel.Safe;

        /// <summary>
        /// Hours a file must be untouched before it may be deleted. Deleting a temp file an open
        /// application still holds is how documents get corrupted — Microsoft's own Disk Cleanup only
        /// removes temp files older than a week, so 0 here is only for things nothing can hold open.
        /// </summary>
        public int MinAgeHours { get; set; } = 72;

        /// <summary>Search patterns, e.g. <c>Backup_of_*.cdr</c>. Empty means every file.</summary>
        public List<string> Patterns { get; } = new List<string>();

        /// <summary>Whether subdirectories are included.</summary>
        public bool Recursive { get; set; } = true;

        /// <summary>True when the rule targets folders inside each USER profile rather than a fixed path.</summary>
        public bool PerUserProfile { get; set; }

        /// <summary>Path relative to the profile (when per-user) or absolute (when not).</summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Further paths the same rule covers, so one operator-visible item ("cache do navegador")
        /// does not have to be split into four checkboxes. A segment may contain <c>*</c> — Firefox
        /// keeps its cache under <c>Profiles\&lt;random&gt;.default\cache2</c>.
        /// </summary>
        public List<string> ExtraPaths { get; } = new List<string>();

        /// <summary>
        /// How many of the newest matches to preserve, regardless of age. Crash dumps are the only
        /// evidence available if the machine keeps crashing, so the recent ones are kept even when the
        /// operator asked to clean up.
        /// </summary>
        public int KeepNewest { get; set; }

        /// <summary>Decides whether a candidate file is old enough to remove.</summary>
        public bool IsOldEnough(DateTime lastWriteUtc, DateTime nowUtc) =>
            MinAgeHours <= 0 || (nowUtc - lastWriteUtc).TotalHours >= MinAgeHours;

        /// <summary>Matches a file name against the rule's patterns (case-insensitive).</summary>
        public bool Matches(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            if (Patterns.Count == 0) return true;
            foreach (string pattern in Patterns)
                if (GlobMatch(fileName, pattern)) return true;
            return false;
        }

        /// <summary>
        /// Minimal glob (<c>*</c> and <c>?</c>) so rules stay declarative without a regex dependency,
        /// and without a regex's catastrophic-backtracking risk on hostile file names.
        /// </summary>
        internal static bool GlobMatch(string text, string pattern)
        {
            int t = 0, p = 0, starP = -1, starT = 0;
            while (t < text.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || Same(pattern[p], text[t]))) { t++; p++; }
                else if (p < pattern.Length && pattern[p] == '*') { starP = p++; starT = t; }
                else if (starP >= 0) { p = starP + 1; t = ++starT; }
                else return false;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }

        private static bool Same(char a, char b) => char.ToLowerInvariant(a) == char.ToLowerInvariant(b);
    }
}
