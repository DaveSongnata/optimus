using System;
using System.Collections.Generic;
using System.Linq;
using Optimus.Core.Maintenance;
using Xunit;

namespace Optimus.Core.Tests.Maintenance;

/// <summary>
/// Guards on the system-wide rules. These tests exist because the worst defects possible in this module
/// are not crashes — they are a rule that quietly reaches the wrong folder.
/// </summary>
public class SystemResidueRulesTests
{
    [Fact]
    public void Every_rule_declares_a_path_and_an_explanation()
    {
        foreach (CleanupRule rule in SystemResidueRules.All())
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Path), rule.Id + " precisa de um caminho");
            Assert.True(rule.Description.Length > 30, rule.Id + " precisa de uma explicação real");
        }
    }

    [Fact]
    public void Rule_ids_are_unique_across_both_rule_sets()
    {
        var ids = SystemResidueRules.All().Select(r => r.Id)
            .Concat(CorelResidueRules.All().Select(r => r.Id)).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>The 72 h gate is the promise that an open CorelDRAW document is never damaged.</summary>
    [Fact]
    public void Temp_sweeps_keep_the_seventy_two_hour_gate()
    {
        Assert.Equal(72, SystemResidueRules.UserTemp().MinAgeHours);
        Assert.Equal(72, SystemResidueRules.MachineTemp().MinAgeHours);
    }

    /// <summary>
    /// The broad temp sweep intentionally has no patterns — everything in a Temp folder is temporary.
    /// The age gate, not a filename filter, is what makes it safe.
    /// </summary>
    [Fact]
    public void The_broad_temp_sweep_relies_on_the_age_gate_not_on_patterns()
    {
        CleanupRule rule = SystemResidueRules.UserTemp();
        Assert.Empty(rule.Patterns);
        Assert.True(rule.Matches("qualquer-coisa.dat"));

        DateTime now = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(rule.IsOldEnough(now.AddHours(-2), now));
    }

    /// <summary>Temp is per user, and elevation makes the process's own %TEMP% the admin's.</summary>
    [Fact]
    public void User_temp_is_resolved_per_profile()
    {
        Assert.True(SystemResidueRules.UserTemp().PerUserProfile);
        Assert.Contains(@"AppData\Local\Temp", SystemResidueRules.UserTemp().Path);
    }

    // ── crash evidence ───────────────────────────────────────────────────────────

    [Fact]
    public void Crash_dumps_preserve_the_five_newest()
    {
        CleanupRule rule = SystemResidueRules.CrashEvidence();
        Assert.Equal(5, rule.KeepNewest);
        Assert.True(rule.MinAgeHours >= 24 * 7);
    }

    [Theory]
    [InlineData("MEMORY.dmp", true)]
    [InlineData("011425-12345-01.dmp", true)]
    [InlineData("Report.wer", true)]
    [InlineData("arte-do-cliente.cdr", false)]
    [InlineData("relatorio.pdf", false)]
    public void Crash_evidence_matches_only_dump_and_report_files(string fileName, bool expected)
    {
        Assert.Equal(expected, SystemResidueRules.CrashEvidence().Matches(fileName));
    }

    // ── browser caches: the boundary that must never be crossed ─────────────────

    /// <summary>
    /// Every browser path must end inside a cache folder. A shop that loses the saved login to its
    /// client's file-transfer site will blame the tool, and it would be right.
    /// </summary>
    [Fact]
    public void Browser_rule_only_ever_points_at_cache_folders()
    {
        CleanupRule rule = SystemResidueRules.BrowserCaches();

        var paths = new List<string> { rule.Path };
        paths.AddRange(rule.ExtraPaths);

        Assert.NotEmpty(paths);
        foreach (string path in paths)
        {
            string leaf = path.Substring(path.LastIndexOf('\\') + 1);
            Assert.True(
                leaf.IndexOf("cache", StringComparison.OrdinalIgnoreCase) >= 0,
                "caminho de navegador não termina numa pasta de cache: " + path);
        }
    }

    [Fact]
    public void Browser_rule_never_names_cookies_logins_history_or_bookmarks()
    {
        CleanupRule rule = SystemResidueRules.BrowserCaches();
        var all = new List<string> { rule.Path };
        all.AddRange(rule.ExtraPaths);
        all.AddRange(rule.Patterns);

        foreach (string entry in all)
            foreach (string forbidden in new[] { "Cookies", "Login Data", "History", "Bookmarks", "Web Data" })
                Assert.DoesNotContain(forbidden, entry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Browser_rule_needs_confirmation_and_is_per_profile()
    {
        CleanupRule rule = SystemResidueRules.BrowserCaches();
        Assert.Equal(RiskLevel.NeedsConfirmation, rule.Risk);
        Assert.True(rule.PerUserProfile);
    }

    /// <summary>Firefox hides its cache behind a random profile folder, hence the wildcard segment.</summary>
    [Fact]
    public void Firefox_path_uses_a_wildcard_segment()
    {
        Assert.Contains(SystemResidueRules.BrowserCaches().ExtraPaths,
                        p => p.Contains("Firefox") && p.Contains("*"));
    }

    // ── the two placebos must stay out ──────────────────────────────────────────

    /// <summary>
    /// Decision O6, enforced by a test: no rule may ever target Prefetch, and nothing here may pretend
    /// to clean RAM. Both were measured as net-negative.
    /// </summary>
    [Fact]
    public void No_rule_touches_prefetch_or_claims_to_clean_ram()
    {
        foreach (CleanupRule rule in SystemResidueRules.All().Concat(CorelResidueRules.All()))
        {
            var text = new List<string> { rule.Id, rule.Label, rule.Path };
            text.AddRange(rule.ExtraPaths);

            foreach (string entry in text)
            {
                Assert.DoesNotContain("Prefetch", entry, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("SuperFetch", entry, StringComparison.OrdinalIgnoreCase);
            }

            Assert.DoesNotContain("limpar RAM", rule.Label, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Windows.old must not be reachable: decision documented in the phase's DO NOT WANT list.</summary>
    [Fact]
    public void No_rule_targets_windows_old()
    {
        foreach (CleanupRule rule in SystemResidueRules.All().Concat(CorelResidueRules.All()))
        {
            var paths = new List<string> { rule.Path };
            paths.AddRange(rule.ExtraPaths);
            foreach (string path in paths)
                Assert.DoesNotContain("Windows.old", path, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A rule that deletes from a Windows folder must be a machine rule, not a per-profile one — the two
    /// resolvers are different and mixing them would join a Windows path onto a user profile.
    /// </summary>
    [Fact]
    public void Machine_paths_are_not_marked_as_per_profile()
    {
        foreach (CleanupRule rule in SystemResidueRules.All().Concat(CorelResidueRules.All()))
        {
            if (!rule.PerUserProfile) continue;
            Assert.DoesNotContain("%WINDIR%", rule.Path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("%PROGRAMDATA%", rule.Path, StringComparison.OrdinalIgnoreCase);
            foreach (string extra in rule.ExtraPaths)
            {
                Assert.DoesNotContain("%WINDIR%", extra, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("%PROGRAMDATA%", extra, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
