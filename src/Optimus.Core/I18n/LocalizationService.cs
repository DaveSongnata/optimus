using System;
using System.Collections.Generic;

namespace Optimus.Core.I18n
{
    public enum Language { Pt, Es, En }

    /// <summary>
    /// Resolves UI strings for the current language and persists the choice.
    ///
    /// <para>
    /// Persistence is <b>injected</b> (Optimus has no SQLite and no settings service), which keeps this
    /// class pure and testable: the tests drive it with a dictionary, the add-in with a file under
    /// <c>%LOCALAPPDATA%\Optimus</c>. Same shape as SisCut's <c>LocalizationService</c> — the form is
    /// reused, the content is not.
    /// </para>
    /// <para>
    /// Default is <b>pt-BR</b>: the customer is Brazilian, and a wrong default is worse than a missing
    /// language picker.
    /// </para>
    /// </summary>
    public sealed class LocalizationService
    {
        public const string LanguagePrefKey = "ui.language";

        private readonly Action<string, string>? _persist;

        public LocalizationService(Action<string, string>? persist = null)
        {
            _persist = persist;
        }

        public Language Current { get; private set; } = Language.Pt;

        /// <summary>The localized string for a key in the current language.</summary>
        public string this[string key] => LocalizedStrings.Get(Current, key);

        public void SetLanguage(Language language)
        {
            Current = language;
            _persist?.Invoke(LanguagePrefKey, language.ToString());
        }

        /// <summary>
        /// Accepts what a UI actually sends ("pt", "pt-BR", "es", "en-US") and falls back to pt-BR rather
        /// than throwing — an unknown tag must never leave the operator with a blank interface.
        /// </summary>
        public void SetLanguage(string tag)
        {
            SetLanguage(Parse(tag));
        }

        public static Language Parse(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return Language.Pt;

            string t = tag!.Trim().ToLowerInvariant();
            if (t.StartsWith("es", StringComparison.Ordinal)) return Language.Es;
            if (t.StartsWith("en", StringComparison.Ordinal)) return Language.En;
            return Language.Pt;
        }

        /// <summary>Language tag for the HTML <c>lang</c> attribute and for number formatting.</summary>
        public static string TagOf(Language language)
        {
            switch (language)
            {
                case Language.Es: return "es";
                case Language.En: return "en";
                default: return "pt-BR";
            }
        }

        /// <summary>Builds a service from persisted prefs (defaults to pt-BR when unset or unknown).</summary>
        public static LocalizationService Load(Func<string, string?> read,
                                               Action<string, string>? persist = null)
        {
            var service = new LocalizationService(persist);
            if (read != null) service.Current = Parse(read(LanguagePrefKey));
            return service;
        }

        /// <summary>
        /// The whole catalog for the current language, ready to be injected into the page. The UI holds no
        /// strings of its own, so this is what makes the HTML translatable at all.
        /// </summary>
        public IReadOnlyDictionary<string, string> Dictionary() => LocalizedStrings.All(Current);
    }
}
