using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Optimus.Windows
{
    /// <summary>The bytes of one font file, ready to be handed to a WebView2 page as a data URL.</summary>
    public sealed class FontFileBytes
    {
        public string Family { get; set; } = "";
        public string Path { get; set; } = "";

        /// <summary>MIME type matching the extension — the browser refuses a face whose format lies.</summary>
        public string Mime { get; set; } = "";

        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string Error { get; set; } = "";
        public bool Ok => Error.Length == 0 && Data.Length > 0;
    }

    /// <summary>
    /// Finds the FILE behind a font family name, so the docker can render a real specimen instead of
    /// only asserting that the accents are there.
    ///
    /// <para>
    /// Telling an operator "Acentos do português OK" asks them to trust us. Drawing "ção" in the
    /// actual typeface lets them see it — and when a glyph is missing they see the notdef box, which
    /// is the same thing CorelDRAW will print. That is evidence, not a claim.
    /// </para>
    /// <para>
    /// Three sources, in the order that matches how a print shop actually gets fonts:
    /// per-user installs (HKCU — where a font installed without admin rights lands, and the one most
    /// often missed), machine-wide installs (HKLM), and a plain folder the shop keeps its fonts in,
    /// the Corel Font Manager way, where nothing is installed in Windows at all.
    /// </para>
    /// </summary>
    public sealed class FontFileLocator
    {
        private const string FontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

        /// <summary>
        /// Above this the specimen is not worth its cost: the bytes travel to the page as base64,
        /// which inflates them by a third, and a CJK font can be 20 MB. Refused with a reason rather
        /// than freezing the docker mid-render.
        /// </summary>
        public const int MaxBytes = 6 * 1024 * 1024;

        private readonly string[] _extraFolders;

        public FontFileLocator(IEnumerable<string>? extraFolders = null)
        {
            _extraFolders = extraFolders == null ? Array.Empty<string>() : new List<string>(extraFolders).ToArray();
        }

        public FontFileBytes Load(string family)
        {
            var result = new FontFileBytes { Family = family ?? "" };
            if (string.IsNullOrWhiteSpace(family)) { result.Error = "sem nome"; return result; }

            string? path = Locate(family!.Trim());
            if (path == null) { result.Error = "arquivo da fonte não encontrado"; return result; }

            result.Path = path;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) { result.Error = "arquivo da fonte não encontrado"; return result; }
                if (info.Length > MaxBytes)
                {
                    result.Error = "fonte muito grande para pré-visualizar";
                    return result;
                }

                result.Data = File.ReadAllBytes(path);
                result.Mime = MimeFor(path);
            }
            catch (Exception ex) { result.Error = ex.Message; }

            return result;
        }

        private string? Locate(string family)
        {
            foreach (string folder in _extraFolders)
            {
                string? hit = InFolder(folder, family);
                if (hit != null) return hit;
            }

            // HKCU first: a font installed "for me only" shadows nothing, but it is the case a
            // machine-wide-only lookup silently misses, and it is common on a shop machine where the
            // operator is not an administrator.
            return FromRegistry(Registry.CurrentUser, family) ?? FromRegistry(Registry.LocalMachine, family);
        }

        private static string? FromRegistry(RegistryKey hive, string family)
        {
            try
            {
                using RegistryKey? key = hive.OpenSubKey(FontsKey);
                if (key == null) return null;

                foreach (string name in key.GetValueNames())
                {
                    // Values read "Arial (TrueType)" / "Bebas Neue Bold (OpenType)". The family is the
                    // part before the parenthesis; style words after it are why an exact match on the
                    // whole value never works.
                    string label = name;
                    int paren = label.LastIndexOf('(');
                    if (paren > 0) label = label.Substring(0, paren);
                    label = label.Trim();

                    if (!label.Equals(family, StringComparison.OrdinalIgnoreCase) &&
                        !label.StartsWith(family + " ", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (key.GetValue(name) is not string file || file.Length == 0) continue;

                    // Machine entries are bare file names relative to the Fonts folder; per-user
                    // entries are already absolute.
                    string full = Path.IsPathRooted(file)
                        ? file
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);

                    if (File.Exists(full)) return full;
                }
            }
            catch (Exception) { /* an unreadable hive costs the specimen, not the audit */ }

            return null;
        }

        private static string? InFolder(string folder, string family)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;

                foreach (string file in Directory.GetFiles(folder))
                {
                    string ext = Path.GetExtension(file);
                    if (!IsFontExtension(ext)) continue;

                    string stem = Path.GetFileNameWithoutExtension(file);
                    // File names rarely match a family exactly ("BebasNeue-Regular.ttf" vs "Bebas
                    // Neue"), so compare with spaces and hyphens removed.
                    if (Squash(stem).StartsWith(Squash(family), StringComparison.OrdinalIgnoreCase))
                        return file;
                }
            }
            catch (Exception) { }

            return null;
        }

        private static string Squash(string s) => s.Replace(" ", "").Replace("-", "").Replace("_", "");

        private static bool IsFontExtension(string ext) =>
            ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".ttc", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".otc", StringComparison.OrdinalIgnoreCase);

        private static string MimeFor(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".otf" => "font/otf",
                ".ttc" => "font/collection",
                ".otc" => "font/collection",
                _ => "font/ttf",
            };
        }
    }
}
