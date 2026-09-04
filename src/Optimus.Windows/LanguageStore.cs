using System;
using System.IO;
using System.Text;
using Optimus.Core.I18n;

namespace Optimus.Windows
{
    /// <summary>
    /// Persists the operator's language choice, shared by the CorelDRAW docker and the maintenance app.
    ///
    /// <para>
    /// A one-line text file under <c>%LOCALAPPDATA%\Optimus</c> — deliberately not the registry and not a
    /// settings framework. It must survive a reinstall, be writable without elevation, and be readable by
    /// two different processes; and because the product has no SQLite and no settings service (decision
    /// O7), inventing one for a single string would be the wrong trade.
    /// </para>
    /// <para>
    /// Both operations are non-throwing: a machine where <c>%LOCALAPPDATA%</c> is locked down falls back to
    /// pt-BR rather than refusing to open the window.
    /// </para>
    /// </summary>
    public static class LanguageStore
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Optimus");

        private static string FilePath => Path.Combine(Dir, "ui-language.txt");

        /// <summary>Reads the persisted value for a preference key, or null.</summary>
        public static string? Read(string key)
        {
            if (key != LocalizationService.LanguagePrefKey) return null;

            try
            {
                return File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8).Trim() : null;
            }
            catch (Exception) { return null; }
        }

        public static void Write(string key, string value)
        {
            if (key != LocalizationService.LanguagePrefKey) return;

            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, value ?? "", Encoding.UTF8);
            }
            catch (Exception) { /* an unwritable profile costs the preference, not the app */ }
        }

        /// <summary>A service already wired to this store — what both UI hosts actually use.</summary>
        public static LocalizationService Service() => LocalizationService.Load(Read, Write);
    }
}
