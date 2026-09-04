using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Optimus.Core.I18n;
using Xunit;

namespace Optimus.Core.Tests.I18n;

/// <summary>
/// Checks the two operator UIs against the catalog.
///
/// <para>
/// These tests read the real <c>index.html</c> files from the repository, so they catch the failure mode
/// unit tests cannot: a <c>data-i18n</c> attribute or a <c>T('…')</c> call whose key nobody ever added to
/// the catalog. That renders as the raw key on screen — visible to the customer, invisible to the compiler.
/// </para>
/// </summary>
public class UiKeyCoverageTests
{
    private static readonly string[] UiFiles =
    {
        @"src\Optimus.AddIn\wwwroot\index.html",
        @"src\Optimus.Maintenance\wwwroot\index.html",
    };

    /// <summary>
    /// Walks up from the test binary to the repository root. Without this the tests would silently pass by
    /// finding no files at all — which is why the vacuity guard below exists.
    /// </summary>
    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Optimus.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static List<string> ExistingUiFiles()
    {
        string? root = RepoRoot();
        if (root == null) return new List<string>();

        return UiFiles.Select(f => Path.Combine(root, f)).Where(File.Exists).ToList();
    }

    /// <summary>
    /// Guards against a vacuous green: if the UI files cannot be located, every test below would trivially
    /// pass while checking nothing.
    /// </summary>
    [Fact]
    public void Both_ui_files_are_reachable_so_the_other_tests_are_not_vacuous()
    {
        Assert.Equal(UiFiles.Length, ExistingUiFiles().Count);
    }

    [Fact]
    public void Every_data_i18n_attribute_points_at_a_real_key()
    {
        var missing = new List<string>();
        var seen = 0;

        foreach (string file in ExistingUiFiles())
        {
            string html = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(html, @"data-i18n(?:-title)?=""([^""]+)"""))
            {
                seen++;
                string key = m.Groups[1].Value;
                if (!LocalizedStrings.HasExplicit(Language.Pt, key))
                    missing.Add(Path.GetFileName(Path.GetDirectoryName(file)) + " → " + key);
            }
        }

        Assert.True(seen > 40, "poucos atributos data-i18n encontrados (" + seen + ") — a varredura falhou?");
        Assert.True(missing.Count == 0, "chaves usadas na UI e ausentes no catálogo:\n  "
                                      + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_T_call_with_a_literal_key_points_at_a_real_key()
    {
        var missing = new List<string>();
        var seen = 0;

        foreach (string file in ExistingUiFiles())
        {
            string html = File.ReadAllText(file);

            // Matches T('key') and T("key") — the only forms the pages use for a literal.
            foreach (Match m in Regex.Matches(html, @"\bT\(\s*['""]([a-z][a-z0-9._]+)['""]"))
            {
                seen++;
                string key = m.Groups[1].Value;
                if (!LocalizedStrings.HasExplicit(Language.Pt, key))
                    missing.Add(Path.GetFileName(Path.GetDirectoryName(file)) + " → " + key);
            }
        }

        Assert.True(seen > 40, "poucas chamadas T() encontradas (" + seen + ") — a varredura falhou?");
        Assert.True(missing.Count == 0, "chaves chamadas em T() e ausentes no catálogo:\n  "
                                      + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The keys held in the sliders/factor tables are string literals in JS objects, not T() calls, so this
    /// checks them explicitly — a typo there disables a slider label silently.
    /// </summary>
    [Fact]
    public void Slider_and_factor_tables_reference_real_keys()
    {
        string? root = RepoRoot();
        if (root == null) return;

        string html = File.ReadAllText(Path.Combine(root, @"src\Optimus.AddIn\wwwroot\index.html"));
        var missing = new List<string>();
        var seen = 0;

        foreach (Match m in Regex.Matches(html, @"['""](opt\.(?:sl|flu)\.[a-z0-9._]+)['""]"))
        {
            seen++;
            string key = m.Groups[1].Value;
            if (!LocalizedStrings.HasExplicit(Language.Pt, key)) missing.Add(key);
        }

        Assert.True(seen > 30, "poucas chaves de cursor/fluidez encontradas (" + seen + ")");
        Assert.True(missing.Count == 0, "chaves inexistentes:\n  " + string.Join("\n  ", missing.Distinct()));
    }

    /// <summary>
    /// The UI must hold NO user-facing text of its own. Checked by the cheapest reliable proxy: a Portuguese
    /// accented character outside a comment means a literal survived the extraction.
    /// </summary>
    [Fact]
    public void No_accented_literal_survives_in_either_ui()
    {
        var offenders = new List<string>();

        foreach (string file in ExistingUiFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();

                // Comments are allowed to be in any language — they are documentation, not UI.
                if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*")
                    || trimmed.StartsWith("<!--")) continue;

                if (Regex.IsMatch(line, @"[àáâãäçèéêëìíîïòóôõöùúûü]", RegexOptions.IgnoreCase))
                    offenders.Add(Path.GetFileName(Path.GetDirectoryName(file)) + ":" + (i + 1) + "  " + trimmed);
            }
        }

        Assert.True(offenders.Count == 0,
            "texto literal ainda na UI (deveria vir do catálogo):\n  " + string.Join("\n  ", offenders));
    }
}
