using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Optimus.Core.I18n;
using Optimus.Core.Maintenance;
using Optimus.Core.Optimization;
using Xunit;

namespace Optimus.Core.Tests.I18n;

/// <summary>
/// The translation guards. Every one of these exists so that a half-translated screen cannot reach the
/// customer: adding a string in pt-BR only, or leaving a placeholder out of one language, fails the build
/// instead of shipping a blank label or a broken sentence.
/// </summary>
public class LocalizedStringsTests
{
    [Fact]
    public void There_are_keys_at_all()
    {
        Assert.True(LocalizedStrings.Keys.Count > 200,
            "o catálogo encolheu — alguém removeu chaves sem querer?");
    }

    /// <summary>
    /// THE test the plan asks for: a key added only in Portuguese must fail here.
    ///
    /// <para>
    /// It asserts on EXPLICIT declaration, not on the text. Comparing values would produce false failures
    /// on every string Portuguese and Spanish happen to share — "Copiar", "Moderado", "Objetos",
    /// "Resultado", "Segura" — and would tempt someone to paraphrase a correct translation just to make a
    /// test pass.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_key_exists_in_all_three_languages()
    {
        var missing = new List<string>();

        foreach (string key in LocalizedStrings.Keys)
            foreach (Language language in LocalizedStrings.Languages)
                if (!LocalizedStrings.HasExplicit(language, key))
                    missing.Add(language + ": " + key);

        Assert.True(missing.Count == 0,
            "chaves ausentes em algum idioma:\n  " + string.Join("\n  ", missing.Take(40)));
    }

    /// <summary>No language may declare a key pt-BR does not have — that key would never be served.</summary>
    [Fact]
    public void No_language_declares_an_orphan_key()
    {
        var known = new HashSet<string>(LocalizedStrings.Keys);
        var orphans = new List<string>();

        foreach (Language language in LocalizedStrings.Languages)
            foreach (string key in LocalizedStrings.All(language).Keys)
                if (!known.Contains(key)) orphans.Add(language + ": " + key);

        Assert.True(orphans.Count == 0, "chaves órfãs:\n  " + string.Join("\n  ", orphans));
    }

    [Fact]
    public void No_value_is_empty_or_whitespace()
    {
        foreach (string key in LocalizedStrings.Keys)
            foreach (Language language in LocalizedStrings.Languages)
                Assert.False(string.IsNullOrWhiteSpace(LocalizedStrings.Get(language, key)),
                             language + ":" + key + " está vazia");
    }

    /// <summary>
    /// A translation that drops a <c>{0}</c> produces a sentence with a missing number — the kind of defect
    /// that only shows up in front of the customer.
    /// </summary>
    [Fact]
    public void Placeholders_match_across_languages()
    {
        var mismatched = new List<string>();

        foreach (string key in LocalizedStrings.Keys)
        {
            HashSet<string> expected = Placeholders(LocalizedStrings.Get(Language.Pt, key));

            foreach (Language language in LocalizedStrings.Languages)
            {
                HashSet<string> actual = Placeholders(LocalizedStrings.Get(language, key));
                if (!expected.SetEquals(actual))
                    mismatched.Add(language + ": " + key
                        + " (esperado " + string.Join(",", expected.OrderBy(x => x))
                        + " / obtido " + string.Join(",", actual.OrderBy(x => x)) + ")");
            }
        }

        Assert.True(mismatched.Count == 0,
            "marcadores divergentes:\n  " + string.Join("\n  ", mismatched.Take(40)));
    }

    private static HashSet<string> Placeholders(string text) =>
        new HashSet<string>(Regex.Matches(text, @"\{\d+\}").Cast<Match>().Select(m => m.Value));

    /// <summary>An unknown key must be visible as itself, never as an empty label.</summary>
    [Fact]
    public void An_unknown_key_returns_the_key()
    {
        Assert.Equal("chave.que.nao.existe", LocalizedStrings.Get(Language.Pt, "chave.que.nao.existe"));
    }

    [Fact]
    public void The_full_dictionary_covers_every_key()
    {
        foreach (Language language in LocalizedStrings.Languages)
            Assert.Equal(LocalizedStrings.Keys.Count, LocalizedStrings.All(language).Count);
    }

    // ── the honesty rules must survive translation ──────────────────────────────

    /// <summary>
    /// Decision O6 in three languages: the memory screen must EXPLAIN why there is no "clean RAM" button.
    /// If a translator softened it into a feature blurb, the product would be selling the placebo again.
    /// </summary>
    [Theory]
    [InlineData(Language.Pt, "RAM")]
    [InlineData(Language.Es, "RAM")]
    [InlineData(Language.En, "RAM")]
    public void The_no_ram_cleaning_explanation_exists_in_every_language(Language language, string needle)
    {
        string text = LocalizedStrings.Get(language, "mnt.mem.whyram");
        Assert.Contains(needle, text);
        Assert.True(text.Length > 200, "a explicação foi encurtada até perder o argumento");
    }

    /// <summary>The per-category caveat is the anti-inflation guard; it may never be dropped.</summary>
    [Fact]
    public void The_estimate_caveat_exists_in_every_language()
    {
        foreach (Language language in LocalizedStrings.Languages)
        {
            Assert.True(LocalizedStrings.Get(language, "mnt.caveat.over").Length > 60);
            Assert.True(LocalizedStrings.Get(language, "mnt.caveat.plain").Length > 40);
        }
    }

