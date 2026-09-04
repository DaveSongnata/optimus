using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using Optimus.Core.Maintenance;

namespace Optimus.Windows.Maintenance
{
    /// <summary>Everything read before any operation is offered. Read-only by construction.</summary>
    public sealed class MaintenanceDiagnosis
    {
        public List<PhysicalDiskInfo> Disks { get; } = new List<PhysicalDiskInfo>();
        public List<VolumeInfo> Volumes { get; } = new List<VolumeInfo>();
        public List<UserProfile> Profiles { get; } = new List<UserProfile>();
        public MemoryReport Memory { get; set; } = new MemoryReport();
        public RecycleBinContents Bin { get; set; } = new RecycleBinContents();

        public bool IsElevated { get; set; }

        /// <summary>True when a disk reports Warning/Unhealthy and destructive work must not run.</summary>
        public bool DestructiveBlocked { get; set; }

        public string BlockReason { get; set; } = "";

        public string SystemDriveRoot { get; set; } = "C:\\";
    }

    /// <summary>What the operator approved, resolved to concrete files.</summary>
    public sealed class MaintenancePlan
    {
        public List<MaintenanceOperation> Operations { get; } = new List<MaintenanceOperation>();

        /// <summary>Scan results keyed by operation id.</summary>
        public Dictionary<string, CleanupScan> Scans { get; } =
            new Dictionary<string, CleanupScan>(StringComparer.OrdinalIgnoreCase);

        public bool EmptyRecycleBin { get; set; }
        public bool RunCleanMgr { get; set; }
        public bool IncludeUpdateCleanup { get; set; }
        public bool RunComponentStore { get; set; }
        public bool ComponentStoreResetBase { get; set; }
        public bool OptimizeVolume { get; set; }

        /// <summary>Extra folders to sweep for <c>Backup_of_*.cdr</c>, chosen by the operator.</summary>
        public List<string> ArtworkRoots { get; } = new List<string>();

        public long TotalCandidateBytes
        {
            get
            {
                long n = 0;
                foreach (KeyValuePair<string, CleanupScan> kv in Scans) n += kv.Value.TotalBytes;
                return n;
            }
        }

