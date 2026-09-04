using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Web.WebView2.Core;
using Optimus.Core.I18n;
using Optimus.Core.Maintenance;
using Optimus.Windows.Maintenance;

namespace Optimus.Maintenance
{
    /// <summary>
    /// JS ↔ C# dispatch for the maintenance app.
    ///
    /// <para>
    /// Same two hard-won conventions as the add-in docker: incoming messages are read with
    /// <c>WebMessageAsJson</c> (never <c>TryGetWebMessageAsString</c>, which throws for object messages),
    /// and outgoing messages go through <c>ExecuteScriptAsync("window.optimusReceive(...)")</c> rather than
    /// the message channel.
    /// </para>
    /// <para>
    /// All the real work runs on a background thread — a scan of an artwork drive takes tens of seconds and
    /// a frozen window is indistinguishable from a crashed one.
    /// </para>
    /// </summary>
    internal sealed class MaintenanceBridge
    {
        private readonly CoreWebView2 _core;
        private readonly Action<Action> _runOnUi;
        private readonly LocalizationService _i18n;
        private readonly Action _reloadForLanguage;

        private MaintenanceDiagnosis? _diagnosis;
        private MaintenancePlan? _plan;
        private List<StartupEntry> _startup = new List<StartupEntry>();
        private List<PerformanceTweak> _tweaks = new List<PerformanceTweak>();
        private readonly List<string> _artworkRoots = new List<string>();
        private readonly List<MaintenanceOperation> _catalog = MaintenanceCatalog.All();

        private readonly object _queueLock = new object();
        private readonly Queue<KeyValuePair<string, Action>> _queue =
            new Queue<KeyValuePair<string, Action>>();
        private bool _busy;

        public MaintenanceBridge(CoreWebView2 core, Action<Action> runOnUi,
                                 LocalizationService i18n, Action reloadForLanguage)
        {
            _core = core;
            _runOnUi = runOnUi;
            _i18n = i18n;
            _reloadForLanguage = reloadForLanguage;
            _core.WebMessageReceived += OnWebMessage;
        }

        /// <summary>Shorthand for a localized string in the operator's current language.</summary>
        private string L(string key) => _i18n[key];

        /// <summary>Localized string with positional substitution, mirroring the page's <c>T()</c>.</summary>
        private string L(string key, params object[] args)
        {
            string s = _i18n[key];
            for (int i = 0; i < args.Length; i++)
                s = s.Replace("{" + i + "}", args[i]?.ToString() ?? "");
            return s;
        }

        public void Detach()
        {
            try { _core.WebMessageReceived -= OnWebMessage; } catch (Exception) { }
        }

        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json;
                try { json = e.WebMessageAsJson; }
                catch (Exception ex) { MaintenanceLog.Write("leitura da mensagem falhou: " + ex.Message); return; }

                string cmd;
                using (JsonDocument doc = JsonDocument.Parse(json))
                    cmd = Str(doc.RootElement, "cmd");

                if (cmd != "log") MaintenanceLog.Write("comando: " + json);

