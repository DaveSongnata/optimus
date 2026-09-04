using System;
using System.Linq;
using Optimus.Core.Maintenance;
using Xunit;

namespace Optimus.Core.Tests.Maintenance;

public class CleanupRuleTests
{
    /// <summary>
    /// The age gate exists because deleting a temp file an open application still holds corrupts
    /// documents — Microsoft's own Disk Cleanup only removes temp files older than a week.
    /// </summary>
    [Fact]
    public void A_file_touched_an_hour_ago_is_not_old_enough()
    {
        var rule = new CleanupRule { MinAgeHours = 72 };
        DateTime now = new DateTime(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(rule.IsOldEnough(now.AddHours(-1), now));
        Assert.False(rule.IsOldEnough(now.AddHours(-71), now));
        Assert.True(rule.IsOldEnough(now.AddHours(-73), now));
    }

    [Fact]
    public void A_zero_age_gate_accepts_anything()
    {
        var rule = new CleanupRule { MinAgeHours = 0 };
        DateTime now = DateTime.UtcNow;
        Assert.True(rule.IsOldEnough(now, now));
    }

    // ── pattern matching ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Backup_of_arquivo.cdr", true)]
    [InlineData("Backup_of_kaneki.cdr", true)]
    [InlineData("BACKUP_OF_X.CDR", true)]          // case-insensitive
    [InlineData("arquivo.cdr", false)]             // the customer's real artwork
    [InlineData("Backup_of_arquivo.cdt", false)]   // template, not a backup copy
    [InlineData("meu Backup_of_arquivo.cdr", false)]
    public void Save_backup_rule_matches_only_corel_backup_copies(string fileName, bool expected)
    {
        Assert.Equal(expected, CorelResidueRules.SaveBackups().Matches(fileName));
    }

    /// <summary>
    /// THE rule that must never fire on real artwork. A false positive here deletes a paying job.
    /// </summary>
    [Fact]
    public void The_backup_rule_never_matches_a_working_file()
    {
        CleanupRule rule = CorelResidueRules.SaveBackups();
        foreach (string real in new[] { "arquivo.cdr", "kaneki.cdr", "CAMISA FINAL.cdr", "logo v2.cdr" })
            Assert.False(rule.Matches(real), $"{real} must NOT be treated as a backup");
    }

    [Fact]
    public void A_rule_with_no_patterns_matches_everything()
    {
        Assert.True(new CleanupRule().Matches("anything.xyz"));
    }

    [Fact]
    public void Empty_file_name_never_matches()
    {
        Assert.False(CorelResidueRules.SaveBackups().Matches(""));
        Assert.False(CorelResidueRules.SaveBackups().Matches(null!));
    }

    [Theory]
    [InlineData("thumbcache_256.db", true)]
    [InlineData("thumbcache_idx.db", true)]
    [InlineData("iconcache_16.db", true)]
    [InlineData("importante.db", false)]
    public void Thumbnail_rule_matches_only_cache_databases(string fileName, bool expected)
    {
        Assert.Equal(expected, CorelResidueRules.ThumbnailCache().Matches(fileName));
    }

    [Theory]
    [InlineData("a", "?", true)]
    [InlineData("ab", "?", false)]
    [InlineData("abc", "a*c", true)]
    [InlineData("ac", "a*c", true)]
    [InlineData("abd", "a*c", false)]
    [InlineData("x.SPL", "*.SPL", true)]
    public void Glob_matching_handles_the_basic_cases(string text, string pattern, bool expected)
    {
        Assert.Equal(expected, CleanupRule.GlobMatch(text, pattern));
    }

    // ── risk posture ─────────────────────────────────────────────────────────────

    /// <summary>Anything that touches a `.cdr` must require a tick — that file is the product.</summary>
    [Fact]
    public void Rules_that_touch_cdr_files_require_confirmation()
    {
        foreach (CleanupRule rule in CorelResidueRules.All())
        {
            bool touchesCdr = rule.Patterns.Any(p => p.EndsWith(".cdr", StringComparison.OrdinalIgnoreCase));
            if (touchesCdr)
                Assert.NotEqual(RiskLevel.Safe, rule.Risk);
        }
    }

    [Fact]
    public void Every_rule_has_an_id_a_label_and_an_explanation()
    {
        foreach (CleanupRule rule in CorelResidueRules.All())
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Label));
            Assert.True(rule.Description.Length > 30, $"{rule.Id} needs a real explanation");
        }
    }

    [Fact]
    public void Rule_ids_are_unique()
    {
        var ids = CorelResidueRules.All().Select(r => r.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    /// <summary>The Corel backup rule leads the list: it is the gigabyte win nobody else finds.</summary>
    [Fact]
    public void The_corel_backup_rule_is_presented_first()
    {
        Assert.Equal("corel.savebackups", CorelResidueRules.All()[0].Id);
    }
}

public class FreedSpaceReportTests
{
    private static CategoryResult Cat(string id, long bytes, int files = 1) =>
        new CategoryResult { Id = id, Label = id, BytesRemoved = bytes, FilesRemoved = files };

    /// <summary>The headline is the DISK's gain, never the sum of the categories.</summary>
    [Fact]
    public void The_headline_is_the_real_disk_delta()
    {
        var report = new FreedSpaceReport { FreeBeforeBytes = 10_000_000_000, FreeAfterBytes = 16_840_000_000 };
        report.Add(Cat("temp", 2_410_000_000));
        report.Add(Cat("winsxs", 5_000_000_000));   // hardlinked: overstates the real gain

        Assert.Equal(6_840_000_000, report.RealFreedBytes);
        Assert.True(report.EstimatedBytes > report.RealFreedBytes);
        // 6.84e9 bytes = 6,37 GiB — the headline quotes what Explorer will show, not the decimal count.
        Assert.Contains("6,37 GB", report.Headline().Replace('.', ','));
    }

    /// <summary>
    /// If another process wrote more than we freed, the honest answer is 0 with an explanation —
    /// never a negative number and never the category sum.
    /// </summary>
    [Fact]
    public void A_concurrent_writer_yields_zero_not_a_negative_or_the_estimate()
    {
        var report = new FreedSpaceReport { FreeBeforeBytes = 10_000_000_000, FreeAfterBytes = 9_500_000_000 };
        report.Add(Cat("temp", 500_000_000));

        Assert.Equal(0, report.RealFreedBytes);
        Assert.True(report.AnotherProcessWrote);
        Assert.Contains("0", report.Headline());
        Assert.Contains("outro programa", report.Headline());
    }

    [Fact]
    public void Nothing_to_remove_says_so_plainly()
    {
        var report = new FreedSpaceReport { FreeBeforeBytes = 5_000, FreeAfterBytes = 5_000 };
        Assert.Equal(0, report.RealFreedBytes);
        Assert.Contains("não havia nada", report.Headline());
    }

    /// <summary>False precision is a lie too: 40 MB freed with ±200 MB of drift is "about".</summary>
    [Fact]
    public void A_result_below_the_noise_floor_is_hedged()
    {
        var report = new FreedSpaceReport
        {
            FreeBeforeBytes = 1_000_000_000,
            FreeAfterBytes = 1_040_000_000,
            NoiseFloorBytes = 200_000_000,
        };

        Assert.True(report.BelowNoiseFloor);
        Assert.Contains("cerca de", report.Headline());
    }

    [Fact]
    public void A_result_above_the_noise_floor_is_stated_plainly()
    {
        var report = new FreedSpaceReport
        {
            FreeBeforeBytes = 1_000_000_000,
            FreeAfterBytes = 3_000_000_000,
            NoiseFloorBytes = 50_000_000,
        };

        Assert.False(report.BelowNoiseFloor);
        Assert.DoesNotContain("cerca de", report.Headline());
    }

    /// <summary>The caveat must be present whenever the estimates exceed reality — the normal case.</summary>
    [Fact]
    public void The_breakdown_caveat_explains_why_the_parts_exceed_the_whole()
    {
        var report = new FreedSpaceReport { FreeBeforeBytes = 0, FreeAfterBytes = 1_000_000 };
        report.Add(Cat("a", 900_000));
        report.Add(Cat("b", 900_000));

        Assert.Contains("estimativas", report.BreakdownCaveat());
        Assert.Contains("somam mais", report.BreakdownCaveat());
    }

    [Fact]
    public void Locked_files_are_counted_separately_from_removed_ones()
    {
        var report = new FreedSpaceReport();
        report.Add(new CategoryResult { Id = "temp", FilesRemoved = 10, FilesLocked = 3, BytesRemoved = 1000 });

        Assert.Equal(10, report.TotalFilesRemoved);
        Assert.Equal(3, report.TotalFilesLocked);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(5_242_880, "5 MB")]
    public void Sizes_are_formatted_for_humans(long bytes, string expected)
    {
        Assert.Equal(expected, FreedSpaceReport.Format(bytes));
    }

    [Fact]
    public void Gigabytes_use_two_decimals()
    {
        Assert.Contains("GB", FreedSpaceReport.Format(3_221_225_472));
    }
}
