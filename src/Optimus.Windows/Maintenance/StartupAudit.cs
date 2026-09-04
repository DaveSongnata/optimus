using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Optimus.Windows.Maintenance
{
    /// <summary>Where a startup entry lives. Each surface is disabled by a different mechanism.</summary>
    public enum StartupSurface
    {
        /// <summary>HKCU ...\CurrentVersion\Run — this user only.</summary>
        UserRun,

        /// <summary>HKLM ...\CurrentVersion\Run — every user (64-bit view).</summary>
        MachineRun,

        /// <summary>HKLM ...\Wow6432Node\...\Run — 32-bit programs on a 64-bit Windows.</summary>
        MachineRun32,

        /// <summary>A shortcut in a Startup folder (per-user or All Users).</summary>
        StartupFolder,

        /// <summary>A scheduled task that fires at logon.</summary>
        ScheduledTask,
    }

    public sealed class StartupEntry
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public StartupSurface Surface { get; set; }

        /// <summary>Registry path or file path where it lives — shown so nothing is mysterious.</summary>
        public string Location { get; set; } = "";

        /// <summary>False when Windows already has it disabled (StartupApproved says so).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>True when Optimus refuses to offer it for disabling, with <see cref="ProtectedBecause"/>.</summary>
        public bool IsProtected { get; set; }

        public string ProtectedBecause { get; set; } = "";

        /// <summary>True when the target executable no longer exists — pure dead weight.</summary>
        public bool TargetMissing { get; set; }

        public override string ToString() => Name + " → " + Command;
    }

    /// <summary>
    /// Lists what starts with Windows, across the five surfaces that actually matter, and disables entries
    /// <b>reversibly</b>.
    ///
    /// <para>
    /// TWO rules, both learned from how these tools earn their bad reputation:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>Disable, never delete.</b> Optimus writes the same <c>StartupApproved</c> value Task Manager
    /// writes, so the operator (or the software's own installer) can turn it back on from Windows' own UI.
    /// Deleting a Run value means the entry can only come back by reinstalling the program.
    /// </description></item>
    /// <item><description>
    /// <b>Never offer to disable something the machine needs.</b> Security software, the audio stack, the
    /// touchpad/tablet driver and CorelDRAW's own licensing service are protected outright — a shop whose
    /// Wacom stopped working after "optimisation" will never trust the tool again.
    /// </description></item>
    /// </list>
    /// </summary>
    public static class StartupAudit
    {
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string RunKey32 = @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedRun =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ApprovedFolder =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        /// <summary>
        /// Substrings that mark an entry as untouchable. Matching on substrings is deliberate: vendors
        /// rename these constantly, and a false positive here only costs a missed optimisation, while a
        /// false negative can leave the machine without antivirus or without a working tablet.
        /// </summary>
        private static readonly (string Needle, string Why)[] ProtectedEntries =
        {
            ("defender",    "Antivírus do Windows."),
            ("securityhealth", "Central de Segurança do Windows."),
            ("avast",       "Antivírus."),
            ("avg",         "Antivírus."),
            ("kaspersky",   "Antivírus."),
            ("eset",        "Antivírus."),
            ("bitdefender", "Antivírus."),
            ("mcafee",      "Antivírus."),
            ("norton",      "Antivírus."),
            ("sophos",      "Antivírus."),
            ("realtek",     "Áudio — desativar deixa a máquina sem som."),
            // Broad on purpose: a driver's own task can be named anything ("iGoAudioTaskSession" was
            // found on a real machine), and the cost of over-protecting is a missed optimisation, while
            // the cost of under-protecting is a shop machine with no sound.
            ("audio",       "Áudio — desativar pode deixar a máquina sem som."),
            ("nahimic",     "Áudio."),
            ("wacom",       "Mesa digitalizadora — essencial no trabalho de design."),
            ("synaptics",   "Touchpad."),
            ("elan",        "Touchpad."),
            ("igfx",        "Vídeo Intel."),
            ("nvidia",      "Vídeo NVIDIA."),
            ("amd ",        "Vídeo AMD."),
            ("corel",       "Licenciamento e serviços do CorelDRAW."),
            ("protexis",    "Licenciamento do CorelDRAW (PSI Service)."),
            ("adobe genuine", "Licenciamento Adobe."),
            ("onedrive",    "Sincronização de arquivos — pode haver trabalho não sincronizado."),
            ("dropbox",     "Sincronização de arquivos."),
            ("google drive", "Sincronização de arquivos."),
        };

        public static List<StartupEntry> All()
        {
            var entries = new List<StartupEntry>();

            ReadRunKey(entries, RegistryHive.CurrentUser, RegistryView.Registry64, RunKey,
                       StartupSurface.UserRun, "HKCU\\" + RunKey);
            ReadRunKey(entries, RegistryHive.LocalMachine, RegistryView.Registry64, RunKey,
                       StartupSurface.MachineRun, "HKLM\\" + RunKey);
            ReadRunKey(entries, RegistryHive.LocalMachine, RegistryView.Registry32, RunKey,
                       StartupSurface.MachineRun32, "HKLM\\" + RunKey32);

            ReadStartupFolder(entries, Environment.SpecialFolder.Startup);
            ReadStartupFolder(entries, Environment.SpecialFolder.CommonStartup);

            // The fifth surface — and the one modern updaters actually use.
            entries.AddRange(ScheduledTaskAudit.All());

            ApplyApprovedFlags(entries);
            ApplyProtection(entries);

            return entries;
        }

        private static void ReadRunKey(List<StartupEntry> into, RegistryHive hive, RegistryView view,
                                       string subKey, StartupSurface surface, string label)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey? key = baseKey.OpenSubKey(subKey))
                {
                    if (key == null) return;
                    foreach (string name in key.GetValueNames())
                    {
                        string command = key.GetValue(name)?.ToString() ?? "";
                        into.Add(new StartupEntry
                        {
                            Name = name,
                            Command = command,
                            Surface = surface,
                            Location = label,
                            TargetMissing = !ExecutableExists(command),
                        });
                    }
                }
            }
            catch (Exception) { /* a locked hive costs one surface, not the whole audit */ }
        }

        private static void ReadStartupFolder(List<StartupEntry> into, Environment.SpecialFolder folder)
        {
            try
            {
                string dir = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                foreach (string file in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(file);
                    if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    into.Add(new StartupEntry
                    {
                        Name = name,
                        Command = file,
                        Surface = StartupSurface.StartupFolder,
                        Location = dir,
                    });
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Reads the <c>StartupApproved</c> keys so an entry Windows already disabled is not reported as
        /// a finding — otherwise the tool "optimises" the same three items forever.
        /// </summary>
        private static void ApplyApprovedFlags(List<StartupEntry> entries)
        {
            var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            void Collect(RegistryHive hive, string path)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                    using (RegistryKey? key = baseKey.OpenSubKey(path))
                    {
                        if (key == null) return;
                        foreach (string name in key.GetValueNames())
                            if (key.GetValue(name) is byte[] raw && raw.Length > 0)
                                flags[name] = IsEnabledFlag(raw);
                    }
                }
                catch (Exception) { }
            }

            Collect(RegistryHive.CurrentUser, ApprovedRun);
            Collect(RegistryHive.LocalMachine, ApprovedRun);
            Collect(RegistryHive.CurrentUser, ApprovedFolder);
            Collect(RegistryHive.LocalMachine, ApprovedFolder);

            foreach (StartupEntry e in entries)
            {
                // Scheduled tasks carry their own Enabled flag; a name collision with a Run value must
                // never overwrite it.
                if (e.Surface == StartupSurface.ScheduledTask) continue;
                if (flags.TryGetValue(e.Name, out bool enabled)) e.Enabled = enabled;
            }
        }

        /// <summary>
        /// First byte 0x02 = enabled; 0x03 (and 0x06 for folder items) = disabled. This is Task Manager's
        /// own encoding, which is exactly why using it keeps the change visible and reversible in Windows.
        /// </summary>
        internal static bool IsEnabledFlag(byte[] raw) => raw.Length > 0 && (raw[0] & 0x01) == 0;

        private static void ApplyProtection(List<StartupEntry> entries)
        {
            foreach (StartupEntry e in entries)
            {
                string haystack = (e.Name + " " + e.Command).ToLowerInvariant();
                foreach ((string needle, string why) in ProtectedEntries)
                {
                    if (haystack.IndexOf(needle, StringComparison.Ordinal) >= 0)
                    {
                        e.IsProtected = true;
                        e.ProtectedBecause = why;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Turns an entry off the way Windows does it — by writing <c>StartupApproved</c>, never by
        /// deleting the Run value. Returns false when the entry is protected or the write fails.
        /// </summary>
        public static bool Disable(StartupEntry entry, out string message)
        {
            if (entry == null) { message = "Entrada inválida."; return false; }

            if (entry.IsProtected)
            {
                message = "Não desativado: " + entry.ProtectedBecause;
                return false;
            }

            if (entry.Surface == StartupSurface.ScheduledTask)
            {
                bool done = ScheduledTaskAudit.SetEnabled(entry.Location, false, out message);
                if (done) entry.Enabled = false;
                return done;
            }

            RegistryHive hive = entry.Surface == StartupSurface.UserRun
                             || entry.Surface == StartupSurface.StartupFolder
                                 ? RegistryHive.CurrentUser
                                 : RegistryHive.LocalMachine;

            string path = entry.Surface == StartupSurface.StartupFolder ? ApprovedFolder : ApprovedRun;

            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey key = baseKey.CreateSubKey(path))
                {
                    if (key == null) { message = "Não foi possível abrir o registro."; return false; }
                    key.SetValue(entry.Name, DisabledBlob(), RegistryValueKind.Binary);
                }

                entry.Enabled = false;
                message = "Desativado (reversível no Gerenciador de Tarefas do Windows).";
                return true;
            }
            catch (Exception ex)
            {
                message = "Falha ao desativar: " + ex.Message;
                return false;
            }
        }

        /// <summary>Re-enables an entry, so the operation is genuinely two-way.</summary>
        public static bool Enable(StartupEntry entry, out string message)
        {
            if (entry == null) { message = "Entrada inválida."; return false; }

            if (entry.Surface == StartupSurface.ScheduledTask)
            {
                bool done = ScheduledTaskAudit.SetEnabled(entry.Location, true, out message);
                if (done) entry.Enabled = true;
                return done;
            }

            RegistryHive hive = entry.Surface == StartupSurface.UserRun
                             || entry.Surface == StartupSurface.StartupFolder
                                 ? RegistryHive.CurrentUser
                                 : RegistryHive.LocalMachine;
            string path = entry.Surface == StartupSurface.StartupFolder ? ApprovedFolder : ApprovedRun;

            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey key = baseKey.CreateSubKey(path))
                {
                    if (key == null) { message = "Não foi possível abrir o registro."; return false; }
                    key.SetValue(entry.Name, EnabledBlob(), RegistryValueKind.Binary);
                }
                entry.Enabled = true;
                message = "Reativado.";
                return true;
            }
            catch (Exception ex)
            {
                message = "Falha ao reativar: " + ex.Message;
                return false;
            }
        }

        private static byte[] DisabledBlob()
        {
            // 0x03 + 11 bytes: Task Manager stores the disable timestamp in bytes 4..11; zeros are valid
            // and Windows treats the entry as disabled regardless.
            var blob = new byte[12];
            blob[0] = 0x03;
            return blob;
        }

        private static byte[] EnabledBlob()
        {
            var blob = new byte[12];
            blob[0] = 0x02;
            return blob;
        }

        /// <summary>
        /// Extracts the executable from a Run command line and checks it exists. A dead entry is the one
        /// finding that is always safe to act on — nothing can break by not launching a missing file.
        /// </summary>
        internal static bool ExecutableExists(string command)
        {
            string? exe = ExtractExecutable(command);
            if (string.IsNullOrEmpty(exe)) return true;   // cannot tell → do not accuse

            try
            {
                exe = Environment.ExpandEnvironmentVariables(exe!);
                if (File.Exists(exe)) return true;
                // Bare name relying on PATH (e.g. "rundll32.exe"): not a missing target.
                return !Path.IsPathRooted(exe);
            }
            catch (Exception) { return true; }
        }

        internal static string? ExtractExecutable(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            command = command.Trim();

            if (command[0] == '"')
            {
                int close = command.IndexOf('"', 1);
                return close > 1 ? command.Substring(1, close - 1) : null;
            }

            // Unquoted: take up to the first space that is followed by a switch-looking token, otherwise
            // up to ".exe". Unquoted paths with spaces are ambiguous by construction — when in doubt we
            // return the .exe prefix and let File.Exists decide.
            int exeAt = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeAt > 0) return command.Substring(0, exeAt + 4);

            int space = command.IndexOf(' ');
            return space > 0 ? command.Substring(0, space) : command;
        }
    }
}