                switch (cmd)
                {
                    case "diagnosticar": StartDiagnosis(); break;
                    case "varrer": Background("varrer", () => DoScan(json)); break;
                    case "executar": Background("executar", () => DoExecute(json)); break;
                    case "inicializacao": Background("inicializacao", DoStartupAudit); break;
                    case "inicializacao-alternar": Background("alternar:" + json, () => DoStartupToggle(json)); break;
                    case "memoria": Background("memoria", DoMemory); break;
                    case "desempenho": Background("desempenho", DoPerformance); break;
                    case "desempenho-aplicar": Background("perf:" + json, () => DoTweak(json, true)); break;
                    case "desempenho-reverter": Background("perf:" + json, () => DoTweak(json, false)); break;
                    case "escolher-pasta": PickFolder(); break;
                    case "abrir-log": OpenLog(); break;
                    case "idioma": SetLanguage(json); break;
                }
            }
            catch (Exception ex) { MaintenanceLog.Write("OnWebMessage: " + ex); }
        }

        // ── phase 1: diagnosis ──────────────────────────────────────────────────────

        public void StartDiagnosis() => Background("diagnosticar", DoDiagnosis);

        private void DoDiagnosis()
        {
            Busy(true, L("mnt.busy.diagnose"));
            try
            {
                var runner = new MaintenanceRunner(Log);
                _diagnosis = runner.Diagnose();
                PostDiagnosis(_diagnosis);
            }
            finally { BusyDone(); }
        }

        private void PostDiagnosis(MaintenanceDiagnosis d)
        {
            var sb = new StringBuilder();
            sb.Append("{\"tipo\":\"diagnostico\"");
            sb.Append(",\"versao\":").Append(Json(Program.VersionTag));
            sb.Append(",\"elevado\":").Append(d.IsElevated ? "true" : "false");
            sb.Append(",\"bloqueado\":").Append(d.DestructiveBlocked ? "true" : "false");
            sb.Append(",\"motivoBloqueio\":").Append(Json(d.BlockReason));
            sb.Append(",\"perfis\":[");
            for (int i = 0; i < d.Profiles.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"nome\":").Append(Json(d.Profiles[i].DisplayName))
                  .Append(",\"caminho\":").Append(Json(d.Profiles[i].ProfilePath)).Append('}');
            }
            sb.Append("],\"discos\":[");
            for (int i = 0; i < d.Disks.Count; i++)
            {
                PhysicalDiskInfo disk = d.Disks[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"nome\":").Append(Json(disk.FriendlyName))
                  .Append(",\"midia\":").Append(Json(MediaLabel(disk.Media)))
                  .Append(",\"saude\":").Append(Json(HealthLabel(disk.Health)))
                  .Append(",\"saudavel\":").Append(disk.Health == DriveHealth.Healthy ? "true" : "false")
                  .Append(",\"desconhecido\":").Append(disk.Health == DriveHealth.Unknown ? "true" : "false")
                  .Append(",\"otimizacao\":").Append(Json(OptimizeLabel(disk.Media)))
                  .Append(",\"horas\":").Append(disk.PowerOnHours?.ToString(CultureInfo.InvariantCulture) ?? "null")
                  .Append(",\"vida\":").Append(disk.WearRemainingPercent?.ToString(CultureInfo.InvariantCulture) ?? "null")
                  .Append('}');
            }
            sb.Append("],\"volumes\":[");
            for (int i = 0; i < d.Volumes.Count; i++)
            {
                VolumeInfo v = d.Volumes[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"raiz\":").Append(Json(v.Root))
                  .Append(",\"rotulo\":").Append(Json(v.Label))
                  .Append(",\"total\":").Append(Json(FreedSpaceReport.Format(v.TotalBytes)))
                  .Append(",\"livre\":").Append(Json(FreedSpaceReport.Format(v.FreeBytes)))
                  .Append(",\"livrePct\":").Append(v.FreePercent.ToString("0.#", CultureInfo.InvariantCulture))
                  .Append(",\"critico\":").Append(v.IsCritical ? "true" : "false")
                  .Append('}');
            }
            sb.Append("],\"lixeira\":{\"tamanho\":").Append(Json(FreedSpaceReport.Format(d.Bin.TotalBytes)))
              .Append(",\"itens\":").Append(d.Bin.ItemCount.ToString(CultureInfo.InvariantCulture))
              .Append(",\"maiores\":[");
            for (int i = 0; i < d.Bin.Largest.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"tamanho\":").Append(Json(FreedSpaceReport.Format(d.Bin.Largest[i].SizeBytes)))
                  .Append('}');
            }
            sb.Append("]}");
            sb.Append(",\"travamentos\":").Append(d.Memory.CrashesLast30Days.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"operacoes\":[");
            for (int i = 0; i < _catalog.Count; i++)
            {
                MaintenanceOperation op = _catalog[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Json(op.Id))
                  .Append(",\"rotulo\":").Append(Json(OpText(op.Id, "label", op.Label)))
                  .Append(",\"descricao\":").Append(Json(OpText(op.Id, "desc", op.Description)))
                  .Append(",\"ganho\":").Append(Json(OpText(op.Id, "gain", op.ExpectedGain)))
                  .Append(",\"risco\":").Append(Json(RiskLabel(op.Risk)))
                  .Append(",\"confirmar\":").Append(op.Risk != RiskLevel.Safe ? "true" : "false")
                  .Append(",\"irreversivel\":").Append(op.Irreversible ? "true" : "false")
                  .Append(",\"marcado\":").Append(op.DefaultOn ? "true" : "false")
                  .Append(",\"diagnostico\":").Append(op.Kind == OperationKind.Diagnostic ? "true" : "false")
                  .Append('}');
            }
            sb.Append("]}");
            Post(sb.ToString());
        }

        // ── phase 2: scan ───────────────────────────────────────────────────────────

        private void DoScan(string json)
        {
            if (_diagnosis == null) { DoDiagnosis(); if (_diagnosis == null) return; }

            Busy(true, L("mnt.busy.scan"));
            try
            {
                List<string> ids = Ids(json, "ops");
                var selected = new List<MaintenanceOperation>();
                foreach (MaintenanceOperation op in _catalog)
                    if (ids.Contains(op.Id)) selected.Add(op);

                var runner = new MaintenanceRunner(Log);
                _plan = runner.Scan(selected, _diagnosis!,
                                    _artworkRoots.Count > 0 ? _artworkRoots : null);

                PostScan(_plan);
            }
            finally { BusyDone(); }
        }

        private void PostScan(MaintenancePlan plan)
        {
            var sb = new StringBuilder();
            sb.Append("{\"tipo\":\"varredura\"");
            sb.Append(",\"totalArquivos\":").Append(plan.TotalCandidateFiles.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"totalTamanho\":").Append(Json(FreedSpaceReport.Format(plan.TotalCandidateBytes)));
            sb.Append(",\"pastas\":[");
            for (int i = 0; i < plan.ArtworkRoots.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Json(plan.ArtworkRoots[i]));
            }
            sb.Append("],\"itens\":[");

            bool first = true;
            foreach (KeyValuePair<string, CleanupScan> kv in plan.Scans)
            {
                CleanupScan scan = kv.Value;
                if (!first) sb.Append(',');
                first = false;

                sb.Append("{\"id\":").Append(Json(scan.RuleId))
                  .Append(",\"rotulo\":").Append(Json(OpText(scan.RuleId, "label", scan.Label)))
                  .Append(",\"arquivos\":").Append(scan.Candidates.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"tamanho\":").Append(Json(FreedSpaceReport.Format(scan.TotalBytes)))
                  .Append(",\"recentesPreservados\":").Append(scan.SkippedTooNew.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"evidenciaPreservada\":").Append(scan.KeptAsEvidence.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"semAcesso\":").Append(scan.UnreadableFolders.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"exemplos\":[");

                // The 40 biggest, because the operator reviews by size — this is the list that makes
                // deleting Backup_of_*.cdr next to real artwork an informed decision.
                var byteOrder = new List<CleanupCandidate>(scan.Candidates);
                byteOrder.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
                for (int i = 0; i < byteOrder.Count && i < 40; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"caminho\":").Append(Json(byteOrder[i].FullPath))
                      .Append(",\"tamanho\":").Append(Json(FreedSpaceReport.Format(byteOrder[i].SizeBytes)))
                      .Append(",\"data\":").Append(Json(byteOrder[i].LastWriteUtc.ToLocalTime().ToString("dd/MM/yyyy")))
                      .Append('}');
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            Post(sb.ToString());
        }

        // ── phase 3: execute ────────────────────────────────────────────────────────

        private void DoExecute(string json)
        {
            if (_diagnosis == null || _plan == null)
            {
                Log(L("mnt.err.scanfirst"));
                return;
            }

            if (_diagnosis.DestructiveBlocked)
            {
                Log("BLOQUEADO: " + _diagnosis.BlockReason);
                Post("{\"tipo\":\"bloqueado\",\"motivo\":" + Json(_diagnosis.BlockReason) + "}");
                return;
            }

            Busy(true, L("mnt.busy.exec"));
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    _plan.IncludeUpdateCleanup = Bool(root, "updateCleanup", false);
                    _plan.ComponentStoreResetBase = Bool(root, "resetBase", false);
                }

                // A restore point before anything irreversible — created AND verified, and if it could not
                // be created the operator is told, not reassured.
                if (_plan.HasIrreversible)
                {
                    Log("Tentando criar um ponto de restauração…");
                    RestorePointResult rp = RestorePoint.Create("Optimus Manutenção " + Program.VersionTag);
                    Log(rp.Message);
                    Post("{\"tipo\":\"restauracao\",\"criado\":" + (rp.Created ? "true" : "false")
                       + ",\"protegido\":" + (rp.HasUsableProtection ? "true" : "false")
                       + ",\"mensagem\":" + Json(rp.Message) + "}");
                }

                var runner = new MaintenanceRunner(Log);
                FreedSpaceReport report = runner.Execute(_plan, _diagnosis!);

                Post("{\"tipo\":\"resultado\",\"manchete\":" + Json(Headline(report))
                   + ",\"ressalva\":" + Json(Caveat(report))
                   + ",\"arquivos\":" + report.TotalFilesRemoved.ToString(CultureInfo.InvariantCulture)
                   + ",\"preservados\":" + report.TotalFilesLocked.ToString(CultureInfo.InvariantCulture)
                   + ",\"estimativa\":" + Json(FreedSpaceReport.Format(report.EstimatedBytes))
                   + ",\"categorias\":" + Categories(report) + "}");

                // Re-read the machine so the panel shows the new free space.
                _diagnosis = new MaintenanceRunner(Log).Diagnose();
                PostDiagnosis(_diagnosis);
            }
            finally { BusyDone(); }
        }

        private string Categories(FreedSpaceReport report)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < report.Categories.Count; i++)
            {
                CategoryResult c = report.Categories[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"rotulo\":").Append(Json(OpText(c.Id, "label", c.Label)))
                  .Append(",\"tamanho\":").Append(Json(FreedSpaceReport.Format(c.BytesRemoved)))
                  .Append(",\"arquivos\":").Append(c.FilesRemoved.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"preservados\":").Append(c.FilesLocked.ToString(CultureInfo.InvariantCulture))
                  .Append('}');
            }
            return sb.Append(']').ToString();
        }

        // ── startup audit ───────────────────────────────────────────────────────────

        private void DoStartupAudit()
        {
            Busy(true, L("mnt.busy.startup"));
            try
            {
                _startup = StartupAudit.All();

                var sb = new StringBuilder("{\"tipo\":\"inicializacao\",\"itens\":[");
                for (int i = 0; i < _startup.Count; i++)
                {
                    StartupEntry en = _startup[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"indice\":").Append(i.ToString(CultureInfo.InvariantCulture))
                      .Append(",\"nome\":").Append(Json(en.Name))
                      .Append(",\"comando\":").Append(Json(en.Command))
                      .Append(",\"origem\":").Append(Json(SurfaceLabel(en.Surface)))
                      .Append(",\"local\":").Append(Json(en.Location))
                      .Append(",\"ativo\":").Append(en.Enabled ? "true" : "false")
                      .Append(",\"protegido\":").Append(en.IsProtected ? "true" : "false")
                      .Append(",\"motivo\":").Append(Json(en.ProtectedBecause))
                      .Append(",\"ausente\":").Append(en.TargetMissing ? "true" : "false")
                      .Append('}');
                }
                sb.Append("]}");
                Post(sb.ToString());
            }
            finally { BusyDone(); }
        }

        private void DoStartupToggle(string json)
        {
            int index;
            bool enable;
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                index = (int)Num(doc.RootElement, "indice", -1);
                enable = Bool(doc.RootElement, "ativar", false);
            }

            if (index < 0 || index >= _startup.Count) return;

            StartupEntry entry = _startup[index];
            bool ok = enable
                ? StartupAudit.Enable(entry, out string message)
                : StartupAudit.Disable(entry, out message);

            Log((ok ? "" : "FALHOU: ") + entry.Name + " — " + message);
            Post("{\"tipo\":\"inicializacao-resultado\",\"indice\":" + index
               + ",\"ok\":" + (ok ? "true" : "false")
               + ",\"ativo\":" + (entry.Enabled ? "true" : "false")
               + ",\"mensagem\":" + Json(message) + "}");
        }

        // ── performance tab ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads every performance adjustment and reports its CURRENT state. Read-only: nothing is applied
        /// until the operator asks for a specific one, and an adjustment already in the optimized state is
        /// reported as such instead of being offered again.
        /// </summary>
        private void DoPerformance()
        {
            Busy(true, L("mnt.busy.perf"));
            try
            {
                _tweaks = new PerformanceTuner(Log).Read();
                PostPerformance();
            }
            finally { BusyDone(); }
        }

        private void PostPerformance()
        {
            var sb = new StringBuilder("{\"tipo\":\"desempenho\",\"itens\":[");

            for (int i = 0; i < _tweaks.Count; i++)
            {
                PerformanceTweak t = _tweaks[i];
                if (i > 0) sb.Append(',');

                sb.Append("{\"id\":").Append(Json(t.Id))
                  .Append(",\"rotulo\":").Append(Json(TweakText(t.Id, "label", t.Label)))
                  .Append(",\"descricao\":").Append(Json(TweakText(t.Id, "desc", t.Description)))
                  .Append(",\"troca\":").Append(Json(TweakText(t.Id, "tradeoff", t.Tradeoff)))
                  .Append(",\"estado\":").Append(Json(StateLabel(t.State)))
                  .Append(",\"acionavel\":").Append(t.IsActionable ? "true" : "false")
                  .Append(",\"otimizado\":").Append(t.State == TweakState.Optimized ? "true" : "false")
                  .Append(",\"atual\":").Append(Json(t.CurrentValue))
                  .Append(",\"depois\":").Append(Json(t.OptimizedValue))
                  .Append(",\"motivo\":").Append(Json(t.NotApplicableBecause))
                  .Append(",\"confirmar\":").Append(t.Risk != RiskLevel.Safe ? "true" : "false")
                  .Append(",\"explorer\":").Append(t.NeedsExplorerRestart ? "true" : "false")
                  .Append(",\"sair\":").Append(t.NeedsSignOut ? "true" : "false")
                  .Append(",\"marcado\":").Append(t.DefaultOn && t.IsActionable ? "true" : "false")
                  .Append('}');
            }

            sb.Append("]}");
            Post(sb.ToString());
        }

        private void DoTweak(string json, bool apply)
        {
            string id;
            using (JsonDocument doc = JsonDocument.Parse(json)) id = Str(doc.RootElement, "id");
            if (string.IsNullOrEmpty(id)) return;

            var tuner = new PerformanceTuner(Log);
            bool ok = apply
                ? tuner.Apply(id, _artworkRoots, out string message)
                : tuner.Revert(id, out message);

            Log((ok ? "" : "FALHOU: ") + id + " — " + message);

            // Explorer only restarts when the change needs it AND it worked — a restart for nothing is
            // two seconds of blank taskbar the operator did not agree to.
            if (ok && apply && NeedsExplorerRestart(id))
                ExplorerHost.WithExplorerStopped(() => { }, Log);

            // Re-read: the table must show what the machine actually reports now, never what we assumed
            // our own write achieved.
            _tweaks = tuner.Read();
            PostPerformance();
        }

        private bool NeedsExplorerRestart(string id)
        {
            foreach (PerformanceTweak t in _tweaks)
                if (t.Id == id) return t.NeedsExplorerRestart;
            return false;
        }

        private string StateLabel(TweakState state)
        {
            switch (state)
            {
                case TweakState.Optimized: return L("mnt.perf.state.optimized");
                case TweakState.Default: return L("mnt.perf.state.default");
                case TweakState.NotApplicable: return L("mnt.perf.state.na");
                default: return L("mnt.perf.state.unknown");
            }
        }

        /// <summary>Same key-with-fallback pattern as the cleanup operations.</summary>
        private string TweakText(string id, string part, string fallback)
        {
            string key = "mnt.tweak." + id + "." + part;
            string value = L(key);
            return value == key ? fallback : value;
        }

        // ── memory screen ───────────────────────────────────────────────────────────

        private void DoMemory()
        {
            MemoryReport m = MemoryDiagnostics.Read();

            var sb = new StringBuilder("{\"tipo\":\"memoria\"");
            sb.Append(",\"total\":").Append(Json(FreedSpaceReport.Format(m.TotalBytes)));
            sb.Append(",\"disponivel\":").Append(Json(FreedSpaceReport.Format(m.AvailableBytes)));
            sb.Append(",\"usadoPct\":").Append(m.UsedPercent.ToString("0", CultureInfo.InvariantCulture));
            sb.Append(",\"comprometidoPct\":").Append(m.CommitPercent.ToString("0", CultureInfo.InvariantCulture));
            sb.Append(",\"pressao\":").Append(m.UnderRealPressure ? "true" : "false");
            sb.Append(",\"travamentos\":").Append(m.CrashesLast30Days.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"porqueNaoLimparRam\":").Append(Json(MemoryDiagnostics.WhyNoRamCleaning()));
            sb.Append(",\"porqueNaoPrefetch\":").Append(Json(MemoryDiagnostics.WhyNoPrefetchCleaning()));
            sb.Append(",\"consumidores\":[");
            for (int i = 0; i < m.TopConsumers.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"nome\":").Append(Json(m.TopConsumers[i].Name))
                  .Append(",\"memoria\":").Append(Json(FreedSpaceReport.Format(m.TopConsumers[i].WorkingSetBytes)))
                  .Append('}');
            }
            sb.Append("]}");
            Post(sb.ToString());
        }

        // ── helpers ─────────────────────────────────────────────────────────────────

        private void PickFolder()
        {
            _runOnUi(() =>
            {
                try
                {
                    const string prompt = "Escolha a pasta onde a arte fica guardada";

                    // The modern picker, same as the docker. The old SHBrowseForFolder stays as a
                    // fallback: on a machine where the modern dialog cannot be created, an ugly
                    // dialog beats no dialog.
                    string? chosen = Optimus.Windows.FolderPicker.Pick(prompt);
                    if (chosen == null)
                    {
                        using (var dialog = new System.Windows.Forms.FolderBrowserDialog
                        {
                            Description = prompt,
                            ShowNewFolderButton = false,
                        })
                        {
                            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                            chosen = dialog.SelectedPath;
                        }
                    }

                    {
                        if (string.IsNullOrWhiteSpace(chosen)) return;

                        if (!_artworkRoots.Contains(chosen))
                            _artworkRoots.Add(chosen);

                        var sb = new StringBuilder("{\"tipo\":\"pastas\",\"itens\":[");
                        for (int i = 0; i < _artworkRoots.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append(Json(_artworkRoots[i]));
                        }
                        Post(sb.Append("]}").ToString());
                    }
                }
                catch (Exception ex) { MaintenanceLog.Write("escolher-pasta: " + ex.Message); }
            });
        }

        /// <summary>
        /// Switches language, persists it and reloads. Persistence goes through the injected store, so the
        /// choice survives closing the app — and it is shared with the CorelDRAW docker, which is what the
        /// operator expects from one product.
        /// </summary>
        private void SetLanguage(string json)
        {
            string tag;
            using (JsonDocument doc = JsonDocument.Parse(json)) tag = Str(doc.RootElement, "tag");

            _i18n.SetLanguage(tag);
            MaintenanceLog.Write("Idioma: " + _i18n.Current);
            _runOnUi(_reloadForLanguage);
        }

        private void OpenLog()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = MaintenanceLog.Path,
                    UseShellExecute = true,
                })?.Dispose();
            }
            catch (Exception ex) { MaintenanceLog.Write("abrir-log: " + ex.Message); }
        }

        /// <summary>
        /// Runs work off the UI thread, one item at a time, through a QUEUE.
        ///
        /// <para>
        /// It used to reject a second command with "já existe uma operação em andamento" — which broke the
        /// very first thing the app does: the page asks for the diagnosis and the memory reading together
        /// at boot, so the memory screen simply never loaded (seen in Davi's log, 12:06:22). Queuing is
        /// also what the operator expects from clicking two buttons in a row.
        /// </para>
        /// <para>
        /// Duplicates of a command already waiting are dropped: holding a button down must not schedule
        /// twenty disk scans.
        /// </para>
        /// </summary>
        private void Background(string label, Action work)
        {
            lock (_queueLock)
            {
                foreach (KeyValuePair<string, Action> pending in _queue)
                    if (pending.Key == label) return;   // already scheduled

                _queue.Enqueue(new KeyValuePair<string, Action>(label, work));

                if (_busy) return;                      // the running pump will pick it up
                _busy = true;
            }

            var thread = new Thread(Pump) { IsBackground = true, Name = "OptimusManutencao" };
            thread.Start();
        }

        private void Pump()
        {
            while (true)
            {
                Action work;
                lock (_queueLock)
                {
                    if (_queue.Count == 0) { _busy = false; break; }
                    work = _queue.Dequeue().Value;
                }

                try { work(); }
                catch (Exception ex)
                {
                    MaintenanceLog.Write("trabalho em segundo plano falhou: " + ex);
                    Log(L("mnt.err.generic", ex.Message));
                }
            }

            Busy(false, "");
        }

        /// <summary>
        /// Ends one queued task. It clears the spinner only when nothing else is waiting — otherwise the
        /// window would flick to "Pronto." between two commands the operator asked for in one go.
        /// </summary>
        private void BusyDone()
        {
            lock (_queueLock)
            {
                if (_queue.Count > 0) return;
            }
            Busy(false, "");
        }

        private void Busy(bool busy, string label) =>
            Post("{\"tipo\":\"ocupado\",\"ocupado\":" + (busy ? "true" : "false")
               + ",\"rotulo\":" + Json(label) + "}");

        private void Log(string message)
        {
            MaintenanceLog.Write(message);
            Post("{\"tipo\":\"log\",\"texto\":" + Json(message) + "}");
        }

        private void Post(string json)
        {
            _runOnUi(() =>
            {
                try
                {
                    // ExecuteScriptAsync, not PostWebMessageAsJson: the message channel proved unreliable
                    // inside a foreign dispatcher in the add-in, and one technique for both hosts is safer.
                    _core.ExecuteScriptAsync("window.optimusReceive(" + json + ")");
                }
                catch (Exception ex) { MaintenanceLog.Write("Post falhou: " + ex.Message); }
            });
        }

        private string RiskLabel(RiskLevel risk)
        {
            switch (risk)
            {
                case RiskLevel.Safe: return L("mnt.risk.safe");
                case RiskLevel.NeedsConfirmation: return L("mnt.risk.confirm");
                default: return L("mnt.risk.irreversible");
            }
        }

        private string SurfaceLabel(StartupSurface surface)
        {
            switch (surface)
            {
                case StartupSurface.UserRun: return L("mnt.surface.userrun");
                case StartupSurface.MachineRun: return L("mnt.surface.machinerun");
                case StartupSurface.MachineRun32: return L("mnt.surface.machinerun32");
                case StartupSurface.StartupFolder: return L("mnt.surface.folder");
                default: return L("mnt.surface.task");
            }
        }

        private string MediaLabel(MediaKind media)
        {
            switch (media)
            {
                case MediaKind.Hdd: return L("mnt.media.hdd");
                case MediaKind.Ssd: return L("mnt.media.ssd");
                case MediaKind.Scm: return L("mnt.media.scm");
                default: return L("mnt.media.unknown");
            }
        }

        private string HealthLabel(DriveHealth health)
        {
            switch (health)
            {
                case DriveHealth.Healthy: return L("mnt.health.healthy");
                case DriveHealth.Warning: return L("mnt.health.warning");
                case DriveHealth.Unhealthy: return L("mnt.health.unhealthy");
                default: return L("mnt.health.unknown");
            }
        }

        private string OptimizeLabel(MediaKind media)
        {
            switch (media)
            {
                case MediaKind.Hdd: return L("mnt.optimize.hdd");
                case MediaKind.Ssd: return L("mnt.optimize.ssd");
                case MediaKind.Scm: return L("mnt.optimize.scm");
                default: return L("mnt.optimize.unknown");
            }
        }

        /// <summary>
        /// The label an operation shows, resolved from <c>mnt.op.&lt;id&gt;.&lt;part&gt;</c> with the rule's
        /// own pt-BR text as fallback — so a rule added without translation keys still reads correctly.
        /// </summary>
        private string OpText(string id, string part, string fallback)
        {
            string key = "mnt.op." + id + "." + part;
            string value = L(key);
            return value == key ? fallback : value;
        }

        /// <summary>
        /// Rebuilds the honest headline in the operator's language. It stays a composition of the SAME
        /// cases <see cref="FreedSpaceReport"/> decides between — the decision is C#'s, only the wording is
        /// translated, so no language can accidentally state a bigger number than the drive gained.
        /// </summary>
        private string Headline(FreedSpaceReport report)
        {
            if (report.AnotherProcessWrote) return L("mnt.head.concurrent");
            if (report.RealFreedBytes == 0) return L("mnt.head.nothing");

            string amount = FreedSpaceReport.Format(report.RealFreedBytes);
            return report.BelowNoiseFloor ? L("mnt.head.about", amount) : L("mnt.head.exact", amount);
        }

        private string Caveat(FreedSpaceReport report) =>
            report.EstimatedBytes > report.RealFreedBytes && report.RealFreedBytes > 0
                ? L("mnt.caveat.over")
                : L("mnt.caveat.plain");

        private static List<string> Ids(string json, string property)
        {
            var ids = new List<string>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty(property, out JsonElement array)
                        && array.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement item in array.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.String) ids.Add(item.GetString() ?? "");
                }
            }
            catch (Exception) { }
            return ids;
        }

        private static string Str(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";

        private static bool Bool(JsonElement root, string name, bool fallback) =>
            root.TryGetProperty(name, out JsonElement v)
            && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
                ? v.GetBoolean() : fallback;

        private static double Num(JsonElement root, string name, double fallback) =>
            root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDouble() : fallback;

        private static string Json(string? value) => JsonSerializer.Serialize(value ?? "");
    }
}
