using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;

namespace Optimus.Windows.Maintenance
{
    /// <summary>One process worth naming when the operator says the machine is out of memory.</summary>
    public sealed class MemoryHog
    {
        public string Name { get; set; } = "";
        public long WorkingSetBytes { get; set; }
        public int ProcessId { get; set; }
    }

    public sealed class MemoryReport
    {
        public long TotalBytes { get; set; }
        public long AvailableBytes { get; set; }

        /// <summary>Bytes Windows is using as file cache — memory that LOOKS used and is doing work.</summary>
        public long CacheBytes { get; set; }

        public long CommitLimitBytes { get; set; }
        public long CommitUsedBytes { get; set; }

        public List<MemoryHog> TopConsumers { get; } = new List<MemoryHog>();

        /// <summary>Crashes counted from dump files in the last 30 days — a fact, not a feeling.</summary>
        public int CrashesLast30Days { get; set; }

        public double UsedPercent => TotalBytes > 0
            ? 100.0 * (TotalBytes - AvailableBytes) / TotalBytes : 0;

        /// <summary>
        /// Commit pressure is the number that actually predicts "o Corel travou": when the commit charge
        /// approaches the limit, allocations start failing regardless of how much RAM looks free.
        /// </summary>
        public double CommitPercent => CommitLimitBytes > 0
            ? 100.0 * CommitUsedBytes / CommitLimitBytes : 0;

        public bool UnderRealPressure => CommitPercent > 90 || (TotalBytes > 0 && AvailableBytes < 1L * 1024 * 1024 * 1024);
    }

    /// <summary>
    /// The memory screen — which deliberately has <b>no "limpar RAM" button</b>.
    ///
    /// <para>
    /// This is decision O6, and it is a measurement, not an opinion. "Freeing" RAM means calling
    /// <c>EmptyWorkingSet</c> or purging the standby list, which evicts pages Windows had already cached;
    /// the very next time a program needs them they come back from disk. The graph looks better and the
    /// machine is <b>slower</b>. Unused RAM is not a resource being wasted — it is a resource being
    /// wasted only when it is empty.
    /// </para>
    /// <para>
    /// So instead of a placebo the app reports what genuinely explains a slow machine: how much is
    /// actually committed, which processes hold it, how many times this PC has crashed, and the startup
    /// audit — because the only permanent way to give RAM back is to stop programs from loading at boot.
    /// </para>
    /// </summary>
    public static class MemoryDiagnostics
    {
        public static MemoryReport Read()
        {
            var report = new MemoryReport();

            ReadOsCounters(report);
            ReadTopConsumers(report);
            report.CrashesLast30Days = CountRecentCrashes();

            return report;
        }

        private static void ReadOsCounters(MemoryReport report)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT TotalVisibleMemorySize, FreePhysicalMemory, TotalVirtualMemorySize, "
                  + "FreeVirtualMemory, SizeStoredInPagingFiles FROM Win32_OperatingSystem"))
                foreach (ManagementObject mo in searcher.Get())
                    using (mo)
                    {
                        // Win32_OperatingSystem reports KILOBYTES.
                        report.TotalBytes = Kb(mo, "TotalVisibleMemorySize");
                        report.AvailableBytes = Kb(mo, "FreePhysicalMemory");

                        long totalVirtual = Kb(mo, "TotalVirtualMemorySize");
                        long freeVirtual = Kb(mo, "FreeVirtualMemory");
                        report.CommitLimitBytes = totalVirtual;
                        report.CommitUsedBytes = Math.Max(0, totalVirtual - freeVirtual);
                    }
            }
            catch (Exception) { }

            try
            {
                using (var pc = new PerformanceCounter("Memory", "Cache Bytes"))
                    report.CacheBytes = (long)pc.NextValue();
            }
            catch (Exception)
            {
                // Performance counters are frequently broken on repaired machines; the cache figure is
                // explanatory, never load-bearing.
            }
        }

        private static void ReadTopConsumers(MemoryReport report)
        {
            var hogs = new List<MemoryHog>();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        hogs.Add(new MemoryHog
                        {
                            Name = p.ProcessName,
                            ProcessId = p.Id,
                            WorkingSetBytes = p.WorkingSet64,
                        });
                    }
                    catch (Exception) { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch (Exception) { }

            hogs.Sort((a, b) => b.WorkingSetBytes.CompareTo(a.WorkingSetBytes));
            for (int i = 0; i < hogs.Count && i < 8; i++) report.TopConsumers.Add(hogs[i]);
        }

        /// <summary>
        /// Counts crash dumps written in the last 30 days. Turning "essa máquina vive travando" into
        /// "travou 7 vezes nos últimos 30 dias" is the difference between a complaint and a diagnosis.
        /// </summary>
        public static int CountRecentCrashes()
        {
            int count = 0;
            DateTime cutoff = DateTime.UtcNow.AddDays(-30);

            foreach (string dir in CrashDumpFolders())
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (string file in Directory.GetFiles(dir, "*.dmp", SearchOption.TopDirectoryOnly))
                    {
                        try { if (new FileInfo(file).LastWriteTimeUtc >= cutoff) count++; }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }
            }

            return count;
        }

        internal static List<string> CrashDumpFolders()
        {
            var dirs = new List<string>();
            try
            {
                string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                dirs.Add(Path.Combine(windir, "Minidump"));
                dirs.Add(Path.Combine(windir, "LiveKernelReports"));
            }
            catch (Exception) { }
            return dirs;
        }

        /// <summary>
        /// The text the operator reads where a "limpar RAM" button would be. Written to answer the
        /// question honestly instead of pretending the button was forgotten.
        /// </summary>
        public static string WhyNoRamCleaning() =>
            "Não existe botão de \"limpar RAM\" aqui de propósito.\n\n"
          + "Memória livre não é memória economizada: o Windows usa a RAM sobrando para guardar o que "
          + "você acabou de abrir. \"Liberar\" essa memória obriga o próximo acesso a voltar ao disco — "
          + "o número na tela melhora e o computador fica MAIS LENTO.\n\n"
          + "O que realmente devolve memória é deixar de carregar programas junto com o Windows. "
          + "É isso que a auditoria de inicialização faz, e o efeito é permanente.";

        /// <summary>The same honesty applied to Prefetch, the other classic placebo.</summary>
        public static string WhyNoPrefetchCleaning() =>
            "Limpar a pasta Prefetch também não entra aqui. Ela é justamente o registro que faz os "
          + "programas abrirem rápido; apagá-la deixa tudo mais lento até o Windows reconstruí-la, "
          + "e ela ocupa poucos megabytes.";

        private static long Kb(ManagementBaseObject mo, string name)
        {
            try
            {
                object? v = mo[name];
                return v == null ? 0 : Convert.ToInt64(v) * 1024;
            }
            catch (Exception) { return 0; }
        }
    }
}
