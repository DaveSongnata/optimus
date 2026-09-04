using System;
using System.IO;
using System.Text;
using Optimus.Core.Palettes;

namespace Optimus.Windows
{
    /// <summary>
    /// Persists the shop's registered colour palettes — same file-under-%LOCALAPPDATA% shape as
    /// <see cref="LanguageStore"/> (decision O7: no SQLite, no settings service). One JSON file, one
    /// registry, shared by every CorelDRAW session on this machine.
    /// </summary>
    public static class PaletteStore
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Optimus");

        private static string FilePath => Path.Combine(Dir, "color-palettes.json");

        private static string? Read()
        {
            try { return File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8) : null; }
            catch (Exception) { return null; }
        }

        private static void Write(string json)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, json ?? "", Encoding.UTF8);
            }
            catch (Exception) { /* an unwritable profile costs persistence, not the running session */ }
        }

        public static PaletteRegistry Service() => PaletteRegistry.Load(Read, Write);
    }
}
