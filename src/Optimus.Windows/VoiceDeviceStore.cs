using System;
using System.IO;
using System.Text;

namespace Optimus.Windows
{
    /// <summary>
    /// Persists which recording device the operator picked for voice commands — same shape as
    /// <see cref="LanguageStore"/> (a plain text file under <c>%LOCALAPPDATA%\Optimus</c>, no registry,
    /// no settings service, per decision O7).
    ///
    /// <para>
    /// Stored by DEVICE NAME, never by index: <c>WaveInEvent</c>'s device numbers are assigned by
    /// Windows in enumeration order and shift whenever a USB headset is plugged, unplugged, or the
    /// machine reboots with a different device attached first. A saved index would silently start
    /// recording from the wrong microphone; a saved name is looked up again at record time and falls
    /// back to the system default when it is not found (unplugged, renamed) instead of failing.
    /// </para>
    /// </summary>
    public static class VoiceDeviceStore
    {
        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Optimus");

        private static string FilePath => Path.Combine(Dir, "voice-mic.txt");

        /// <summary>The saved device name, or "" when none was ever chosen (use the system default).</summary>
        public static string Read()
        {
            try { return File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8).Trim() : ""; }
            catch (Exception) { return ""; }
        }

        public static void Write(string deviceName)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, deviceName ?? "", Encoding.UTF8);
            }
            catch (Exception) { /* an unwritable profile costs the preference, not the app */ }
        }
    }
}
