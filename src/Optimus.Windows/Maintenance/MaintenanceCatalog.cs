using System.Collections.Generic;
using Optimus.Core.Maintenance;

namespace Optimus.Windows.Maintenance
{
    /// <summary>How an operation is carried out, which decides who executes it.</summary>
    public enum OperationKind
    {
        /// <summary>File deletion driven by a <see cref="CleanupRule"/>.</summary>
        FileRule,

        /// <summary>Needs a Windows service stopped first (Windows Update cache, print spool).</summary>
        FileRuleWithService,

        /// <summary>Needs Explorer stopped first (thumbnail cache).</summary>
        FileRuleWithExplorer,

        /// <summary>Delegated to Windows' own Disk Cleanup.</summary>
        CleanMgr,

        /// <summary>Recycle Bin, via the shell.</summary>
        RecycleBin,

        /// <summary>DISM component-store cleanup.</summary>
        ComponentStore,

        /// <summary>Media-aware volume optimisation.</summary>
        VolumeOptimize,

        /// <summary>Read-only: reports something, changes nothing.</summary>
        Diagnostic,
    }

    /// <summary>One thing the operator can tick.</summary>
    public sealed class MaintenanceOperation
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public OperationKind Kind { get; set; }
        public RiskLevel Risk { get; set; } = RiskLevel.Safe;

        /// <summary>The rule, for the file-deletion kinds.</summary>
        public CleanupRule? Rule { get; set; }

        /// <summary>Service to stop for <see cref="OperationKind.FileRuleWithService"/>.</summary>
        public string ServiceName { get; set; } = "";

        /// <summary>Ticked by default. Only the safe, high-value ones are.</summary>
        public bool DefaultOn { get; set; }

        /// <summary>
        /// True when the operation cannot be undone even with a restore point — deleted files and
        /// <c>/ResetBase</c>. The UI must say so in those words.
        /// </summary>
        public bool Irreversible { get; set; }

