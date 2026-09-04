using System;
using System.Collections.Generic;
using Optimus.Core.Corel;

namespace Optimus.Interop
{
    /// <summary>
    /// Collects the font names actually USED by text in the document.
    ///
    /// <para>
    /// Two sources, and the difference matters. <c>font/fontTable.dat</c> inside the file lists
    /// REFERENCED names and can be read with no CorelDRAW at all — but it may include leftovers. This
    /// class asks the live document instead, which is authoritative for "what is on the page right
    /// now".
    /// </para>
    /// <para>
    /// Verified members: <c>Page.FindShapes(, cdrTextShape, true)</c>, <c>Shape.Text.Story</c> →
    /// <c>TextRange</c> whose <c>Font</c> is a STRING, and
    /// <c>TextRange.EnumRanges(cdrTextPropertyFont = 4)</c> for per-run fonts — needed because
    /// <c>Story.Font</c> comes back empty on a mixed-font story, which would silently drop fonts.
    /// </para>
    /// </summary>
    public sealed class FontCollector
    {
        public sealed class Result
        {
            /// <summary>Distinct font names found on the page.</summary>
            public List<string> Fonts { get; } = new List<string>();

            /// <summary>Text shapes inspected.</summary>
            public int TextShapes { get; set; }

            /// <summary>Stories holding more than one font — where a naive read loses names.</summary>
            public int MixedFontStories { get; set; }
        }

        public Result Collect(dynamic page)
        {
            var result = new Result();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            object? range = FindText(page);
            if (range == null) return result;

            int count;
            try { count = (int)((dynamic)range).Count; }
            catch { Release(range); return result; }

            dynamic shapes = range;
            for (int i = 1; i <= count; i++)
            {
                object? shape = null;
                try { shape = shapes[i]; } catch { continue; }
                if (shape == null) continue;

                try
                {
                    result.TextShapes++;
                    int before = seen.Count;
                    int added = CollectFromShape(shape, seen);
                    if (added > 1) result.MixedFontStories++;
                    _ = before;
                }
                finally { Release(shape); }
            }

            Release(range);
            result.Fonts.AddRange(seen);
            result.Fonts.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>Returns how many distinct fonts this shape contributed.</summary>
        private static int CollectFromShape(object shape, HashSet<string> into)
        {
            object? story = null;
            try
            {
                story = ((dynamic)shape).Text.Story;
                if (story == null) return 0;

                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Per-run enumeration first: a mixed-font story returns an EMPTY .Font, so relying on
                // it alone would silently drop every font in the shape.
                object? ranges = null;
                try { ranges = ((dynamic)story).EnumRanges(CorelConstants.CdrTextPropertyFont); }
                catch { ranges = null; }

                if (ranges != null)
                {
                    try
                    {
                        int n = (int)((dynamic)ranges).Count;
                        for (int i = 1; i <= n; i++)
                        {
                            object? run = null;
                            try
                            {
                                run = ((dynamic)ranges)[i];
                                string name = SafeFont(run);
                                if (name.Length > 0) found.Add(name);
                            }
                            catch { }
                            finally { Release(run); }
                        }
                    }
                    catch { }
                    finally { Release(ranges); }
                }

                // Fall back to the whole-story font when the per-run walk found nothing.
                if (found.Count == 0)
                {
                    string whole = SafeFont(story);
                    if (whole.Length > 0) found.Add(whole);
                }

                foreach (string f in found) into.Add(f);
                return found.Count;
            }
            catch { return 0; }
            finally { Release(story); }
        }

        private static string SafeFont(object? textRange)
        {
            if (textRange == null) return "";
            try
            {
                object? value = ((dynamic)textRange).Font;   // a STRING, not an object
                return value == null ? "" : value.ToString()!.Trim();
            }
            catch { return ""; }
        }

        /// <summary>Installed fonts as CorelDRAW itself sees them — the cross-check for "missing font".</summary>
        public static List<string> InstalledPerCorel(dynamic app)
        {
            var names = new List<string>();
            try
            {
                dynamic list = app.FontList;
                int n = (int)list.Count;
                for (int i = 1; i <= n; i++)
                {
                    try
                    {
                        object? item = list[i];   // FontList.Item returns a STRING
                        if (item != null) names.Add(item.ToString()!.Trim());
                    }
                    catch { }
                }
            }
            catch { /* older hosts may not expose FontList */ }
            return names;
        }

        private static object? FindText(dynamic page)
        {
            try { return page.FindShapes(Type.Missing, CorelConstants.CdrTextShape, true); }
            catch { return null; }
        }

        private static void Release(object? comObject)
        {
            if (comObject == null) return;
            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
            }
            catch { }
        }
    }
}
