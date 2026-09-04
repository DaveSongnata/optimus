using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Optimus.Core.Maintenance;

namespace Optimus.Windows.Maintenance
{
    /// <summary>One file the operator will see BEFORE anything is deleted.</summary>
    public sealed class CleanupCandidate
    {
        public string FullPath { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public string RuleId { get; set; } = "";

        /// <summary>Which user profile it belongs to, for a multi-user shop machine.</summary>
        public string Profile { get; set; } = "";

        public override string ToString() =>
            FullPath + " (" + FreedSpaceReport.Format(SizeBytes) + ")";
    }

    /// <summary>What a scan found for one rule.</summary>
    public sealed class CleanupScan
    {
        public string RuleId { get; set; } = "";
        public string Label { get; set; } = "";
        public RiskLevel Risk { get; set; }
        public List<CleanupCandidate> Candidates { get; } = new List<CleanupCandidate>();

        /// <summary>Files that matched the pattern but are too new to touch (the age gate).</summary>
        public int SkippedTooNew { get; set; }

        /// <summary>Folders the scan could not read (permissions, long paths).</summary>
        public int UnreadableFolders { get; set; }

        /// <summary>Newest matches deliberately preserved (see <see cref="CleanupRule.KeepNewest"/>).</summary>
        public int KeptAsEvidence { get; set; }

        public long TotalBytes
        {
            get { long n = 0; foreach (CleanupCandidate c in Candidates) n += c.SizeBytes; return n; }
        }
    }

    /// <summary>
    /// Executes <see cref="CleanupRule"/>s against the real filesystem.
    ///
    /// <para>
    /// Scan and delete are deliberately separate calls: the rule that earns this product its money
    /// (<c>Backup_of_*.cdr</c>) sits <b>next to the customer's artwork</b>, so the operator must see the
    /// list with paths and sizes and tick it, never have it swept silently.
    /// </para>
    /// <para>
    /// The age gate is applied here, at the moment of deletion, and re-checked — a file that was old when
    /// the scan ran may have been reopened while the operator read the list.
    /// </para>
    /// </summary>
    public static class CleanupExecutor
    {
        /// <summary>
        /// Finds everything a rule would remove. Never deletes. Never throws: an unreadable subfolder is
        /// counted, not fatal.
        /// </summary>
        public static CleanupScan Scan(CleanupRule rule, IEnumerable<string> roots,
                                       string profileName = "", DateTime? nowUtc = null)
        {
            var pairs = new List<(string, string)>();
            foreach (string root in roots) pairs.Add((root, profileName));
            return ScanAll(rule, pairs, nowUtc);
        }

        /// <summary>
        /// Scans every (root, profile) pair a rule resolves to, then applies <see cref="CleanupRule.KeepNewest"/>
        /// ACROSS all of them — keeping the five newest dumps per folder would keep twenty-five in total,
        /// which is not what "os 5 mais recentes" means.
        /// </summary>
        public static CleanupScan ScanAll(CleanupRule rule, IEnumerable<(string Root, string Profile)> roots,
                                          DateTime? nowUtc = null)
        {
            var scan = new CleanupScan { RuleId = rule.Id, Label = rule.Label, Risk = rule.Risk };
            DateTime now = nowUtc ?? DateTime.UtcNow;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach ((string root, string profile) in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                if (!Directory.Exists(root)) continue;
                Walk(rule, root, scan, profile, now, depth: 0);
            }

            // Nested roots (e.g. Temp under the profile, plus the profile itself) can surface the same
            // file twice, which would double-count the estimate.
            scan.Candidates.RemoveAll(c => !seen.Add(c.FullPath));

            ApplyKeepNewest(rule, scan);
            return scan;
        }

