using System;
using System.IO;
using System.Text;

namespace Optimus.Windows
{
    /// <summary>
    /// Remembers which folder of fonts the shop watches — the same idea as Corel Font Manager's
    /// watched folder, so the operator points at it once instead of re-picking it every session.
    /// One line of text under <c>%LOCALAPPDATA%\Optimus</c>, mirroring <see cref="LanguageStore"/>.
    /// </summary>
    public static class FontFolderStore
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Optimus");

        private static string FilePath => Path.Combine(Dir, "font-folder.txt");

        public static string Read()
        {
            try { return File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8).Trim() : ""; }
            catch (Exception) { return ""; }
        }

        public static void Write(string folder)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, folder ?? "", Encoding.UTF8);
            }
            catch (Exception) { /* an unwritable profile costs the preference, not the feature */ }
        }
    }
}
