using System.Collections.Generic;
using System.Text;

namespace Optimus.Core.I18n
{
    /// <summary>
    /// Builds the script that hands the string catalog to a WebView2 page.
    ///
    /// <para>
    /// It lives in <c>Optimus.Core</c> because it is pure string work — which is what makes it testable on
    /// .NET 8 without a browser, a COM host or a Windows API. The hosts only inject what this returns.
    /// </para>
    /// <para>
    /// It is injected with <c>AddScriptToExecuteOnDocumentCreatedAsync</c>, which runs <b>before the page's
    /// own script</b>. That ordering is the whole point: the HTML carries no text of its own (every element
    /// is marked <c>data-i18n</c>), so a dictionary that arrived as a normal message would show a flash of
    /// empty labels first — and on a slow machine, not so briefly.
    /// </para>
    /// <para>
    /// JSON is written by hand rather than depending on a serializer: the payload is a flat string→string
    /// map, and the escaping rules for that are small enough to get right and to test.
    /// </para>
    /// </summary>
    public static class I18nScript
    {
        /// <summary>The full script: dictionary, language tag, the <c>T()</c> resolver and <c>applyI18n()</c>.</summary>
        public static string Build(LocalizationService localization)
        {
            Language language = localization.Current;
            var sb = new StringBuilder(64 * 1024);

            sb.Append("window.OPTIMUS_LANG=").Append(Quote(LocalizationService.TagOf(language))).Append(";");
            sb.Append("window.OPTIMUS_LANGS=[\"pt\",\"es\",\"en\"];");
            sb.Append("window.OPTIMUS_LANG_KEY=").Append(Quote(language.ToString().ToLowerInvariant())).Append(";");
            sb.Append("window.OPTIMUS_I18N=").Append(Json(localization.Dictionary())).Append(";");

            // The resolver the pages use. Missing key → the key itself, so a gap is visible and reportable,
            // never an empty button. Variadic: some sentences carry five values (the colour audit line).
            sb.Append(@"window.T=function(k){")
              .Append(@"var s=(window.OPTIMUS_I18N&&window.OPTIMUS_I18N[k])||k;")
              .Append(@"for(var i=1;i<arguments.length;i++){")
              .Append(@"s=s.split('{'+(i-1)+'}').join(String(arguments[i]));}")
              .Append(@"return s;};");

            // Fills every element the HTML marked. Runs on DOMContentLoaded, and can be re-run on a
            // subtree after a dynamic render.
            sb.Append(@"window.applyI18n=function(root){")
              .Append(@"var scope=root||document;")
              .Append(@"var n=scope.querySelectorAll('[data-i18n]');")
              .Append(@"for(var i=0;i<n.length;i++){n[i].textContent=window.T(n[i].getAttribute('data-i18n'));}")
              .Append(@"var t=scope.querySelectorAll('[data-i18n-title]');")
              .Append(@"for(var j=0;j<t.length;j++){t[j].setAttribute('title',window.T(t[j].getAttribute('data-i18n-title')));}")
              .Append(@"var p=scope.querySelectorAll('[data-i18n-placeholder]');")
              .Append(@"for(var k=0;k<p.length;k++){p[k].setAttribute('placeholder',window.T(p[k].getAttribute('data-i18n-placeholder')));}")
              .Append(@"try{document.documentElement.setAttribute('lang',window.OPTIMUS_LANG);}catch(e){}};")
              .Append(@"document.addEventListener('DOMContentLoaded',function(){window.applyI18n();});");

            return sb.ToString();
        }

        /// <summary>The catalog alone, as a JSON object literal.</summary>
        public static string Json(IReadOnlyDictionary<string, string> dictionary)
        {
            var sb = new StringBuilder(48 * 1024);
            sb.Append('{');

            bool first = true;
            foreach (KeyValuePair<string, string> kv in dictionary)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Quote(kv.Key)).Append(':').Append(Quote(kv.Value));
            }

            return sb.Append('}').ToString();
        }

        /// <summary>
        /// JSON string literal. Escapes the control characters plus <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c>
        /// — a literal <c>&lt;/script&gt;</c> inside a value would otherwise terminate the injected block,
        /// which is both a rendering bug and an injection vector.
        /// </summary>
        public static string Quote(string? value)
        {
            var sb = new StringBuilder((value?.Length ?? 0) + 16);
            sb.Append('"');

            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '<': sb.Append("\\u003c"); break;
                    case '>': sb.Append("\\u003e"); break;
                    case '&': sb.Append("\\u0026"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