        /// <summary>
        /// Drops the newest <see cref="CleanupRule.KeepNewest"/> matches from the candidate list. They are
        /// evidence, not garbage: if the machine crashes again next week, these dumps are the only thing
        /// that can explain why.
        /// </summary>
        internal static void ApplyKeepNewest(CleanupRule rule, CleanupScan scan)
        {
            if (rule.KeepNewest <= 0 || scan.Candidates.Count <= rule.KeepNewest)
            {
                if (rule.KeepNewest > 0) { scan.KeptAsEvidence = scan.Candidates.Count; scan.Candidates.Clear(); }
                return;
            }

            scan.Candidates.Sort((a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
            scan.Candidates.RemoveRange(0, rule.KeepNewest);
            scan.KeptAsEvidence = rule.KeepNewest;
        }

        /// <summary>
        /// Manual recursion rather than <c>SearchOption.AllDirectories</c>: the framework enumerator
        /// aborts the WHOLE walk on the first unauthorised folder, which on a shop machine means the scan
        /// silently returns almost nothing.
        /// </summary>
        private static void Walk(CleanupRule rule, string dir, CleanupScan scan,
                                 string profileName, DateTime now, int depth)
        {
            // A guard against reparse-point loops; art folders nest deeply but never 64 levels.
            if (depth > 64) return;

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (Exception) { scan.UnreadableFolders++; return; }

            foreach (string file in files)
            {
                try
                {
                    string name = Path.GetFileName(file);
                    if (!rule.Matches(name)) continue;

                    var info = new FileInfo(file);
                    if (!info.Exists) continue;

                    if (!rule.IsOldEnough(info.LastWriteTimeUtc, now)) { scan.SkippedTooNew++; continue; }

                    scan.Candidates.Add(new CleanupCandidate
                    {
                        FullPath = file,
                        SizeBytes = info.Length,
                        LastWriteUtc = info.LastWriteTimeUtc,
                        RuleId = rule.Id,
                        Profile = profileName,
                    });
                }
                catch (Exception) { /* a file that vanishes mid-scan is normal on a live machine */ }
            }

            if (!rule.Recursive) return;

            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception) { scan.UnreadableFolders++; return; }

            foreach (string sub in subs)
            {
                try
                {
                    // Junctions and symlinks would double-count and can loop.
                    var di = new DirectoryInfo(sub);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch (Exception) { continue; }

                Walk(rule, sub, scan, profileName, now, depth + 1);
            }
        }

        /// <summary>
        /// Deletes the candidates the operator approved, re-checking the age gate. Files another program
        /// holds open are counted as locked, never forced — forcing is how an open document gets corrupted.
        /// </summary>
        public static CategoryResult Delete(CleanupRule rule, IEnumerable<CleanupCandidate> approved,
                                            DateTime? nowUtc = null, Action<string>? log = null)
        {
            var result = new CategoryResult { Id = rule.Id, Label = rule.Label };
            DateTime now = nowUtc ?? DateTime.UtcNow;

            foreach (CleanupCandidate c in approved)
            {
                try
                {
                    var info = new FileInfo(c.FullPath);
                    if (!info.Exists) continue;

                    // Re-check: the operator may have spent ten minutes reading the list.
                    if (!rule.IsOldEnough(info.LastWriteTimeUtc, now))
                    {
                        result.FilesLocked++;
                        log?.Invoke("mantido (voltou a ser usado): " + c.FullPath);
                        continue;
                    }

                    long size = info.Length;
                    info.Delete();

                    result.BytesRemoved += size;
                    result.FilesRemoved++;
                }
                catch (IOException)
                {
                    result.FilesLocked++;
                    log?.Invoke("em uso por outro programa: " + c.FullPath);
                }
                catch (UnauthorizedAccessException)
                {
                    result.FilesLocked++;
                    log?.Invoke("sem permissão: " + c.FullPath);
                }
                catch (Exception ex)
                {
                    result.FilesLocked++;
                    log?.Invoke("falhou (" + ex.GetType().Name + "): " + c.FullPath);
                }
            }

            return result;
        }

        /// <summary>
        /// Resolves the folders a rule should search. Per-user rules expand once per REAL profile — see
        /// <see cref="UserProfiles"/> for why the elevated process's own <c>%TEMP%</c> is the wrong answer.
        /// Rules with no path (the Corel backup sweep) use the roots the operator chose.
        /// </summary>
        public static List<(string Root, string Profile)> ResolveRoots(
            CleanupRule rule, List<UserProfile> profiles, IEnumerable<string>? operatorRoots)
        {
            var roots = new List<(string, string)>();

            var declared = new List<string>();
            if (!string.IsNullOrWhiteSpace(rule.Path)) declared.Add(rule.Path);
            declared.AddRange(rule.ExtraPaths);

            if (rule.PerUserProfile)
            {
                foreach (UserProfile p in profiles)
                    foreach (string decl in declared)
                        foreach (string resolved in ExpandWildcards(UserProfiles.Resolve(decl, true, p)))
                            roots.Add((resolved, p.DisplayName));
                return roots;
            }

            if (declared.Count > 0)
            {
                foreach (string decl in declared)
                    foreach (string resolved in ExpandWildcards(Environment.ExpandEnvironmentVariables(decl)))
                        roots.Add((resolved, ""));
                return roots;
            }

            if (operatorRoots != null)
                foreach (string r in operatorRoots)
                    if (!string.IsNullOrWhiteSpace(r)) roots.Add((r, ""));

            return roots;
        }

        /// <summary>
        /// Expands one <c>*</c> segment in a path. Firefox stores its cache under
        /// <c>Profiles\&lt;random&gt;.default-release\cache2</c>, so a literal path can never find it.
        /// </summary>
        internal static List<string> ExpandWildcards(string path)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(path)) return result;

            if (path.IndexOf('*') < 0) { result.Add(path); return result; }

            string[] parts = path.Split(Path.DirectorySeparatorChar);
            int starIndex = Array.FindIndex(parts, s => s.IndexOf('*') >= 0);
            if (starIndex <= 0) { return result; }

            string head = string.Join(Path.DirectorySeparatorChar.ToString(), parts, 0, starIndex);
            string pattern = parts[starIndex];
            string tail = starIndex + 1 < parts.Length
                ? string.Join(Path.DirectorySeparatorChar.ToString(), parts, starIndex + 1,
                              parts.Length - starIndex - 1)
                : "";

            string[] matches;
            try { matches = Directory.GetDirectories(head, pattern); }
            catch (Exception) { return result; }

            foreach (string match in matches)
            {
                string candidate = string.IsNullOrEmpty(tail) ? match : Path.Combine(match, tail);
                // The tail may itself contain another wildcard.
                result.AddRange(ExpandWildcards(candidate));
            }
            return result;
        }

