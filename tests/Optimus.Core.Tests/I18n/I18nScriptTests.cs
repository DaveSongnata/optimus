using System.Collections.Generic;
using System.Text.Json;
using Optimus.Core.I18n;
using Xunit;

namespace Optimus.Core.Tests.I18n;

/// <summary>
/// Tests for the injected bootstrap script. The JSON is hand-written, so it has to be proven valid on the
/// REAL catalog — which contains quotes, newlines, accents and long paragraphs.
/// </summary>
public class I18nScriptTests
{
    [Theory]
    [InlineData(Language.Pt)]
    [InlineData(Language.Es)]
    [InlineData(Language.En)]
    public void The_catalog_serialises_to_valid_json_in_every_language(Language language)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        string json = I18nScript.Json(service.Dictionary());

        using JsonDocument doc = JsonDocument.Parse(json);   // throws if malformed
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);

        int count = 0;
        foreach (JsonProperty _ in doc.RootElement.EnumerateObject()) count++;
        Assert.Equal(LocalizedStrings.Keys.Count, count);
    }

    [Theory]
    [InlineData(Language.Pt)]
    [InlineData(Language.Es)]
    [InlineData(Language.En)]
    public void Round_tripping_preserves_every_value_exactly(Language language)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        using JsonDocument doc = JsonDocument.Parse(I18nScript.Json(service.Dictionary()));

        foreach (KeyValuePair<string, string> kv in service.Dictionary())
        {
            Assert.True(doc.RootElement.TryGetProperty(kv.Key, out JsonElement value), kv.Key);
            Assert.Equal(kv.Value, value.GetString());
        }
    }

    /// <summary>
    /// A literal <c>&lt;/script&gt;</c> in a value would close the injected block. Escaping the angle
    /// brackets is both a rendering fix and the reason a translated string can never inject markup.
    /// </summary>
    [Fact]
    public void Angle_brackets_and_ampersands_are_escaped()
    {
        string quoted = I18nScript.Quote("</script><b>a & b</b>");

        Assert.DoesNotContain("<", quoted);
        Assert.DoesNotContain(">", quoted);
        Assert.DoesNotContain("&", quoted);
        Assert.Equal("</script><b>a & b</b>", JsonSerializer.Deserialize<string>(quoted));
    }

    [Fact]
    public void Quotes_backslashes_and_newlines_survive()
    {
        const string nasty = "diz \"oi\"\nlinha 2\tC:\\temp\\x";
        Assert.Equal(nasty, JsonSerializer.Deserialize<string>(I18nScript.Quote(nasty)));
    }

    [Fact]
    public void Control_characters_are_escaped_rather_than_emitted_raw()
    {
        string quoted = I18nScript.Quote("ab");
        Assert.Contains("\\u0001", quoted);
        Assert.Equal("ab", JsonSerializer.Deserialize<string>(quoted));
    }

    [Fact]
    public void Null_becomes_an_empty_string_not_a_crash()
    {
        Assert.Equal("\"\"", I18nScript.Quote(null));
    }

    // ── the script as a whole ───────────────────────────────────────────────────

    [Theory]
    [InlineData(Language.Pt, "pt-BR", "pt")]
    [InlineData(Language.Es, "es", "es")]
    [InlineData(Language.En, "en", "en")]
    public void The_script_declares_the_language_the_page_needs(Language language, string tag, string key)
    {
        var service = new LocalizationService();
        service.SetLanguage(language);

        string script = I18nScript.Build(service);

        Assert.Contains("window.OPTIMUS_LANG=\"" + tag + "\"", script);
        Assert.Contains("window.OPTIMUS_LANG_KEY=\"" + key + "\"", script);
    }

    /// <summary>
    /// The page depends on these three globals existing before its own script runs; if any is renamed here
    /// without the HTML being updated, both UIs render as raw key names.
    /// </summary>
    [Fact]
    public void The_script_defines_the_three_globals_the_pages_use()
    {
        string script = I18nScript.Build(new LocalizationService());

        Assert.Contains("window.OPTIMUS_I18N=", script);
        Assert.Contains("window.T=function", script);
        Assert.Contains("window.applyI18n=function", script);
        Assert.Contains("data-i18n", script);
        Assert.Contains("data-i18n-title", script);
    }

    [Fact]
    public void The_script_applies_itself_on_dom_content_loaded()
    {
        Assert.Contains("DOMContentLoaded", I18nScript.Build(new LocalizationService()));
    }
}