        public int TotalCandidateFiles
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<string, CleanupScan> kv in Scans) n += kv.Value.Candidates.Count;
                return n;
            }
        }

        /// <summary>True when anything in the plan cannot be undone — decides the restore-point prompt.</summary>
        public bool HasIrreversible
        {
            get
            {
                foreach (MaintenanceOperation op in Operations) if (op.Irreversible) return true;
                return EmptyRecycleBin || RunCleanMgr || RunComponentStore;
            }
        }
    }

    /// <summary>
    /// Orchestrates the maintenance run: <b>diagnose, then scan, then execute, then measure</b> — in that
    /// order, always.
    ///
    /// <para>
    /// The order is a safety property, not a style choice. Disk health is read <i>before</i> anything
    /// destructive is even offered (R6.1/R6.2): mass deletion on a drive that is already failing is how a
    /// shop loses artwork it can still recover today. And the headline number comes from re-reading the
    /// drive's free space after the work, never from adding up the files we deleted (R6.6).
    /// </para>
    /// </summary>
    public sealed class MaintenanceRunner
    {
        private readonly Action<string> _log;

        public MaintenanceRunner(Action<string>? log = null)
        {
            _log = log ?? (_ => { });
        }

        // ── phase 1: read-only diagnosis ────────────────────────────────────────────

        public MaintenanceDiagnosis Diagnose()
        {
            var d = new MaintenanceDiagnosis { IsElevated = IsElevated() };

            try
            {
                d.SystemDriveRoot = Path.GetPathRoot(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";
            }
            catch (Exception) { }

            d.Disks.AddRange(DiskHealth.PhysicalDisks());
            d.Volumes.AddRange(DiskHealth.Volumes());
            d.Profiles.AddRange(UserProfiles.All());
            d.Memory = MemoryDiagnostics.Read();
            d.Bin = RecycleBin.Query();

            foreach (PhysicalDiskInfo disk in d.Disks)
            {
                if (disk.Health == DriveHealth.Warning || disk.Health == DriveHealth.Unhealthy)
                {
                    d.DestructiveBlocked = true;
                    d.BlockReason =
                        "O disco \"" + disk.FriendlyName + "\" está reportando problema ("
                        + disk.HealthLabel() + "). Nenhuma limpeza será executada: primeiro copie a arte "
                        + "para outro disco ou para a nuvem. Apagar arquivos num disco que está falhando "
                        + "pode inviabilizar a recuperação do que ainda dá para salvar.";
                    break;
                }
            }

            _log("Diagnóstico: discos=" + d.Disks.Count + " volumes=" + d.Volumes.Count
               + " perfis=" + d.Profiles.Count + " elevado=" + d.IsElevated
               + " travas=" + d.DestructiveBlocked + " travamentos30d=" + d.Memory.CrashesLast30Days);

            if (!d.IsElevated)
                _log("AVISO: sem elevação, as pastas do Windows e de outros usuários ficam inacessíveis.");

            return d;
        }

        // ── phase 2: scan (still changes nothing) ───────────────────────────────────

        /// <summary>
        /// Resolves every selected file-based operation to a concrete list the operator can review.
        /// Nothing is deleted here — which is what makes the <c>Backup_of_*.cdr</c> sweep safe to offer at all.
        /// </summary>
        public MaintenancePlan Scan(IEnumerable<MaintenanceOperation> selected, MaintenanceDiagnosis diag,
                                    IEnumerable<string>? artworkRoots = null)
        {
            var plan = new MaintenancePlan();

            List<string> roots = artworkRoots != null
                ? new List<string>(artworkRoots)
                : CleanupExecutor.DefaultArtworkRoots(diag.Profiles);
            plan.ArtworkRoots.AddRange(roots);

            foreach (MaintenanceOperation op in selected)
            {
                plan.Operations.Add(op);

                switch (op.Kind)
                {
                    case OperationKind.RecycleBin: plan.EmptyRecycleBin = true; continue;
                    case OperationKind.CleanMgr: plan.RunCleanMgr = true; continue;
                    case OperationKind.ComponentStore: plan.RunComponentStore = true; continue;
                    case OperationKind.VolumeOptimize: plan.OptimizeVolume = true; continue;
                    case OperationKind.Diagnostic: continue;
                }

                if (op.Rule == null) continue;

                List<(string Root, string Profile)> resolved =
                    CleanupExecutor.ResolveRoots(op.Rule, diag.Profiles, roots);

                CleanupScan scan = CleanupExecutor.ScanAll(op.Rule, resolved);
                plan.Scans[op.Id] = scan;

                _log("Varredura " + op.Id + ": " + scan.Candidates.Count + " arquivo(s), "
                   + FreedSpaceReport.Format(scan.TotalBytes)
                   + (scan.SkippedTooNew > 0 ? ", " + scan.SkippedTooNew + " recentes preservados" : "")
                   + (scan.KeptAsEvidence > 0 ? ", " + scan.KeptAsEvidence + " mantidos como evidência" : "")
                   + (scan.UnreadableFolders > 0 ? ", " + scan.UnreadableFolders + " pasta(s) sem acesso" : ""));
            }

            return plan;
        }

        // ── phase 3: execute ───────────────────────────────────────────────────────

        /// <summary>
        /// Runs the approved plan and returns the honest report. Refuses outright when the diagnosis
        /// blocked destructive work.
        /// </summary>
        public FreedSpaceReport Execute(MaintenancePlan plan, MaintenanceDiagnosis diag,
                                        bool measureNoiseFloor = true)
        {
            var report = new FreedSpaceReport();

            if (diag.DestructiveBlocked)
            {
                _log("EXECUÇÃO BLOQUEADA: " + diag.BlockReason);
                return report;
            }

            string drive = diag.SystemDriveRoot;

            if (measureNoiseFloor)
            {
                report.NoiseFloorBytes = CleanupExecutor.MeasureNoiseFloor(drive);
                _log("Ruído medido do disco: " + FreedSpaceReport.Format(report.NoiseFloorBytes));
            }

            report.FreeBeforeBytes = DiskHealth.FreeBytesOn(drive);
            _log("Espaço livre antes: " + FreedSpaceReport.Format(report.FreeBeforeBytes));

            foreach (MaintenanceOperation op in plan.Operations)
            {
                try { RunOne(op, plan, diag, report); }
                catch (Exception ex) { _log("Operação " + op.Id + " falhou: " + ex.Message); }
            }

            report.FreeAfterBytes = DiskHealth.FreeBytesOn(drive);
            _log("Espaço livre depois: " + FreedSpaceReport.Format(report.FreeAfterBytes));
            _log(report.Headline());

            return report;
        }

        private void RunOne(MaintenanceOperation op, MaintenancePlan plan, MaintenanceDiagnosis diag,
                            FreedSpaceReport report)
        {
            switch (op.Kind)
            {
                case OperationKind.Diagnostic:
                    return;

                case OperationKind.RecycleBin:
                {
                    // Read the size BEFORE emptying — afterwards there is nothing left to measure. It is
                    // still an estimate like every other category; the headline comes from the drive.
                    long binBytes = RecycleBin.Query().TotalBytes;
                    bool ok = RecycleBin.Empty(out string binMessage);
                    _log(binMessage);
                    if (ok)
                        report.Add(new CategoryResult
                        {
                            Id = op.Id,
                            Label = op.Label,
                            BytesRemoved = binBytes,
                            FilesRemoved = (int)Math.Min(int.MaxValue, diag.Bin.ItemCount),
                        });
                    return;
                }

                case OperationKind.CleanMgr:
                {
                    CleanMgrResult r = CleanMgrRunner.Run(diag.SystemDriveRoot, plan.IncludeUpdateCleanup);
                    _log("cleanmgr: " + r.Message);
                    foreach (VolumeCacheHandler h in r.Handlers)
                        if (!h.Enabled && h.ExcludedBecause.Length > 0)
                            _log("  fora: " + h.Key + " — " + h.ExcludedBecause);
                    return;
                }

                case OperationKind.ComponentStore:
                {
                    ToolRunResult r = VolumeOptimizer.ComponentCleanup(plan.ComponentStoreResetBase);
                    _log("DISM: " + r.Message);
                    return;
                }

                case OperationKind.VolumeOptimize:
                {
                    ToolRunResult r = VolumeOptimizer.Optimize(diag.SystemDriveRoot);
                    _log("defrag /O: " + VolumeOptimizer.DescribeOptimization(
                        diag.Disks.Count > 0 ? diag.Disks[0].Media : MediaKind.Unknown)
                        + " " + r.Message);
                    return;
                }

                case OperationKind.FileRuleWithService:
                {
                    if (op.Rule == null) return;
                    using (var guard = new ServiceGuard(op.ServiceName, _log))
                    {
                        if (!guard.TryStop())
                        {
                            _log("Operação " + op.Id + " não executada: " + guard.Message);
                            return;
                        }
                        DeleteScanned(op, plan, report);
                    }
                    return;
                }

                case OperationKind.FileRuleWithExplorer:
                {
                    if (op.Rule == null) return;
                    ExplorerHost.WithExplorerStopped(() => DeleteScanned(op, plan, report), _log);
                    return;
                }

                default:
                    DeleteScanned(op, plan, report);
                    return;
            }
        }

        private void DeleteScanned(MaintenanceOperation op, MaintenancePlan plan, FreedSpaceReport report)
        {
            if (op.Rule == null) return;
            if (!plan.Scans.TryGetValue(op.Id, out CleanupScan? scan) || scan == null) return;
            if (scan.Candidates.Count == 0) return;

            CategoryResult result = CleanupExecutor.Delete(op.Rule, scan.Candidates, null, _log);
            report.Add(result);

            _log(op.Label + ": " + result.FilesRemoved + " arquivo(s) removido(s), "
               + FreedSpaceReport.Format(result.BytesRemoved) + " (estimativa)"
               + (result.FilesLocked > 0 ? ", " + result.FilesLocked + " em uso/preservado(s)" : ""));
        }

        /// <summary>
        /// Whether the process is elevated. Without elevation the Windows folders and other users'
        /// profiles are unreachable, and the app must SAY so instead of silently freeing nothing.
        /// </summary>
        public static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }
    }
}
