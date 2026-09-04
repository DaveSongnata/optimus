using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Optimus.Windows.Maintenance
{
    /// <summary>One Disk Cleanup handler as Windows registers it.</summary>
    public sealed class VolumeCacheHandler
    {
        public string Key { get; set; } = "";
        public string Display { get; set; } = "";

        /// <summary>True when Optimus asked for it in this run.</summary>
        public bool Enabled { get; set; }

        /// <summary>Why it is off, when it is deliberately off.</summary>
        public string ExcludedBecause { get; set; } = "";
    }

    public sealed class CleanMgrResult
    {
        public bool Available { get; set; }
        public bool Ran { get; set; }
        public bool TimedOut { get; set; }
        public int ExitCode { get; set; }
        public string Message { get; set; } = "";
        public List<VolumeCacheHandler> Handlers { get; } = new List<VolumeCacheHandler>();
    }

    /// <summary>
    /// Drives Windows' own Disk Cleanup (<c>cleanmgr.exe</c>) instead of reimplementing it.
    ///
    /// <para>
    /// Reusing the in-box handlers is the whole point: Microsoft's own code knows which
    /// <c>WinSxS</c>/servicing files are safe to drop this month, and no third-party rule file can keep up
    /// with that. What Optimus adds is a <b>safe configuration</b>, because the API's defaults are a trap.
    /// </para>
    /// <para>
    /// THE trap: <c>cleanmgr /sagerun:N</c> runs every handler whose <c>StateFlagsNNNN</c> is non-zero —
    /// <b>including values a previous run (or another tool, or Microsoft's own default) left behind</b>. And
    /// one of the registered handlers is <c>DownloadsFolder</c>, which <b>deletes the user's entire
    /// Downloads folder</b>. "Not enabling it" is not enough: it must be written as an explicit
    /// <c>StateFlags = 0</c> on every run. A print shop keeps client files in Downloads; getting this wrong
    /// destroys paid work.
    /// </para>
    /// </summary>
    public static class CleanMgrRunner
    {
        private const string VolumeCachesKey =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches";

        /// <summary>Profile slot 100 — ours, so we never disturb whatever the customer had on 0–99.</summary>
        private const int ProfileNumber = 100;

        private static string StateFlagsValueName => "StateFlags" + ProfileNumber.ToString("0000");

        /// <summary>
        /// Handlers we ask for. Deliberately conservative and each one justified:
        /// caches and error reports that Windows rebuilds, plus the servicing caches that are the real
        /// multi-gigabyte wins on an old machine.
        /// </summary>
        private static readonly string[] Wanted =
        {
            "Temporary Files",
            "Temporary Setup Files",
            "Downloaded Program Files",
            "Internet Cache Files",
            "Thumbnail Cache",
            "Old ChkDsk Files",
            "Setup Log Files",
            "Update Cleanup",                       // servicing superseded components: often 2–6 GB
            "Windows Error Reporting Files",
            "System error memory dump files",
            "System error minidump files",
            "Delivery Optimization Files",
            "D3D Shader Cache",
            "Device Driver Packages",
            "Windows Upgrade Log Files",
        };

        /// <summary>
        /// Handlers that must be forced to zero, with the reason. These are not "unchecked defaults" —
        /// they are actively dangerous for this customer.
        /// </summary>
        private static readonly Dictionary<string, string> Forbidden =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "DownloadsFolder",
                  "APAGA a pasta Downloads inteira — onde a gráfica guarda arquivos de cliente." },
                { "Recycle Bin",
                  "A Lixeira é a última chance de recuperar um arquivo apagado por engano." },
                { "Previous Installations",
                  "Remove o Windows.old e com ele a possibilidade de voltar à versão anterior." },
                { "Windows ESD installation files",
                  "São os arquivos que permitem 'Restaurar este PC' sem mídia externa." },
            };

        public static bool IsAvailable() => File.Exists(CleanMgrPath());

        private static string CleanMgrPath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cleanmgr.exe");

        /// <summary>
        /// Writes our profile then runs it. <paramref name="includeUpdateCleanup"/> is separate because
        /// "Update Cleanup" is the biggest win AND the one that makes recent updates non-uninstallable —
        /// exactly the kind of trade-off that belongs to the operator, not to us.
        /// </summary>
        public static CleanMgrResult Run(string driveRoot, bool includeUpdateCleanup, int timeoutMinutes = 30)
        {
            var result = new CleanMgrResult();

            if (!IsAvailable())
            {
                result.Message = "A Limpeza de Disco do Windows não está instalada nesta edição.";
                return result;
            }
            result.Available = true;

            try
            {
                ConfigureProfile(result, includeUpdateCleanup);
            }
            catch (Exception ex)
            {
                result.Message = "Não foi possível configurar a Limpeza de Disco: " + ex.Message
                               + " (é necessário executar como administrador).";
                return result;
            }

            string drive = (driveRoot ?? "C:\\").Substring(0, 1);
            var psi = new ProcessStartInfo
            {
                FileName = CleanMgrPath(),
                Arguments = "/sagerun:" + ProfileNumber + " /d " + drive + ":",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            try
            {
                using (Process? p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        result.Message = "A Limpeza de Disco não pôde ser iniciada.";
                        return result;
                    }

                    // cleanmgr spawns and detaches; the parent can exit long before the work is done,
                    // so the caller still measures free space AFTER this returns, never trusting a total
                    // reported by cleanmgr (it reports none).
                    if (!p.WaitForExit(Math.Max(1, timeoutMinutes) * 60_000))
                    {
                        result.TimedOut = true;
                        result.Message = "A Limpeza de Disco passou de " + timeoutMinutes
                                       + " minutos e continua em segundo plano.";
                        return result;
                    }

                    result.Ran = true;
                    result.ExitCode = p.ExitCode;
                    result.Message = "Limpeza de Disco do Windows concluída.";
                }
            }
            catch (Exception ex)
            {
                result.Message = "Falha ao executar a Limpeza de Disco: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Writes <c>StateFlags0100</c> for EVERY registered handler — 1 for the ones we want, 0 for
        /// everything else. Writing the zeros is the safety mechanism, not housekeeping.
        /// </summary>
        internal static void ConfigureProfile(CleanMgrResult result, bool includeUpdateCleanup)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(
                       RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey? caches = baseKey.OpenSubKey(VolumeCachesKey, writable: true))
            {
                if (caches == null)
                    throw new InvalidOperationException("VolumeCaches não encontrado no registro.");

                foreach (string name in caches.GetSubKeyNames())
                {
                    using (RegistryKey? handler = caches.OpenSubKey(name, writable: true))
                    {
                        if (handler == null) continue;

                        var info = new VolumeCacheHandler
                        {
                            Key = name,
                            Display = handler.GetValue("")?.ToString() ?? name,
                        };

                        bool wanted = Array.IndexOf(Wanted, name) >= 0;

                        if (Forbidden.TryGetValue(name, out string? why))
                        {
                            wanted = false;
                            info.ExcludedBecause = why;
                        }
                        else if (name.Equals("Update Cleanup", StringComparison.OrdinalIgnoreCase)
                                 && !includeUpdateCleanup)
                        {
                            wanted = false;
                            info.ExcludedBecause =
                                "Não marcado: libera muito espaço, mas impede desinstalar atualizações antigas.";
                        }
                        else if (!wanted)
                        {
                            info.ExcludedBecause = "Fora da lista conservadora do Optimus.";
                        }

                        info.Enabled = wanted;
                        result.Handlers.Add(info);

                        try
                        {
                            handler.SetValue(StateFlagsValueName, wanted ? 2 : 0, RegistryValueKind.DWord);
                        }
                        catch (Exception)
                        {
                            // A handler whose key is ACL-locked stays untouched; because unknown handlers
                            // are only ever written as 0, failing to write cannot enable anything.
                            info.Enabled = false;
                            info.ExcludedBecause = "Não foi possível configurar (chave protegida).";
                        }
                    }
                }
            }
        }

        /// <summary>The handler list with the reasons, for the report the operator can read.</summary>
        public static List<VolumeCacheHandler> Preview(bool includeUpdateCleanup)
        {
            var result = new CleanMgrResult();
            try { ConfigureProfileReadOnly(result, includeUpdateCleanup); } catch (Exception) { }
            return result.Handlers;
        }

        private static void ConfigureProfileReadOnly(CleanMgrResult result, bool includeUpdateCleanup)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(
                       RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey? caches = baseKey.OpenSubKey(VolumeCachesKey))
            {
                if (caches == null) return;
                foreach (string name in caches.GetSubKeyNames())
                {
                    using (RegistryKey? handler = caches.OpenSubKey(name))
                    {
                        bool wanted = Array.IndexOf(Wanted, name) >= 0;
                        string why = "";
                        if (Forbidden.TryGetValue(name, out string? forbidden)) { wanted = false; why = forbidden; }
                        else if (name.Equals("Update Cleanup", StringComparison.OrdinalIgnoreCase)
                                 && !includeUpdateCleanup) { wanted = false; why = "Opcional, não marcado."; }

                        result.Handlers.Add(new VolumeCacheHandler
                        {
                            Key = name,
                            Display = handler?.GetValue("")?.ToString() ?? name,
                            Enabled = wanted,
                            ExcludedBecause = why,
                        });
                    }
                }
            }
        }
    }
}
