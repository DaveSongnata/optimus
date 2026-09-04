using System.Collections.Generic;

namespace Optimus.Core.Maintenance
{
    /// <summary>
    /// The Windows-wide cleanup rules — operations 4–10 of the ranked list in <c>plans/Phase_6.md</c>.
    ///
    /// <para>
    /// Two things are deliberately absent and stay absent: <b>RAM cleaning</b> (<c>EmptyWorkingSet</c> and
    /// standby-list purging measurably make the machine SLOWER, because the next read has to come from
    /// disk again) and <b>Prefetch clearing</b> (it is the data that makes apps start fast; deleting it is
    /// negative value until Windows rebuilds it). Decision O6. The UI explains this instead of hiding it.
    /// </para>
    /// </summary>
    public static class SystemResidueRules
    {
        /// <summary>
        /// The broad per-profile temp sweep. Everything, but only if untouched for 72 h — CorelDRAW keeps
        /// live scratch files here while a document is open, and deleting one of those corrupts the job on
        /// screen.
        /// </summary>
        public static CleanupRule UserTemp()
        {
            var rule = new CleanupRule
            {
                Id = "windows.usertemp",
                Label = "Arquivos temporários (por usuário)",
                Description = "A pasta de temporários de cada usuário real da máquina. Só remove o que "
                            + "está sem uso há mais de 3 dias, para não tocar em nada que o CorelDRAW "
                            + "ainda esteja usando.",
                Risk = RiskLevel.Safe,
                MinAgeHours = 72,
                PerUserProfile = true,
                Path = @"AppData\Local\Temp",
                Recursive = true,
            };
            return rule;   // no patterns: everything in this folder is temporary by definition
        }

        /// <summary>The machine-wide temp folder, same age gate.</summary>
        public static CleanupRule MachineTemp()
        {
            return new CleanupRule
            {
                Id = "windows.machinetemp",
                Label = "Arquivos temporários do Windows",
                Description = "A pasta de temporários do próprio Windows (C:\\Windows\\Temp). "
                            + "Mesmo critério: nada modificado nos últimos 3 dias é tocado.",
                Risk = RiskLevel.Safe,
                MinAgeHours = 72,
                Path = @"%WINDIR%\Temp",
                Recursive = true,
            };
        }

        /// <summary>
        /// Windows Update's download cache. Safe because Windows re-downloads anything it still needs —
        /// but the service must be stopped first, which is the executor's job.
        /// </summary>
        public static CleanupRule WindowsUpdateCache()
        {
            return new CleanupRule
            {
                Id = "windows.updatecache",
                Label = "Cache do Windows Update",
                Description = "Instaladores de atualizações já aplicadas. O Windows baixa de novo o que "
                            + "precisar. Costuma ocupar de 300 MB a 8 GB numa máquina antiga.",
                Risk = RiskLevel.Safe,
                MinAgeHours = 24,
                Path = @"%WINDIR%\SoftwareDistribution\Download",
                Recursive = true,
            };
        }

        /// <summary>
        /// Crash dumps, WER reports and LiveKernelReports. A single kernel dump can be the size of
        /// installed RAM, and this is one of the biggest single wins on a machine that has been crashing.
        /// The five newest are preserved: they are the only evidence of WHY it crashes.
        /// </summary>
        public static CleanupRule CrashEvidence()
        {
            var rule = new CleanupRule
            {
                Id = "windows.crashevidence",
                Label = "Relatórios de travamento",
                Description = "Despejos de memória e relatórios de erro antigos — um único arquivo pode "
                            + "ter o tamanho da memória RAM. Os 5 mais recentes são mantidos, porque são "
                            + "a única pista se a máquina voltar a travar.",
                Risk = RiskLevel.Safe,
                MinAgeHours = 24 * 7,
                KeepNewest = 5,
                Path = @"%WINDIR%\Minidump",
                Recursive = true,
            };
            rule.ExtraPaths.Add(@"%WINDIR%\LiveKernelReports");
            rule.ExtraPaths.Add(@"%PROGRAMDATA%\Microsoft\Windows\WER\ReportArchive");
            rule.ExtraPaths.Add(@"%PROGRAMDATA%\Microsoft\Windows\WER\ReportQueue");
            rule.Patterns.Add("*.dmp");
            rule.Patterns.Add("*.hdmp");
            rule.Patterns.Add("*.mdmp");
            rule.Patterns.Add("*.wer");
            return rule;
        }

        /// <summary>
        /// Browser caches — the CACHE folders only.
        ///
        /// <para>
        /// The patterns and paths are scoped so that cookies, saved logins, history and bookmarks are
        /// never reachable. A designer who loses the login to the client's file-transfer site because a
        /// "cleaner" wiped cookies will blame the tool, correctly.
        /// </para>
        /// </summary>
        public static CleanupRule BrowserCaches()
        {
            var rule = new CleanupRule
            {
                Id = "browser.cache",
                Label = "Cache dos navegadores",
                Description = "Somente as pastas de cache de Chrome, Edge e Firefox. "
                            + "Senhas salvas, cookies, histórico e favoritos NÃO são tocados.",
                Risk = RiskLevel.NeedsConfirmation,
                MinAgeHours = 24,
                PerUserProfile = true,
                Path = @"AppData\Local\Google\Chrome\User Data\Default\Cache",
                Recursive = true,
            };
            rule.ExtraPaths.Add(@"AppData\Local\Google\Chrome\User Data\Default\Code Cache");
            rule.ExtraPaths.Add(@"AppData\Local\Microsoft\Edge\User Data\Default\Cache");
            rule.ExtraPaths.Add(@"AppData\Local\Microsoft\Edge\User Data\Default\Code Cache");
            rule.ExtraPaths.Add(@"AppData\Local\Mozilla\Firefox\Profiles\*\cache2");
            rule.ExtraPaths.Add(@"AppData\Local\BraveSoftware\Brave-Browser\User Data\Default\Cache");
            return rule;
        }

        /// <summary>Windows' own delivery-optimisation peer cache.</summary>
        public static CleanupRule DeliveryOptimization()
        {
            return new CleanupRule
            {
                Id = "windows.deliveryopt",
                Label = "Cache de distribuição de atualizações",
                Description = "Arquivos que o Windows guarda para compartilhar atualizações com outros "
                            + "PCs da rede. São recriados sob demanda.",
                Risk = RiskLevel.Safe,
                MinAgeHours = 24 * 3,
                Path = @"%WINDIR%\SoftwareDistribution\DeliveryOptimization",
                Recursive = true,
            };
        }

        /// <summary>
        /// Every system rule, in presentation order (safest and most valuable first). The Corel-specific
        /// rules are a separate list because they are the product's differentiator, not housekeeping.
        /// </summary>
        public static List<CleanupRule> All() => new List<CleanupRule>
        {
            UserTemp(),
            MachineTemp(),
            WindowsUpdateCache(),
            CrashEvidence(),
            DeliveryOptimization(),
            BrowserCaches(),
        };
    }
}