        /// <summary>Rough expected win, stated as a RANGE because a real figure is unknowable up front.</summary>
        public string ExpectedGain { get; set; } = "";
    }

    /// <summary>
    /// The ranked operation list — ordered by value for a CorelDRAW machine, not by what is easy to
    /// implement. The Corel residue sweep sits at the top because it is the gigabyte win no generic
    /// cleaner knows about; the two placebos (RAM, Prefetch) are absent by decision O6 and the app
    /// explains their absence rather than hiding it.
    /// </summary>
    public static class MaintenanceCatalog
    {
        public static List<MaintenanceOperation> All()
        {
            var ops = new List<MaintenanceOperation>();

            ops.Add(new MaintenanceOperation
            {
                Id = "diag.disk",
                Label = "Espaço livre e saúde dos discos",
                Description = "Lê o estado de saúde (SMART) e o espaço livre de cada disco. "
                            + "Roda primeiro e não altera nada.",
                Kind = OperationKind.Diagnostic,
                Risk = RiskLevel.Safe,
                DefaultOn = true,
                ExpectedGain = "previne perda de arte",
            });

            ops.Add(FromRule(CorelResidueRules.SaveBackups(), OperationKind.FileRule,
                             "gigabytes — o diferencial deste programa", irreversible: true));

            ops.Add(FromRule(CorelResidueRules.AutoRecovery(), OperationKind.FileRule,
                             "100 MB – 2 GB", irreversible: true));

            ops.Add(new MaintenanceOperation
            {
                Id = "diag.startup",
                Label = "Auditoria de inicialização",
                Description = "Lista tudo que abre junto com o Windows, nas 5 origens possíveis. "
                            + "Você decide o que desativar — nada é desativado sozinho, e é reversível.",
                Kind = OperationKind.Diagnostic,
                Risk = RiskLevel.Safe,
                DefaultOn = true,
                ExpectedGain = "800 MB – 1,5 GB de RAM, permanente",
            });

            ops.Add(FromRule(SystemResidueRules.UserTemp(), OperationKind.FileRule,
                             "500 MB – 15 GB", irreversible: true, defaultOn: true));

            ops.Add(FromRule(SystemResidueRules.MachineTemp(), OperationKind.FileRule,
                             "100 MB – 5 GB", irreversible: true, defaultOn: true));

            MaintenanceOperation wu = FromRule(SystemResidueRules.WindowsUpdateCache(),
                                               OperationKind.FileRuleWithService,
                                               "300 MB – 8 GB", irreversible: true, defaultOn: true);
            wu.ServiceName = "wuauserv";
            ops.Add(wu);

            ops.Add(FromRule(SystemResidueRules.CrashEvidence(), OperationKind.FileRule,
                             "100 MB – 64 GB", irreversible: true, defaultOn: true));

            ops.Add(FromRule(SystemResidueRules.DeliveryOptimization(), OperationKind.FileRule,
                             "100 MB – 4 GB", irreversible: true, defaultOn: true));

            ops.Add(new MaintenanceOperation
            {
                Id = "windows.recyclebin",
                Label = "Lixeira",
                Description = "Mostra o tamanho e os 5 maiores itens ANTES de esvaziar. "
                            + "Depois de esvaziar não há como recuperar.",
                Kind = OperationKind.RecycleBin,
                Risk = RiskLevel.NeedsConfirmation,
                DefaultOn = false,
                Irreversible = true,
                ExpectedGain = "0 – 50 GB",
            });

            MaintenanceOperation spool = FromRule(CorelResidueRules.StuckPrintSpool(),
                                                 OperationKind.FileRuleWithService,
                                                 "destrava a fila de impressão", irreversible: true);
            spool.ServiceName = "Spooler";
            ops.Add(spool);

            ops.Add(FromRule(CorelResidueRules.ThumbnailCache(), OperationKind.FileRuleWithExplorer,
                             "100 MB – 5 GB; corrige miniatura errada", irreversible: false));

            ops.Add(FromRule(SystemResidueRules.BrowserCaches(), OperationKind.FileRule,
                             "300 MB – 3 GB", irreversible: true));

            ops.Add(FromRule(CorelResidueRules.DesignAppScratch(), OperationKind.FileRule,
                             "50 MB – 2 GB", irreversible: true, defaultOn: true));

            ops.Add(new MaintenanceOperation
            {
                Id = "windows.cleanmgr",
                Label = "Limpeza de Disco do Windows",
                Description = "Usa a própria ferramenta da Microsoft, com uma configuração segura: "
                            + "a pasta Downloads e a Lixeira ficam EXPLICITAMENTE de fora.",
                Kind = OperationKind.CleanMgr,
                Risk = RiskLevel.NeedsConfirmation,
                DefaultOn = false,
                Irreversible = true,
                ExpectedGain = "500 MB – 10 GB",
            });

            ops.Add(new MaintenanceOperation
            {
                Id = "windows.componentstore",
                Label = "Limpeza do repositório de componentes (DISM)",
                Description = "Remove versões antigas de componentes do Windows. Demora, e depois "
                            + "atualizações antigas não podem mais ser desinstaladas.",
                Kind = OperationKind.ComponentStore,
                Risk = RiskLevel.NeedsConfirmation,
                DefaultOn = false,
                Irreversible = true,
                ExpectedGain = "1 – 8 GB",
            });

            ops.Add(new MaintenanceOperation
            {
                Id = "windows.optimize",
                Label = "Otimizar o disco (ciente da mídia)",
                Description = "Em HD mecânico desfragmenta; em SSD executa o TRIM. "
                            + "Nunca desfragmenta um SSD.",
                Kind = OperationKind.VolumeOptimize,
                Risk = RiskLevel.Safe,
                DefaultOn = false,
                ExpectedGain = "desempenho (só em HD)",
            });

            ops.Add(new MaintenanceOperation
            {
                Id = "diag.memory",
                Label = "Diagnóstico de memória e travamentos",
                Description = "Quanta memória está realmente comprometida, quais programas a consomem "
                            + "e quantas vezes esta máquina travou nos últimos 30 dias.",
                Kind = OperationKind.Diagnostic,
                Risk = RiskLevel.Safe,
                DefaultOn = true,
                ExpectedGain = "informação real",
            });

            return ops;
        }

        private static MaintenanceOperation FromRule(CleanupRule rule, OperationKind kind,
                                                    string expectedGain, bool irreversible,
                                                    bool defaultOn = false) =>
            new MaintenanceOperation
            {
                Id = rule.Id,
                Label = rule.Label,
                Description = rule.Description,
                Kind = kind,
                Risk = rule.Risk,
                Rule = rule,
                DefaultOn = defaultOn && rule.Risk == RiskLevel.Safe,
                Irreversible = irreversible,
                ExpectedGain = expectedGain,
            };
    }
}