    /// <summary>
    /// Every maintenance operation must be fully labelled in all three languages, or the app would show a
    /// checkbox nobody can evaluate.
    /// </summary>
    [Fact]
    public void Every_catalog_operation_has_label_desc_and_gain()
    {
        foreach (CleanupRule rule in SystemResidueRules.All().Concat(CorelResidueRules.All()))
            foreach (Language language in LocalizedStrings.Languages)
                foreach (string part in new[] { "label", "desc", "gain" })
                {
                    string key = "mnt.op." + rule.Id + "." + part;
                    string value = LocalizedStrings.Get(language, key);
                    Assert.NotEqual(key, value);   // Get returns the key when it is missing
                }
    }

    /// <summary>
    /// The browser-cache promise ("cookies and passwords are not touched") is a commitment, not marketing.
    /// It has to be present in all three languages.
    /// </summary>
    [Fact]
    public void The_browser_cache_promise_survives_translation()
    {
        foreach (Language language in LocalizedStrings.Languages)
        {
            string desc = LocalizedStrings.Get(language, "mnt.op.browser.cache.desc");
            Assert.Contains("cookies", desc, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The two loss-warning lists must stay in lockstep across EVERY slider combination. If they drift, the
    /// operator sees one consequence in Portuguese and a different set in Spanish — and a missing warning is
    /// the product silently hiding what it is about to trade away.
    /// </summary>
    [Fact]
    public void Loss_warning_keys_mirror_the_portuguese_warnings_for_every_combination()
    {
        foreach (ColorFidelity color in Enum.GetValues(typeof(ColorFidelity)).Cast<ColorFidelity>())
        foreach (ImageQuality image in Enum.GetValues(typeof(ImageQuality)).Cast<ImageQuality>())
        foreach (ColorSpaceTarget space in Enum.GetValues(typeof(ColorSpaceTarget)).Cast<ColorSpaceTarget>())
        foreach (bool offPage in new[] { false, true })
        foreach (bool flatten in new[] { false, true })
        {
            var settings = new OptimizationSettings
            {
                Color = color, Image = image, Space = space,
                RemoveOffPage = offPage, FlattenEffects = flatten,
            };

            Assert.Equal(settings.LossWarnings.Count, settings.LossWarningKeys.Count);

            foreach (string key in settings.LossWarningKeys)
                foreach (Language language in LocalizedStrings.Languages)
                    Assert.True(LocalizedStrings.HasExplicit(language, key),
                                key + " não existe em " + language);
        }
    }
}

public class LocalizationServiceTests
{
    [Fact]
    public void The_default_language_is_pt_br()
    {
        Assert.Equal(Language.Pt, new LocalizationService().Current);
    }

    [Fact]
    public void An_unknown_or_empty_tag_falls_back_to_pt()
    {
        Assert.Equal(Language.Pt, LocalizationService.Parse(null));
        Assert.Equal(Language.Pt, LocalizationService.Parse(""));
        Assert.Equal(Language.Pt, LocalizationService.Parse("klingon"));
    }

    [Theory]
    [InlineData("pt", Language.Pt)]
    [InlineData("pt-BR", Language.Pt)]
    [InlineData("es", Language.Es)]
    [InlineData("es-AR", Language.Es)]
    [InlineData("EN", Language.En)]
    [InlineData("en-US", Language.En)]
    public void Tags_are_parsed_the_way_a_ui_sends_them(string tag, Language expected)
    {
        Assert.Equal(expected, LocalizationService.Parse(tag));
    }

    [Fact]
    public void Changing_the_language_persists_it_through_the_injected_store()
    {
        var store = new Dictionary<string, string>();
        var service = new LocalizationService((k, v) => store[k] = v);

        service.SetLanguage(Language.En);

        Assert.Equal("En", store[LocalizationService.LanguagePrefKey]);
        Assert.Equal(Language.En, service.Current);
    }

    [Fact]
    public void A_persisted_choice_is_read_back()
    {
        var store = new Dictionary<string, string> { [LocalizationService.LanguagePrefKey] = "Es" };

        LocalizationService service = LocalizationService.Load(
            k => store.TryGetValue(k, out string? v) ? v : null);

        Assert.Equal(Language.Es, service.Current);
    }

    [Fact]
    public void Loading_with_nothing_persisted_gives_pt()
    {
        Assert.Equal(Language.Pt, LocalizationService.Load(_ => null).Current);
    }

    [Fact]
    public void The_indexer_resolves_in_the_current_language()
    {
        var service = new LocalizationService();
        Assert.Equal(LocalizedStrings.Get(Language.Pt, "opt.btn.run"), service["opt.btn.run"]);

        service.SetLanguage(Language.En);
        Assert.Equal(LocalizedStrings.Get(Language.En, "opt.btn.run"), service["opt.btn.run"]);
    }

    [Fact]
    public void The_html_lang_tag_is_correct_for_each_language()
    {
        Assert.Equal("pt-BR", LocalizationService.TagOf(Language.Pt));
        Assert.Equal("es", LocalizationService.TagOf(Language.Es));
        Assert.Equal("en", LocalizationService.TagOf(Language.En));
    }

    [Fact]
    public void The_dictionary_handed_to_the_page_is_complete()
    {
        var service = new LocalizationService();
        service.SetLanguage(Language.Es);

        Assert.Equal(LocalizedStrings.Keys.Count, service.Dictionary().Count);
        Assert.Equal(LocalizedStrings.Get(Language.Es, "mnt.btn.run"), service.Dictionary()["mnt.btn.run"]);
    }
}
