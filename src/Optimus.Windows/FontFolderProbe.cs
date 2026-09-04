using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Optimus.Core.Audit;

namespace Optimus.Windows
{
    /// <summary>One font found in a watched folder, with its pt-BR verdict.</summary>
    public sealed class FolderFont
    {
        public string Family { get; set; } = "";
        public string File { get; set; } = "";
        public GlyphCoverage Coverage { get; set; } = new GlyphCoverage();

        /// <summary>True when Windows ALSO has this font installed — so the operator can tell an
        /// activated-from-folder font apart from one that is installed twice.</summary>
        public bool AlsoInstalled { get; set; }
    }

    public sealed class FontFolderResult
    {
        public string Folder { get; set; } = "";
        public List<FolderFont> Fonts { get; } = new List<FolderFont>();
        public int FilesSeen { get; set; }
        public string Error { get; set; } = "";
        public bool Ok => Error.Length == 0;
    }

    /// <summary>
    /// Reads a FOLDER of font files and checks each one's Portuguese coverage — without installing
    /// anything into Windows.
    ///
    /// <para>
    /// WHY THIS EXISTS. Corel Font Manager's whole point is using fonts from a folder WITHOUT
    /// installing them in Windows, which keeps the system light. But <see cref="FontProbe"/> judges
    /// "is this font available" from <c>Fonts.SystemFontFamilies</c> — the WINDOWS-installed list — so
    /// a font the operator legitimately uses through Corel Font Manager would be reported as "NÃO
    /// INSTALADA, está sendo substituída". That is a false alarm, and precisely backwards: the font is
    /// there and works. This class closes that gap by reading the font FILES directly.
    /// </para>
    /// <para>
    /// <c>Fonts.GetFontFamilies(location)</c> enumerates families straight out of a directory, and the
    /// resulting <see cref="GlyphTypeface"/> exposes the same <c>CharacterToGlyphMap</c> (the cmap)
    /// used for installed fonts — so the accent verdict is computed the same way, with the same four
    /// guards, whether the font is installed or merely present on disk.
    /// </para>
    /// </summary>
    public sealed class FontFolderProbe
    {
        private static readonly string[] Extensions = { ".ttf", ".otf", ".ttc", ".otc" };

        public FontFolderResult Scan(string folder)
        {
            var result = new FontFolderResult { Folder = folder ?? "" };

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                result.Error = "Pasta não encontrada.";
                return result;
            }

            var installed = new FontProbe();

            try
            {
                foreach (string ext in Extensions)
                {
                    try { result.FilesSeen += Directory.GetFiles(folder, "*" + ext, SearchOption.TopDirectoryOnly).Length; }
                    catch (Exception) { }
                }

                // A trailing separator is what tells WPF this is a DIRECTORY rather than a file.
                string location = folder.EndsWith("\\", StringComparison.Ordinal) ? folder : folder + "\\";

                ICollection<FontFamily> families;
                try { families = Fonts.GetFontFamilies(location); }
                catch (Exception ex) { result.Error = ex.Message; return result; }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (FontFamily family in families)
                {
                    string name = FirstName(family);
                    if (name.Length == 0 || !seen.Add(name)) continue;

                    GlyphTypeface? glyphs = null;
                    string file = "";
                    try
                    {
                        foreach (Typeface face in family.GetTypefaces())
                        {
                            if (!face.TryGetGlyphTypeface(out GlyphTypeface gt)) continue;
                            glyphs = gt;
                            try
                            {
                                Uri uri = gt.FontUri;
                                file = string.IsNullOrEmpty(uri.Fragment) ? uri.LocalPath : uri.LocalPath + uri.Fragment;
                            }
                            catch (Exception) { }
                            break; // the regular face is enough to judge which glyphs exist
                        }
                    }
                    catch (Exception) { }

                    if (glyphs == null) continue;

                    bool symbol;
                    try { symbol = glyphs.Symbol; } catch (Exception) { symbol = false; }

                    GlyphTypeface g = glyphs;
                    GlyphCoverage coverage = GlyphVerdict.Evaluate(name, symbol, cp =>
                    {
                        try { return g.CharacterToGlyphMap.TryGetValue(cp, out ushort idx) && idx != 0; }
                        catch (Exception) { return false; }
                    });
                    coverage.FontFile = file;

                    result.Fonts.Add(new FolderFont
                    {
                        Family = name,
                        File = file,
                        Coverage = coverage,
                        AlsoInstalled = installed.IsInstalled(name),
                    });
                }

                result.Fonts.Sort((a, b) => string.Compare(a.Family, b.Family, StringComparison.OrdinalIgnoreCase));
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }

        private static string FirstName(FontFamily family)
        {
            try { return family.FamilyNames.Values.FirstOrDefault() ?? family.Source ?? ""; }
            catch (Exception) { return ""; }
        }
    }
}
