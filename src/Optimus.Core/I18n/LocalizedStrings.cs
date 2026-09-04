using System.Collections.Generic;
using System.Linq;

namespace Optimus.Core.I18n
{
    /// <summary>
    /// The UI string catalog in PT/ES/EN.
    ///
    /// <para>
    /// <b>pt-BR is authoritative</b> — it is the language the product was designed in and the one the
    /// customer reads; ES and EN mirror it key by key. A test enforces that every pt key exists in the
    /// other two and that none is empty, so a string added in one language only fails the build rather
    /// than shipping a half-translated screen.
    /// </para>
    /// <para>
    /// The catalog is split into partial-class parts by area (<c>opt.*</c> for the CorelDRAW docker,
    /// <c>mnt.*</c> for the maintenance app) to keep every file under 500 lines.
    /// </para>
    /// </summary>
    public static partial class LocalizedStrings
    {
        public static readonly IReadOnlyList<Language> Languages =
            new[] { Language.Pt, Language.Es, Language.En };

        private static Dictionary<string, string>? _pt;
        private static Dictionary<string, string>? _es;
        private static Dictionary<string, string>? _en;

        private static Dictionary<string, string> Pt => _pt ??= Build(OptimizerPt(), MaintenancePt(), OpsPt());
        private static Dictionary<string, string> Es => _es ??= Build(OptimizerEs(), MaintenanceEs(), OpsEs());
        private static Dictionary<string, string> En => _en ??= Build(OptimizerEn(), MaintenanceEn(), OpsEn());

        private static Dictionary<string, string> Build(params Dictionary<string, string>[] parts)
        {
            var merged = new Dictionary<string, string>();
            foreach (Dictionary<string, string> part in parts)
                foreach (KeyValuePair<string, string> kv in part)
                    merged[kv.Key] = kv.Value;
            return merged;
        }

        private static Dictionary<string, string> For(Language language)
        {
            switch (language)
            {
                case Language.Es: return Es;
                case Language.En: return En;
                default: return Pt;
            }
        }

        /// <summary>All keys, from the authoritative pt-BR catalog.</summary>
        public static IReadOnlyCollection<string> Keys => Pt.Keys.ToList();

        /// <summary>
        /// The localized string for <paramref name="key"/>. Falls back to pt-BR and then to the key
        /// itself: a missing translation must show readable text, never an empty label.
        /// </summary>
        public static string Get(Language language, string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (For(language).TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
                return value;
            return Pt.TryGetValue(key, out string? fallback) ? fallback : key;
        }

        /// <summary>
        /// True when <paramref name="language"/> declares <paramref name="key"/> itself, rather than
        /// inheriting it from the pt-BR fallback.
        ///
        /// <para>
        /// This is what the completeness test asserts on. Comparing the TEXT would be wrong: Portuguese and
        /// Spanish legitimately coincide on many strings ("Copiar", "Moderado", "Objetos", "Resultado"), so
        /// an identical value proves nothing — only an explicitly declared key does.
        /// </para>
        /// </summary>
        public static bool HasExplicit(Language language, string key) =>
            For(language).ContainsKey(key);

        /// <summary>The full dictionary for one language, with pt-BR filling any gap.</summary>
        public static IReadOnlyDictionary<string, string> All(Language language)
        {
            var result = new Dictionary<string, string>(Pt.Count);
            foreach (string key in Pt.Keys) result[key] = Get(language, key);
            return result;
        }
    }
}