        /// <summary>
        /// Default places to look for CorelDRAW backup copies when the operator has not chosen a folder:
        /// every real profile's Desktop and Documents, plus the root of each extra fixed drive (shops keep
        /// art on D:).
        /// </summary>
        public static List<string> DefaultArtworkRoots(List<UserProfile> profiles)
        {
            var roots = new List<string>();

            foreach (UserProfile p in profiles)
            {
                Add(Path.Combine(p.ProfilePath, "Desktop"));
                Add(Path.Combine(p.ProfilePath, "Documents"));
                Add(Path.Combine(p.ProfilePath, "Documentos"));
                Add(Path.Combine(p.ProfilePath, "OneDrive"));
            }

            try
            {
                string systemRoot = Path.GetPathRoot(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";

                foreach (VolumeInfo v in DiskHealth.Volumes())
                    if (!v.Root.Equals(systemRoot, StringComparison.OrdinalIgnoreCase))
                        Add(v.Root);
            }
            catch (Exception) { }

            return roots;

            void Add(string path)
            {
                if (!Directory.Exists(path)) return;
                if (roots.Exists(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase))) return;
                roots.Add(path);
            }
        }

        /// <summary>
        /// Measures the drive's own idle drift, which becomes the report's noise floor. Without it, "40 MB
        /// freed" on a machine that fluctuates by 200 MB is a number invented by rounding.
        /// </summary>
        public static long MeasureNoiseFloor(string driveRoot, int sampleMs = 1500, int samples = 3)
        {
            try
            {
                long min = long.MaxValue, max = long.MinValue;
                for (int i = 0; i < Math.Max(2, samples); i++)
                {
                    long free = DiskHealth.FreeBytesOn(driveRoot);
                    if (free < min) min = free;
                    if (free > max) max = free;
                    if (i < samples - 1) Thread.Sleep(Math.Max(0, sampleMs));
                }
                return max > min ? max - min : 0;
            }
            catch (Exception) { return 0; }
        }
    }
}
