using System.Collections.Generic;

namespace Optimus.Core.Maintenance
{
    /// <summary>
    /// The cleanup rules that make this a maintenance tool for a DESIGN shop rather than one more
    /// generic cleaner.
    ///
    /// <para>
    /// The headline rule came from evidence sitting in the operator's own test folder:
    /// <c>Backup_of_arquivo.cdr</c> (4.5 MB) and <c>Backup_of_arquivo2.cdr</c> (7.9 MB), beside the
    /// artwork. CorelDRAW writes one of those <b>every time a file is saved</b>, so a shop machine with
    /// years of work carries GIGABYTES of them scattered through the art folders — and no generic
    /// cleaner knows they exist.
    /// </para>
    /// <para>
    /// Every rule here is deliberately conservative: a `.cdr` is the customer's product, so only files
    /// CorelDRAW itself created as disposable are eligible, and each one still needs the operator's
    /// tick (<see cref="RiskLevel.NeedsConfirmation"/>).
    /// </para>
    /// </summary>
    public static class CorelResidueRules
    {
        /// <summary>
        /// CorelDRAW's save-time backup copies. Reported with their full path and size so the operator
        /// sees exactly what would go — never deleted silently, because they sit next to real artwork.
        /// </summary>
        public static CleanupRule SaveBackups()
        {
            var rule = new CleanupRule
            {
                Id = "corel.savebackups",
                Label = "Cópias de segurança do CorelDRAW",
                Description = "Arquivos \"Backup_of_*.cdr\" que o CorelDRAW cria a cada salvamento, "
                            + "ao lado da sua arte. Costumam somar gigabytes numa máquina antiga. "
                            + "Você verá a lista com caminho e tamanho antes de apagar.",
                Risk = RiskLevel.NeedsConfirmation,
                MinAgeHours = 24 * 7,   // a week: a recent backup may still be someone's undo plan
                Recursive = true,
            };
            rule.Patterns.Add("Backup_of_*.cdr");
            return rule;
        }

        /// <summary>Auto-recovery files CorelDRAW leaves behind after a crash or a clean exit.</summary>
        public static CleanupRule AutoRecovery()
        {
            var rule = new CleanupRule
            {
                Id = "corel.autorecovery",
                Label = "Arquivos de recuperação do CorelDRAW",
                Description = "Arquivos de recuperação automática antigos. O CorelDRAW os usa para "
                            + "restaurar o trabalho depois de um travamento; os antigos não servem mais.",
                Risk = RiskLevel.NeedsConfirmation,
                MinAgeHours = 24 * 3,
                PerUserProfile = true,
                Path = @"AppData\Roaming\Corel",
                Recursive = true,
            };
            rule.Patterns.Add("*.cdr");     // only inside Corel's own AppData recovery tree
            rule.Patterns.Add("*.bak");
            return rule;
        }

        /// <summary>Scratch files design apps leave in the user's TEMP.</summary>
        public static CleanupRule DesignAppScratch()
        {
            var rule = new CleanupRule
            {
                Id = "design.scratch",
                Label = "Arquivos temporários de programas de design",
                Description = "Rascunhos que CorelDRAW, Illustrator e InDesign deixam na pasta de "
                            + "temporários. Só remove o que está sem uso há mais de 3 dias.",
                Risk = RiskLevel.Safe,
                MinAgeHours = 72,
                PerUserProfile = true,
                Path = @"AppData\Local\Temp",
                Recursive = true,
            };
            rule.Patterns.Add("*.tmp");
            rule.Patterns.Add("~*");
            rule.Patterns.Add("cdr*.tmp");
            return rule;
        }

        /// <summary>
        /// Stuck print jobs. A print shop prints all day, and one jammed spool file blocks the whole
        /// queue — a fix no generic cleaner offers.
        /// </summary>
        public static CleanupRule StuckPrintSpool()
        {
            var rule = new CleanupRule
            {
                Id = "windows.spool",
                Label = "Fila de impressão travada",
                Description = "Trabalhos de impressão presos que travam a fila inteira. "
                            + "Requer que o serviço de impressão seja parado por um instante.",
                Risk = RiskLevel.NeedsConfirmation,
                MinAgeHours = 24,
                Path = @"%WINDIR%\System32\spool\PRINTERS",
                Recursive = false,
            };
            rule.Patterns.Add("*.SPL");
            rule.Patterns.Add("*.SHD");
            return rule;
        }

        /// <summary>Thumbnail cache — designers browse folders of huge images and it corrupts.</summary>
        public static CleanupRule ThumbnailCache()
        {
            var rule = new CleanupRule
            {
                Id = "windows.thumbcache",
                Label = "Cache de miniaturas",
                Description = "Miniaturas do Explorer. Chega a vários GB em quem navega por pastas de "
                            + "imagens, e limpar resolve miniatura errada ou em branco. "
                            + "O Explorer reinicia por 1–2 segundos.",
                Risk = RiskLevel.NeedsConfirmation,
                MinAgeHours = 0,        // nothing unique; Explorer rebuilds on demand
                PerUserProfile = true,
                Path = @"AppData\Local\Microsoft\Windows\Explorer",
                Recursive = false,
            };
            rule.Patterns.Add("thumbcache_*.db");
            rule.Patterns.Add("iconcache_*.db");
            return rule;
        }

        /// <summary>
        /// Every Corel/design-shop rule, in the order the app should present them.
        ///
        /// <para>
        /// Crash dumps are NOT here: they are Windows-wide and live in
        /// <see cref="SystemResidueRules.CrashEvidence"/>, which also covers WER and LiveKernelReports and
        /// preserves the five newest as evidence.
        /// </para>
        /// </summary>
        public static List<CleanupRule> All() => new List<CleanupRule>
        {
            SaveBackups(),          // the differentiator: gigabytes no other cleaner finds
            AutoRecovery(),
            DesignAppScratch(),
            ThumbnailCache(),
            StuckPrintSpool(),
        };
    }
}
