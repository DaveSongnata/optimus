using System;
using System.IO;
using System.Text;
using Optimus.Core.Fonts;

namespace Optimus.Windows
{
    /// <summary>Persists the shop's registered font catalog — mirrors <see cref="PaletteStore"/>.</summary>
    public static class FontRegistryStore
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Optimus");

        private static string FilePath => Path.Combine(Dir, "font-registry.json");

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
            catch (Exception) { }
        }

        public static FontRegistry Service() => FontRegistry.Load(Read, Write);
    }
}
