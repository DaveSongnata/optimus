using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Optimus.AddIn.Core;
using Optimus.Core.Audit;
using Optimus.Core.Diagnostics;
using Optimus.Core.Fonts;
using Optimus.Core.I18n;
using Optimus.Core.Optimization;
using Optimus.Core.Palettes;
using Optimus.Core.Voice;
using Optimus.Interop;
using Optimus.Windows;

namespace Optimus.AddIn.Ui
{
    /// <summary>
    /// Host-agnostic core of the Optimus UI: owns the JS↔C# dispatch, runs the single
    /// optimize command against the live CorelDRAW Application, and posts results back to the
    /// page. The docker (<see cref="OptimusDocker"/>) creates a WebView2, wires its
    /// <see cref="CoreWebView2"/> to one of these, and stays dumb.
    /// </summary>
    public sealed class OptimusBridge
    {
        private readonly CoreWebView2 _core;
        private readonly dynamic _app;
        private readonly Action<Action> _runOnUi;
        private readonly LocalizationService _i18n;
        private readonly Action? _reloadForLanguage;
        private bool _running;

        // Colour table (F9) — the palette registry is a shop-wide catalog, not a per-document thing,
        // so it is loaded once per docker session. The last colour audit is cached so palette CRUD
        // commands (add/rename/replace) can refresh the table without re-walking the document.
        private readonly PaletteRegistry _palettes = PaletteStore.Service();
        private ColorAudit? _lastColorAudit;
        private bool _usageMeasured;

        // De-dupe for "colorFromSelection": the page polls this every 2.5s while the Cores tab is
        // open, and re-posting (and re-rendering) the same answer on every tick would fight whatever
        // the operator is doing with the detail panel. Reset whenever the palette itself changes
        // (see PostColorTable) so a colour just registered flips from "fora da paleta" to "cadastrada"
        // on the very next tick, even with the same object still selected.
        private string _lastSelectionSignature = "";

        // Font manager (F10) — same shop-wide catalog shape.
        private readonly FontRegistry _fontRegistry = FontRegistryStore.Service();

        // Voice command (F11) — fully offline (whisper.cpp locally, no API key, no network). One
        // recorder/transcriber per docker session; the model is loaded lazily on first use so a
        // session that never touches voice never pays for it.
        private readonly VoiceRecorder _voiceRecorder = new VoiceRecorder();
        private readonly LocalVoiceTranscriber _voiceTranscriber = new LocalVoiceTranscriber(InstallDir());

        // Confirmation audio: pre-rendered WAV, never the machine's own installed TTS voices (Davi
        // rejected speechSynthesis outright, 2026-08-15 — quality varies per machine).
        private readonly VoicePlayer _voicePlayer = new VoicePlayer(InstallDir());

        public OptimusBridge(CoreWebView2 core, object app, Action<Action> runOnUi,
                             LocalizationService? i18n = null, Action? reloadForLanguage = null)
        {
            _core = core;
            _app = app;
            _runOnUi = runOnUi;
            // A default service (pt-BR, no persistence) keeps the legacy 3-argument constructor working —
            // the docker never has to be constructed differently just to have strings.
            _i18n = i18n ?? new LocalizationService();
            _reloadForLanguage = reloadForLanguage;
            _core.WebMessageReceived += OnWebMessage;

            // Logged unconditionally at boot, not only when the page asks: a "modelo de voz não
            // encontrado" report with no earlier log line to confirm the checked path wasted a whole
            // round trip diagnosing it once already.
            OptimusLog.Write($"Voz: modelo {(_voiceTranscriber.ModelAvailable ? "encontrado" : "AUSENTE")} " +
                              $"em \"{_voiceTranscriber.ModelPath}\"");
        }

        /// <summary>Localized string in the operator's current language.</summary>
        private string L(string key) => _i18n[key];

        /// <summary>
        /// Switches language and asks the host to re-inject the catalog and reload. The choice is persisted
        /// through the injected store, and it is the SAME store the maintenance app uses — one product, one
        /// language.
        /// </summary>
        private void SetLanguage(string tag)
        {
            _i18n.SetLanguage(tag);
            OptimusLog.Write("Idioma: " + _i18n.Current);
            if (_reloadForLanguage != null) _runOnUi(_reloadForLanguage);
        }

        private static string root_tag(string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json)) return Str(doc.RootElement, "tag");
            }
            catch (Exception) { return ""; }
        }

        public void Detach()
        {
            try { _core.WebMessageReceived -= OnWebMessage; } catch { }
            try { _voiceRecorder.Dispose(); } catch { }
            try { _voiceTranscriber.Dispose(); } catch { }
        }

        // Runs on CorelDRAW's STA event thread — an uncaught exception here would take the whole
        // process down. Never let one escape.
        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                // The page sends OBJECTS (postMessage({...})), so read the message as JSON.
                // TryGetWebMessageAsString throws ("Value does not fall within the expected
                // range") for anything that isn't a bare JS string — same fix as the SisCut
                // installer, which reads WebMessageAsJson.
                string json;
                try { json = e.WebMessageAsJson; }
                catch (Exception ex) { OptimusLog.Write("OnWebMessage read FAILED: " + ex.Message); return; }

                string cmd;
                var opt = new OptimizeOptions();
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    cmd = Str(root, "cmd");
                    opt.ToleranceMm = Num(root, "tol", 0.03);
                    opt.RemoveEmptyLayers = Bool(root, "emptylayers", true);
                    opt.RemoveOffPage = Bool(root, "offpage", false);
                    opt.CloseAndReopen = Bool(root, "reopen", true);
                    opt.SelectionOnly = Bool(root, "selection", false);
                    opt.CountNodes = Bool(root, "countnodes", true);
                }

                // Lightweight status queries are ALWAYS answered, even mid-run: "auditar" alone can
                // take 90+ seconds (measured), and a voiceStatus/status arriving in that window used
                // to be dropped SILENTLY — no log line, nothing — which is exactly how "modelo de voz
                // não encontrado" could show up on a machine where the model genuinely is present:
                // the page's own boot-time voiceStatus request never got answered, so its local
                // "model available" flag just stayed at its default false forever.
                // fontPreview joins this list for a different reason than the other two: it is pure
                // disk I/O (FontFileLocator reading one font file), never touches COM/the STA thread,
                // and never mutates any field this bridge keeps — so it has no reason to wait behind
                // an audit. Reported on a real file (2026-09): "digito 'marcelo' no texto de amostra e
                // não aparece". Root cause had nothing to do with the typed text — requestFontPreview()
                // marks a font as "already asked" BEFORE sending, so the one request that landed
                // during an audit's busy window got silently DROPPED and NEVER retried; that font's
                // specimen (opacity:0 by default, only turned visible by the response this bridge
                // never sent) stayed invisible for the rest of the session no matter what was typed.
                bool isStatusQuery = cmd == "status" || cmd == "voiceStatus" || cmd == "fontPreview";

                if (_running && !isStatusQuery)
                {
                    OptimusLog.Write("OnWebMessage: DROPPED (busy) cmd=" + cmd);

                    // A dropped voiceStart/voiceStop used to leave the mic button stuck forever on
                    // "abrindo o microfone" — the page had already shown that state optimistically and
                    // was waiting for a reply that would never come, because a PREVIOUS command (an
                    // "auditar" can run 90+ seconds) was still using _running. Answering explicitly
                    // here — even just "busy" — is what lets the button recover instead of hanging
                    // (measured on Davi's machine, 2026-08-15: "depois que reconhece, fica só abrindo
                    // o microfone").
                    if (cmd == "voiceStart" || cmd == "voiceStop")
                        Post(new { type = "voiceResult", ok = false, error = L("opt.voice.busy") });

                    return;
                }

                // Perguntas de estado são feitas pela própria página, em laço, e não descrevem
                // nada que o operador tenha pedido. Registrar cada uma transforma o log num
                // teletipo onde a ação de verdade fica perdida no meio.
                if (!IsChatty(cmd)) OptimusLog.Write("OnWebMessage: " + json);

                switch (cmd)
                {
                    case "status": PostStatus(); break;

                    // A JS exception used to be completely invisible: the page died mid-render and
                    // both the operator and docker.log saw nothing. One missing helper (esc) silently
                    // broke every render function for weeks. Never again — the page reports its own
                    // errors here.
                    // The raw message is logged verbatim (line/col arrive as numbers, not strings).
                    case "jsError": OptimusLog.Write("JS ERROR: " + json); break;
                    case "analisar": RunInspect(opt); break;
                    case "estimar": RunEstimate(json); break;
                    case "auditar": RunAudit(false); break;
                    // Segunda passada, sob pedido: mede quanto cada cor ocupa de verdade.
                    case "medirUso": RunAudit(true); break;
                    case "aplicar": RunApply(json); break;
                    case "otimizar": RunOptimize(opt); break;   // legacy fixed recipe
                    case "idioma": SetLanguage(root_tag(json)); break;

                    // ── colour table (F9) ──────────────────────────────────────────────
                    case "paletteAdd": PaletteAdd(json); break;
                    case "paletteRemove": PaletteRemove(json); break;
                    case "paletteRename": PaletteRename(json); break;
                    case "paletteColorAdd": PaletteColorAdd(json); break;
                    case "paletteColorRemove": PaletteColorRemove(json); break;
                    case "paletteColorRename": PaletteColorRename(json); break;
                    case "paletteCapture": PaletteCapture(json); break;
                    case "paletteSetActive": PaletteSetActive(json); break;
                    // A tela pede a lista ao abrir. Nao depende de documento analisado: paletas sao
                    // da grafica e existem antes de qualquer arquivo.
                    case "paletteList": PostPalettes(); break;
                    case "paletteConfirmSet": PaletteConfirmSet(json); break;
                    case "paletteApply": RunPaletteApply(json); break;
                    case "svgPick": SvgPick(); break;
                    case "svgImport": SvgImport(json); break;
                    case "paletteColorAddManual": PaletteColorAddManual(json); break;
                    case "convertColors": RunConvertColors(json); break;
                    case "colorReplace": RunColorReplace(json); break;
                    // Polled while the "Cores" tab is open (see statusPoll): reads the fill of
                    // whatever is selected in CorelDRAW right now and points to it in the palette.
                    case "colorFromSelection": RunColorFromSelection(); break;

                    // ── font manager (F10) ─────────────────────────────────────────────
                    case "fontManager": PostFontManager(); break;
                    case "fontAdd": FontAdd(json); break;
                    case "fontRemove": FontRemove(json); break;
                    case "fontImport": FontImport(json); break;
                    case "fontFolderPick": FontFolderPick(); break;
                    case "fontFolderScan": PostFontFolder(); break;
                    case "fontPreview": PostFontPreview(json); break;

                    // ── voice command (F11) ────────────────────────────────────────────
                    case "voiceStart": VoiceStart(); break;
                    case "voiceStop": RunVoiceStop(); break;
                    case "voiceStatus": PostVoiceStatus(); break;
                    case "voiceDeviceList": PostVoiceDevices(); break;
                    case "voiceDeviceSet": VoiceDeviceSet(json); break;
                }
            }
            catch (Exception ex) { OptimusLog.Write("OnWebMessage UNHANDLED: " + ex); }
        }

        /// <summary>
        /// READ-ONLY inspection: reports what is actually inside the document, including the art
        /// hidden in PowerClips. Answers "why is my file heavy / slow?" without changing anything.
        /// </summary>
        private void RunInspect(OptimizeOptions opt)
        {
            _running = true;
            PostBusy(true, L("opt.busy.analyzing"));
            PostLog("▶ Analisando o documento…", "run");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            OptimusLog.Write("Inspect: START (selectionOnly=" + opt.SelectionOnly + ")");
            try
            {
                InspectResult res = new FileOptimizer(_app, opt).Inspect();
                Optimus.Core.Model.ShapeInventory inv = res.Inventory;
                sw.Stop();
                // Logged so a stuck/slow traversal is diagnosable from docker.log alone, and so the
                // report can never be mistaken for a different file.
                OptimusLog.Write($"Inspect: DONE in {sw.ElapsedMilliseconds} ms — doc=\"{res.DocName}\" " +
                    $"path=\"{res.DocPath}\" shapes={inv.TotalShapes} nodes={inv.TotalNodes} " +
                    $"powerClip={inv.ShapesInPowerClip} raster={inv.RasterShapes} effects={inv.LiveEffects} " +
                    $"reducible={inv.ReducibleShapes}");
                OptimusLog.Write("Inspect: COST " + res.Stats);
                if (res.Composition.Available)
                    OptimusLog.Write($"Inspect: SIZE total={res.Composition.TotalBytes} " +
                        $"raster={res.Composition.PercentOf(CdrComponent.Raster):0.#}% " +
                        $"vector={res.Composition.PercentOf(CdrComponent.Vector):0.#}% " +
                        $"preview={res.Composition.PercentOf(CdrComponent.Preview):0.#}% " +
                        $"icc={res.Composition.PercentOf(CdrComponent.IccProfile):0.#}%({res.IccBytes}B, used={res.IccUsed}) " +
                        $"embFonts={res.Composition.PercentOf(CdrComponent.EmbeddedFonts):0.#}% " +
                        $"lever={res.Ceiling?.DominantLever} | ceiling: lossless={res.Ceiling?.LosslessPercent}% " +
                        $"noRaster={res.Ceiling?.CeilingWithoutRasterPercent}% " +
                        $"resample={res.Ceiling?.CeilingWithResamplingPercent}% " +
                        $"rasterDominated={res.Ceiling?.RasterDominated}");
                if (res.Payload != null)
                    OptimusLog.Write($"Inspect: REPETICAO objetos={res.Payload.ObjectRecords} " +
                        $"distintos={res.Payload.DistinctGeometries} " +
                        $"repetidos={res.Payload.RepeatedSharePercent}% " +
                        $"economiaSemPerda={res.Payload.RepetitionSavingShareOfFile(res.Composition.TotalBytes)}% " +
                        $"({res.Payload.RepetitionSavingBytes} bytes)");
                else
                    OptimusLog.Write("Inspect: SIZE indisponível — " + res.ContainerNote);
                OptimusLog.Write($"Inspect: FLUIDITY score={res.InteractionScore:0} " +
                    $"band={res.InteractionBand} dominant={res.InteractionDominant}");
                if (res.FontNames.Count > 0)
                    OptimusLog.Write("Inspect: FONTS " + string.Join(", ", res.FontNames));
                Post(new
                {
                    type = "inventory",
                    ok = true,
                    doc = res.DocName,
                    path = res.DocPath,
                    ms = sw.ElapsedMilliseconds,
                    nodesCounted = res.NodesCounted,
                    unclassified = res.Unclassified,
                    shapes = inv.TotalShapes,
                    nodes = inv.TotalNodes,
                    nested = res.Nested,
                    reducible = inv.ReducibleShapes,
                    effects = inv.LiveEffects,
                    raster = inv.RasterShapes,

                    // Fluidity (the "does it stall when dragging" objective) — a MODEL, ranked into
                    // a band, never presented as milliseconds. Real timing arrives in Phase 4.
                    fluidity = new
                    {
                        score = Math.Round(res.InteractionScore),
                        band = res.InteractionBand,
                        dominant = res.InteractionDominant,
                    },

                    // Byte composition of the SAVED file — exact, read off the container.
                    size = res.Composition.Available ? new
                    {
                        available = true,
                        totalBytes = res.Composition.TotalBytes,
                        raster = Math.Round(res.Composition.PercentOf(CdrComponent.Raster), 1),
                        vector = Math.Round(res.Composition.PercentOf(CdrComponent.Vector), 1),
                        preview = Math.Round(res.Composition.PercentOf(CdrComponent.Preview), 1),
                        icc = Math.Round(res.Composition.PercentOf(CdrComponent.IccProfile), 1),
                        embFonts = Math.Round(res.Composition.PercentOf(CdrComponent.EmbeddedFonts), 1),
                        fonts = Math.Round(res.Composition.PercentOf(CdrComponent.Fonts), 1),
                        embedded = Math.Round(res.Composition.PercentOf(CdrComponent.Embedded), 1),
                        metadata = Math.Round(res.Composition.PercentOf(CdrComponent.Metadata), 1),
                        iccBytes = res.IccBytes,
                        iccNames = res.IccProfileNames,
                        iccUsed = res.IccUsed,
                        dominantLever = res.Ceiling?.DominantLever ?? "",
                        losslessCeiling = res.Ceiling?.LosslessPercent ?? 0,
                        ceilingNoRaster = res.Ceiling?.CeilingWithoutRasterPercent ?? 0,
                        ceilingResample = res.Ceiling?.CeilingWithResamplingPercent ?? 0,
                        rasterDominated = res.Ceiling?.RasterDominated ?? false,
                        needsResampleFor50 = res.Ceiling?.FiftyPercentRequiresResampling ?? false,

                        // Repeated art (O15): the lever that is LOSSLESS and, on a step-and-repeat
                        // drawing, larger than every other lever combined.
                        objects = res.Payload?.ObjectRecords ?? 0,
                        distinctShapes = res.Payload?.DistinctGeometries ?? 0,
                        repeatedPct = res.Payload?.RepeatedSharePercent ?? 0,
                        repetitionSaving =
                            res.Payload?.RepetitionSavingShareOfFile(res.Composition.TotalBytes) ?? 0,
                    } : (object)new { available = false, note = res.ContainerNote },

                    fontNames = res.FontNames,
                });
                PostLog(
                    $"✔ {res.DocName}: {inv.TotalShapes} objeto(s), " +
                    (res.NodesCounted ? $"{inv.TotalNodes} nó(s), " : "nós não contados, ") +
                    $"{res.Nested} aninhado(s), {inv.RasterShapes} imagem(ns), " +
                    $"{inv.LiveEffects} efeito(s) vivo(s). ({sw.ElapsedMilliseconds} ms)", "ok");
                if (res.Unclassified > 0)
                    PostLog($"⚠ {res.Unclassified} objeto(s) sem tipo reconhecido — a quebra por tipo " +
                            "está incompleta e não deve ser usada como verdade.", "warn");
            }
            catch (OptimizeException ex)
            {
                OptimusLog.Write("Inspect: precondition — " + ex.Message);
                PostLog(ex.Message, "warn");
                Post(new { type = "inventory", ok = false });
            }
            catch (Exception ex)
            {
                OptimusLog.Write("Inspect: FAILED after " + sw.ElapsedMilliseconds + " ms: " + ex);
                PostLog("✘ Falha ao analisar: " + ex.Message, "err");
                Post(new { type = "inventory", ok = false });
            }
            finally
            {
                _running = false;
                PostBusy(false);
                PostStatus();
                OptimusLog.Write("Inspect: END (busy cleared)");
            }
        }

        /// <summary>
        /// Reads the operator's four sliders out of the message. Unknown values fall back to the safe
        /// default rather than to the aggressive one.
        /// </summary>
        private static OptimizationSettings ParseSettings(string messageJson)
        {
            var s = new OptimizationSettings();
            try
            {
                using JsonDocument doc = JsonDocument.Parse(messageJson);
                JsonElement root = doc.RootElement;

                if (Enum.TryParse(Str(root, "color"), true, out ColorFidelity color)) s.Color = color;
                if (Enum.TryParse(Str(root, "image"), true, out ImageQuality image)) s.Image = image;
                if (Enum.TryParse(Str(root, "weight"), true, out DrawingWeight weight)) s.Weight = weight;
                if (Enum.TryParse(Str(root, "space"), true, out ColorSpaceTarget space)) s.Space = space;

                s.RemovePreview = Bool(root, "removePreview", s.RemovePreview);
                s.RemoveEmptyLayers = Bool(root, "removeEmptyLayers", s.RemoveEmptyLayers);
                s.RemoveCompatibilityData = Bool(root, "removeCompat", s.RemoveCompatibilityData);
                s.Reserialize = Bool(root, "reserialize", s.Reserialize);
                s.RemoveOffPage = Bool(root, "removeOffPage", s.RemoveOffPage);
                s.FlattenEffects = Bool(root, "flattenEffects", s.FlattenEffects);
                s.MeasureRedraw = Bool(root, "measureRedraw", s.MeasureRedraw);
                s.InstanceRepeatedArt = Bool(root, "instanceRepeated", s.InstanceRepeatedArt);
            }
            catch (Exception ex) { OptimusLog.Write("ParseSettings fallback to defaults: " + ex.Message); }
            return s;
        }

        /// <summary>
        /// Live estimate for the current slider positions — the operator sees the trade-off BEFORE
        /// committing to it. Read-only: it measures the file on disk and touches nothing.
        /// </summary>
        private void RunEstimate(string messageJson)
        {
            OptimizationSettings settings = ParseSettings(messageJson);
            try
            {
                string path = Safe(() => (string)_app.ActiveDocument.FullFileName, "");
                if (string.IsNullOrWhiteSpace(path))
                {
                    Post(new { type = "estimate", ok = false, reason = L("opt.est.savefirst") });
                    return;
                }

                var reader = new CdrContainerReader();
                CdrContainerReader.Result r = reader.Read(path, analyzePayload: true);
                double? geometryShare = r.Payload?.GeometryShareOfFile(r.Composition.TotalBytes);
                SavingsEstimate est = SavingsEstimator.Estimate(r.Composition, settings, geometryShare);

                Post(new
                {
                    type = "estimate",
                    ok = true,
                    totalBytes = r.Composition.TotalBytes,
                    savedBytes = est.TotalBytes,
                    savedPercent = est.TotalPercent,
                    projectedBytes = est.ProjectedBytes,
                    anyLossy = est.AnyLossy,
                    geometryShare = geometryShare ?? 0,
                    warnings = settings.LossWarningKeys,
                    requiresConfirmation = settings.RequiresConfirmation,
                    items = est.Items.ConvertAll(i => new
                    {
                        label = i.Label,
                        bytes = i.Bytes,
                        percent = i.Percent,
                        lossy = i.Lossy,
                        applicable = i.Applicable,
                        reason = i.NotApplicableReason,
                    }),
                });
            }
            catch (Exception ex)
            {
                OptimusLog.Write("RunEstimate FAILED: " + ex);
                Post(new { type = "estimate", ok = false, reason = ex.Message });
            }
        }

        /// <summary>
        /// Colour + font + accent audit. Read-only: the colour pass copies every colour before
        /// reading it, because CorelDRAW hands out live references.
        /// </summary>
        private void RunAudit() => RunAudit(false);

        private void RunAudit(bool measureUsage)
        {
            _running = true;
            PostBusy(true, L("opt.busy.auditing"));
            PostLog("▶ Auditando cores, fontes e acentuação…", "run");

            // Stage labels are resolved HERE, in the operator's language, rather than in the auditor —
            // the auditor is UI-agnostic and its own Say() strings are diagnostics for the log file.
            string[] stageKeys = { "opt.aud.stage.colors", "opt.aud.stage.fonts", "opt.aud.stage.accents" };
            try
            {
                var r = new DocumentAuditor(_app).Run(
                    s => PostLog("• " + s, "info"),
                    (stage, stageCount, done, total) =>
                    {
                        // A single overall 0-100: each stage owns an equal slice, and the walk inside it
                        // fills that slice. The operator sees one bar that only ever moves forward.
                        double slice = 100.0 / stageCount;
                        double within = total > 0 ? (double)done / total : 0;
                        int percent = (int)Math.Round(stage * slice + within * slice);
                        if (percent > 100) percent = 100;
                        Post(new
                        {
                            type = "auditProgress",
                            percent,
                            step = stage + 1,
                            steps = stageCount,
                            label = L(stageKeys[stage < stageKeys.Length ? stage : stageKeys.Length - 1]),
                            // The COUNT of the operator's own objects, not machinery. Measured
                            // preference (N=425): item counts + elapsed read calmer than a countdown,
                            // and naming the user's own content is what makes a visible wait feel
                            // like work rather than a stall.
                            done,
                            total,
                        });
                    },
                    measureUsage,
                    // O ranking se formando ao vivo. Marcado como PARCIAL: um líder inicial ainda
                    // pode ser ultrapassado, e um número que muda de ideia calado é pior que
                    // número nenhum.
                    soFar => Post(new
                    {
                        type = "colorPartial",
                        partial = true,
                        rows = TopColorRows(soFar, 24),
                    }));
                _lastColorAudit = r.Colors;
                _usageMeasured = r.UsageMeasured;

                Post(new
                {
                    type = "audit",
                    ok = true,
                    doc = r.DocName,
                    ms = r.ElapsedMs,
                    colors = new
                    {
                        unique = r.Colors.Unique.Count,
                        cmyk = r.Colors.CountOf(Optimus.Core.Audit.ColorModel.Cmyk),
                        rgb = r.Colors.CountOf(Optimus.Core.Audit.ColorModel.Rgb),
                        gray = r.Colors.CountOf(Optimus.Core.Audit.ColorModel.Gray),
                        spot = r.Colors.SpotColors().Count,
                        mixed = r.Colors.MixesRgbAndCmyk,
                        notInspectable = r.Colors.NotInspectable,
                        findings = r.Colors.Findings(),
                        spotNames = System.Linq.Enumerable.ToList(
                            System.Linq.Enumerable.Select(r.Colors.SpotColors(), c => c.SpotName)),
                    },
                    fonts = System.Linq.Enumerable.ToList(
                        System.Linq.Enumerable.Select(r.Fonts, f => new
                        {
                            name = f.FontName,
                            verdict = f.Verdict.ToString(),
                            summary = f.Summary(),
                            usable = f.IsUsable,
                            substitutedBy = f.SubstitutedBy,
                            missingRequired = f.MissingRequired.Count,
                            missingWarn = f.MissingWarn.Count,
                        })),
                    textShapes = r.TextShapes,
                    mixedFontStories = r.MixedFontStories,
                    failingFonts = r.FailingFonts.Count,
                });

                PostColorTable();
                PostFontManager();

                // The finding that saves a print job leads the log.
                if (r.FailingFonts.Count > 0)
                    foreach (Optimus.Core.Audit.GlyphCoverage f in r.FailingFonts)
                        PostLog($"⚠ {f.FontName}: {f.Summary()}", "warn");

                PostLog($"✔ {r.Colors.Unique.Count} cor(es), {r.Fonts.Count} fonte(s), " +
                        $"{r.FailingFonts.Count} com problema. ({r.ElapsedMs} ms)",
                        r.FailingFonts.Count > 0 ? "warn" : "ok");
            }
            catch (OptimizeException ex)
            {
                PostLog(ex.Message, "warn");
                Post(new { type = "audit", ok = false });
            }
            catch (Exception ex)
            {
                OptimusLog.Write("RunAudit FAILED: " + ex);
                PostLog("✘ Falha ao auditar: " + ex.Message, "err");
                Post(new { type = "audit", ok = false });
            }
            finally
            {
                _running = false;
                PostBusy(false);
                PostStatus();
            }
        }

        // ── colour table (F9) ────────────────────────────────────────────────────────────

        /// <summary>
        /// Sends the colour table: every colour found by the last audit, its usage share, and
        /// whether it's already registered in a palette — plus the ALERT list (colours the shop has
        /// not standardised) and the palette catalog itself, so the JS side can render everything in
        /// one shot without a second round trip.
        /// </summary>
        /// <summary>The current ranking, biggest first, capped — a live update must stay small enough
        /// to post ~50 times without becoming the cost it is reporting on.</summary>
        private List<object> TopColorRows(ColorAudit audit, int max)
        {
            List<ColorTableRow> rows = PaletteMatcher.BuildTable(audit, _palettes);
            var outRows = new List<object>();
            for (int i = 0; i < rows.Count && i < max; i++) outRows.Add(RowPayload(rows[i]));
            return outRows;
        }

        private static object RowPayload(ColorTableRow row) => new
        {
            key = row.Color.Key,
            model = row.Color.Model.ToString(),
            hex = row.Color.Hex,
            // The swatch colour, read numerically. Color.HexValue's format is undocumented and
            // painted all 128 swatches blank on a real file (2026-08-15).
            rgb = row.Color.Rgb,
            rgbKnown = row.Color.RgbKnown,
            components = row.Color.Components,
            spotName = row.Color.SpotName,
            usage = row.UsagePercent,
            timesUsed = row.TimesUsed,
            registeredAs = row.RegisteredAs,
            registeredPalette = row.RegisteredPalette,
            inPalette = row.InPalette,
            // Only set when this colour is FORA DA PALETA and some registered colour renders as the
            // exact same hex — "por que esse laranja tá fora se eu já cadastrei um laranja igual" is
            // a real support question (measured 2026-09) whose answer is "modelo diferente, mesma
            // aparência", not a bug. Never implies the two are interchangeable for print.
            similarTo = row.SimilarTo,
        };

        /// <summary>
        /// Emits the shop's palettes on their own channel, always — no document required.
        ///
        /// <para>
        /// Palettes belong to the SHOP, not to the file that happens to be open. Sending them inside
        /// the colour-table message tied them to a document audit, so creating a palette before
        /// analysing anything updated nothing on screen and looked like it needed a restart.
        /// </para>
        /// </summary>
        private void PostPalettes()
        {
            Post(new
            {
                type = "palettes",
                palettes = _palettes.Palettes,
                activePalette = _palettes.ActiveName,
                confirmApply = _palettes.ConfirmApply,
            });
        }

        /// <summary>
        /// Why a palette change did NOT happen. It travels on its own channel because a refusal is
        /// not a state update: posting the unchanged palette list would leave the screen looking
        /// exactly as it did before, which reads as "the button is broken".
        /// </summary>
        private void PostPaletteError(PaletteChange status, string subject = "")
        {
            if (status == PaletteChange.Ok) return;
            Post(new { type = "paletteError", code = status.ToString(), subject });
        }

        private void PaletteConfirmSet(string json)
        {
            bool on;
            using (JsonDocument doc = JsonDocument.Parse(json)) on = Bool(doc.RootElement, "on", true);
            _palettes.SetConfirmApply(on);
            PostPalettes();
        }

        private void PostColorTable()
        {
            // Every palette CRUD command and every fresh audit routes through here — the one place
            // that can invalidate the selection cache below without threading a flag through each of
            // them individually.
            _lastSelectionSignature = "";

            if (_lastColorAudit == null) { Post(new { type = "colorTable", ok = false }); return; }

            List<ColorTableRow> rows = PaletteMatcher.BuildTable(_lastColorAudit, _palettes);
            List<ColorRecord> alerts = PaletteMatcher.NotInAnyPalette(_lastColorAudit, _palettes);

            // O que pareceu "impossível" numa tela (uma cor com o hex exato da paleta marcada como
            // fora dela) some em cinco minutos quando as CHAVES aparecem lado a lado no log — sem
            // isso, diagnosticar essa classe de bug exige recriar a sessão inteira e comparar prints.
            // Só quando há paleta ativa E alguma cor ficou de fora: com paleta vazia "tudo fora" não é
            // achado, é o esperado, e logar isso em todo audit encheria o log de ruído.
            if (alerts.Count > 0 && _palettes.Active != null)
            {
                // Raw hex/RGB alongside the (already normalized) key: if a mismatch survives every
                // normalization this file knows about, the raw values are what tells us WHAT to
                // normalize next — measured need, not speculative logging (2026-09, "não entendi, ele
                // considera RGB e a gente lê RGB também" — the key strings alone couldn't answer that).
                List<string> paletteDetail = _palettes.Active.Colors
                    .ConvertAll(c => c.Key + "(hexBruto=" + c.Hex + ")");
                List<string> alertDetail = alerts.ConvertAll(a =>
                    a.Key + "(hexBruto=" + a.Hex + (a.RgbKnown ? ",rgb=" + string.Join(",", a.Rgb) : "") + ")");

                OptimusLog.Write("PaletteMatch: ativa=\"" + _palettes.ActiveName + "\" "
                    + "paletaChaves=[" + string.Join(", ", paletteDetail) + "] "
                    + "foraDaPaleta=[" + string.Join(", ", alertDetail) + "]");
            }

            Post(new
            {
                type = "colorTable",
                ok = true,
                // False on the fast route: the colours are right, but "how much of the drawing" was
                // never measured — so the UI must show no percentage rather than a zero that would
                // read as "barely used".
                usageMeasured = _usageMeasured,
                rows = rows.ConvertAll(RowPayload),
                alerts = alerts.ConvertAll(c => new { key = c.Key, model = c.Model.ToString(), hex = c.Hex, components = c.Components, spotName = c.SpotName }),
                palettes = _palettes.Palettes,
                // Which palette off-palette was measured against. Without it the alert is an
                // accusation with no stated standard, which is why the feature read as useless.
                activePalette = _palettes.ActiveName,
                hasPalette = _palettes.Palettes.Count > 0,
            });
        }

        private void PaletteAdd(string json)
        {
            string name = ReadStr(json, "name");
            if (name.Length == 0) return;

            // The event is emitted only when the change really happened (§30.4). A refused create
            // that still posted "palettes" would tell every consumer a mutation occurred.
            _palettes.AddPalette(name, out PaletteChange status);
            if (status != PaletteChange.Ok) { PostPaletteError(status, name); return; }

            PostPalettes();
            PostColorTable();
        }

        /// <summary>
        /// Chooses which palette the off-palette alert is measured against. The whole point of the
        /// feature: a file is not "a palette file" — it is artwork that happens to contain colours
        /// from inside and outside the standard the shop agreed with THIS client.
        /// </summary>
        private void PaletteSetActive(string json)
        {
            _palettes.SetActive(ReadStr(json, "name"));
            PostPalettes();
            PostColorTable();
        }

        /// <summary>
        /// Adds a colour to a palette from a typed hex value, rather than only from what happens to be
        /// in the open document. A palette has to be buildable before the file that will be checked
        /// against it even exists.
        /// </summary>
        private void PaletteColorAddManual(string json)
        {
            string palette = ReadStr(json, "palette");
            string hex = (ReadStr(json, "hex") ?? "").Trim().TrimStart('#').ToUpperInvariant();
            string name = ReadStr(json, "colorName") ?? "";
            if (palette.Length == 0 || hex.Length != 6) { PostColorTable(); return; }

            _palettes.AddColor(palette, new Optimus.Core.Palettes.PaletteColor
            {
                // A blank name here used to make InPalette itself read as false (it was derived from
                // this field's length) — fixed at the source now (PaletteMatcher.BuildTable sets
                // InPalette directly), but falling back to the hex still means a colour never shows an
                // empty tag, same as PaletteColorAdd/PaletteCapture already do.
                Name = name.Length > 0 ? name : "#" + hex,
                Model = Optimus.Core.Audit.ColorModel.Rgb,
                Hex = hex,
            }, out PaletteChange status);
            if (status != PaletteChange.Ok) { PostPaletteError(status, "#" + hex); return; }

            PostPalettes();
            PostColorTable();
        }

        private void PaletteRemove(string json)
        {
            _palettes.RemovePalette(ReadStr(json, "name"));
            PostPalettes();
            PostColorTable();
        }

        private void PaletteRename(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            string wanted = Str(doc.RootElement, "newName");
            _palettes.RenamePalette(Str(doc.RootElement, "oldName"), wanted, out PaletteChange status);
            if (status != PaletteChange.Ok) { PostPaletteError(status, wanted); return; }

            PostPalettes();
            PostColorTable();
        }

        /// <summary>
        /// Registers a colour that was found in the document (identified by its <c>key</c>) into a
        /// palette — the "cadastro semi-automático" from the whiteboard: the operator names a colour
        /// that already exists in the audit instead of typing RGB/CMYK values by hand.
        /// </summary>
        private void PaletteColorAdd(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            string paletteName = Str(root, "palette");
            string sourceKey = Str(root, "key");
            string name = Str(root, "name");
            if (paletteName.Length == 0 || sourceKey.Length == 0) return;

            // The colour data (model, hex, cmyk) comes straight from the last audit row, matched by
            // key — never re-typed, so the palette entry is byte-identical to what's in the document.
            ColorRecord? source = null;
            if (_lastColorAudit != null)
                foreach (ColorRecord c in _lastColorAudit.Unique)
                    if (c.Key == sourceKey) { source = c; break; }
            if (source == null) return;

            _palettes.AddColor(paletteName, new PaletteColor
            {
                Name = name.Length > 0 ? name : source.Hex,
                Model = source.Model,
                Hex = source.Hex,
                Components = source.Components,
                SpotName = source.SpotName,
            }, out PaletteChange status);
            if (status != PaletteChange.Ok) { PostPaletteError(status, name); return; }

            PostPalettes();
            PostColorTable();
        }

        /// <summary>
        /// "Ao selecionar um objeto, aponta a cor dele na paleta" — reads the fill of whatever is
        /// selected right now and tells the page whether it is registered, so <c>pfShowDetail</c> can
        /// highlight the same swatch/detail a manual click on the colour grid would.
        ///
        /// <para>
        /// Leaf-only and read-only (O24): a group's <c>Fill</c> is an aggregate, never a colour to
        /// report. De-duped by <see cref="_lastSelectionSignature"/> so a 2.5 s poll with nothing new
        /// to say does not re-render the detail panel out from under the operator.
        /// </para>
        /// </summary>
        private void RunColorFromSelection()
        {
            SelectionColorResult result;
            try { result = SelectionColorReader.Read(_app); }
            catch (Exception ex) { OptimusLog.Write("colorFromSelection FAILED: " + ex.Message); return; }

            ColorRecord? color = result.Color;
            string signature = result.Status + "|" + (color?.Key ?? "");
            if (signature == _lastSelectionSignature) return;
            _lastSelectionSignature = signature;

            if (color == null)
            {
                Post(new { type = "selectedColor", ok = false, reason = result.Status.ToString() });
                return;
            }

            // Same numbers the colour table would show for this key, when there IS a table to read
            // them from — a colour picked in CorelDRAW and the same colour clicked in the grid must
            // never disagree about how much of the drawing it covers.
            int timesUsed = 0;
            double usage = 0;
            bool measured = false;
            if (_lastColorAudit != null)
            {
                int total = 0;
                foreach (ColorRecord u in _lastColorAudit.Unique) total += _lastColorAudit.TimesUsed(u);
                foreach (ColorRecord c in _lastColorAudit.Unique)
                {
                    if (c.Key != color.Key) continue;
                    timesUsed = _lastColorAudit.TimesUsed(c);
                    usage = total > 0 ? Math.Round(timesUsed * 100.0 / total, 1) : 0;
                    measured = true;
                    break;
                }
            }

            // The active palette ONLY — same rule PaletteMatcher applies to the audited table.
            // Matching against every registered palette would mean a colour that belongs to a
            // different client's palette reads as "fine", which is exactly the mistake this alert
            // exists to catch.
            string registeredAs = "", registeredPalette = "";
            if (_palettes.ActiveColorsByKey().TryGetValue(color.Key, out PaletteColor? match))
            {
                registeredAs = match!.Name;
                registeredPalette = _palettes.ActiveName;
            }

            Post(new
            {
                type = "selectedColor",
                ok = true,
                key = color.Key,
                model = color.Model.ToString(),
                hex = color.Hex,
                rgb = color.Rgb,
                rgbKnown = color.RgbKnown,
                components = color.Components,
                spotName = color.SpotName,
                usage,
                timesUsed,
                measured,
                registeredAs,
                registeredPalette,
                inPalette = registeredAs.Length > 0,
            });
        }

        private void PaletteColorRemove(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            _palettes.RemoveColor(Str(root, "palette"), Str(root, "key"));
            PostPalettes();
            PostColorTable();
        }

        private void PaletteColorRename(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            _palettes.RenameColor(Str(root, "palette"), Str(root, "key"), Str(root, "name"));
            PostPalettes();
            PostColorTable();
        }

        /// <summary>
        /// Saves EVERY colour of the audited document as one new palette, in a single action.
        ///
        /// <para>
        /// This is how a shop actually acquires a client's brand palette: not by typing colours in one
        /// by one, but by pointing at a file that is already approved and saying "these are the
        /// colours". Registering them individually — the only route the first version offered — is a
        /// data-entry chore nobody would ever finish for a 128-colour file.
        /// </para>
        /// </summary>
        private void PaletteCapture(string json)
        {
            string name = ReadStr(json, "name");
            if (name.Length == 0 || _lastColorAudit == null) return;

            // Through the SAME import route as the SVG: one place decides that a name is free and
            // that a colour is not already in the palette. Building the list here and pushing it into
            // Colors directly — which is what this did — walked around both rules (O25).
            var captured = new List<PaletteColor>();
            foreach (ColorRecord c in _lastColorAudit.Unique)
            {
                captured.Add(new PaletteColor
                {
                    // Named by its own value: the operator renames the few that matter afterwards,
                    // instead of being forced to name all 128 up front.
                    Name = c.SpotName.Length > 0 ? c.SpotName : (c.Hex.Length > 0 ? "#" + c.Hex : c.Model.ToString()),
                    Model = c.Model,
                    Hex = c.Hex,
                    Components = c.Components,
                    SpotName = c.SpotName,
                });
            }

            ColorPalette? palette = _palettes.ImportPalette(name, captured, out PaletteChange status);
            if (palette == null) { PostPaletteError(status, name); return; }

            OptimusLog.Write($"Paleta capturada: \"{name}\" com {palette.Colors.Count} cor(es).");
            PostLog($"✔ Paleta \"{name}\" criada com {palette.Colors.Count} cor(es).", "ok");
            PostPalettes();
            PostColorTable();
        }

        /// <summary>
        /// Replaces one colour with another — "somente selecionado" or "todas as cores", exactly the
        /// two checkboxes on the whiteboard.
        /// </summary>
        /// <summary>
        /// Converts every uniform fill and outline in the document (or the selection) to one colour
        /// model. This is the action behind the "RGB e CMYK misturados" finding: until now the audit
        /// could name the problem and offer no way to fix it.
        /// </summary>
        private void RunConvertColors(string json)
        {
            _running = true;
            PostBusy(true, L("opt.busy.optimizing"));
            try
            {
                bool toCmyk;
                bool selectionOnly;
                using (JsonDocument doc0 = JsonDocument.Parse(json))
                {
                    JsonElement root = doc0.RootElement;
                    toCmyk = string.Equals(Str(root, "target"), "Cmyk", StringComparison.OrdinalIgnoreCase);
                    selectionOnly = Bool(root, "selectionOnly", false);
                }

                dynamic activeDoc;
                try { activeDoc = _app.ActiveDocument; }
                catch { throw new OptimizeException("Abra um documento no CorelDRAW antes de converter as cores."); }
                if (activeDoc == null) throw new OptimizeException("Abra um documento no CorelDRAW antes de converter as cores.");

                ColorConvertResult result = new ColorModeConverter().Convert(
                    (object)_app, (object)activeDoc, toCmyk, selectionOnly, s => PostLog("• " + s, "info"));

OptimusLog.Write($"ConvertColors: alvo={(toCmyk ? "CMYK" : "RGB")} " +
                    $"formas={result.ShapesInspected} preench={result.FillsChanged} " +
                    $"contornos={result.OutlinesChanged} jaNoModo={result.AlreadyInMode} " +
                    $"naoConvertivel={result.NotConvertible} lerFalhou={result.ReadFailed} " +
                    $"converterFalhou={result.ConvertFailed} somenteSelecao={selectionOnly} " +
                    $"censoInstancias=[{result.CensusText()}] rotaAlternativa={result.UsedFallbackWalk} " +
                    (result.FirstError.Length > 0 ? " primeiroErro=" + result.FirstError : ""));

                Post(new
                {
                    type = "colorsConverted",
                    ok = true,
                    target = toCmyk ? "Cmyk" : "Rgb",
                    shapesInspected = result.ShapesInspected,
                    fillsChanged = result.FillsChanged,
                    outlinesChanged = result.OutlinesChanged,
                    alreadyInMode = result.AlreadyInMode,
                    notConvertible = result.NotConvertible,
                    readFailed = result.ReadFailed,
                    convertFailed = result.ConvertFailed,
                    // O censo e a resposta para "a auditoria diz que tem RGB e CMYK, mas converteu
                    // zero": ele conta as cores presas nas FORMAS, enquanto a auditoria rapida conta
                    // as da paleta do documento, que sobrevivem ao objeto que as usava (O19).
                    census = result.CensusText(),
                    rgbFound = result.CountOf(Optimus.Core.Corel.CorelConstants.CdrColorRgb),
                    cmykFound = result.CountOf(Optimus.Core.Corel.CorelConstants.CdrColorCmyk),
                    error = result.FirstError,
                });

                PostLog($"✔ {result.TotalChanged} cor(es) convertida(s) para {(toCmyk ? "CMYK" : "RGB")}" +
                    (result.NotConvertible > 0
                        ? $" ({result.NotConvertible} não convertível(is) — gradiente, padrão ou cor especial)."
                        : "."),
                    result.TotalChanged > 0 ? "ok" : "warn");

                // The document's colours are different now; the cached audit describes the file as it
                // was a moment ago and would show a mixed-mode warning that is no longer true.
                _lastColorAudit = null;
                _usageMeasured = false;
            }
            catch (Exception ex)
            {
                OptimusLog.Write("ConvertColors FAILED: " + ex);
                PostLog("✘ " + ex.Message, "err");
                Post(new { type = "colorsConverted", ok = false, error = ex.Message });
            }
            finally
            {
                _running = false;
                PostBusy(false, "");
            }
        }

        private void RunColorReplace(string json)
        {
            ColorReplaceRequest request;
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                request = new ColorReplaceRequest
                {
                    SourceKey = Str(root, "sourceKey"),
                    SelectionOnly = Bool(root, "selectionOnly", false),
                    // Set by the palette route: choosing a fill colour must not repaint contours.
                    FillOnly = Bool(root, "fillOnly", false),
                };

                string targetModel = Str(root, "targetModel");
                if (string.Equals(targetModel, "Cmyk", StringComparison.OrdinalIgnoreCase))
                {
                    request.TargetModel = ColorModel.Cmyk;
                    request.TargetCmyk = IntArray(root, "targetCmyk", 4);
                }
                else
                {
                    request.TargetModel = ColorModel.Rgb;
                    request.TargetRgb = IntArray(root, "targetRgb", 3);
                }
            }

            ExecuteColorReplace(request);
        }

        /// <summary>
        /// Runs one colour replacement against the live document. Both routes into it — the manual
        /// "trocar cor" panel and applying a palette colour — end HERE, so there is exactly one place
        /// that decides scope, reports a count and invalidates the cached audit. A second engine for
        /// the palette would be a second set of bugs to find twice.
        /// </summary>
        private void ExecuteColorReplace(ColorReplaceRequest request)
        {
            _running = true;
            PostBusy(true, L("opt.busy.optimizing"));
            try
            {
                dynamic activeDoc;
                try { activeDoc = _app.ActiveDocument; }
                catch { throw new OptimizeException("Abra um documento no CorelDRAW antes de substituir uma cor."); }
                if (activeDoc == null) throw new OptimizeException("Abra um documento no CorelDRAW antes de substituir uma cor.");

                ColorReplaceResult result = new ColorReplacer().Replace(
                    (object)_app, (object)activeDoc, request, s => PostLog("• " + s, "info"));

                // A troca de cor nao registrava NADA no log. Foi por isso que ela pode falhar em
                // silencio: o unico rastro era a mensagem de entrada, sem resposta nenhuma depois.
                OptimusLog.Write($"ColorReplace: chave={request.SourceKey} formas={result.ShapesInspected} " +
                    $"preench={result.FillsChanged} contornos={result.OutlinesChanged} " +
                    $"naoSubstituivel={result.NotReplaceable} gruposPulados={result.GroupsSkipped} " +
                    $"rotaAlternativa={result.UsedFallbackWalk} somenteSelecao={request.SelectionOnly} " +
                    $"soPreenchimento={request.FillOnly} selecaoVazia={result.SelectionEmpty}" +
                    (result.FirstError.Length > 0 ? " primeiroErro=" + result.FirstError : ""));

                // Percorreu formas e não casou NADA: essa combinação é sempre ou "a cor não está
                // aqui" ou "as duas pontas escrevem a chave de um jeito diferente", e sem ver as
                // chaves do arquivo não há como saber qual. Foi essa linha que faltou enquanto a
                // troca de CMYK falhava calada.
                if (result.TotalChanged == 0 && result.ShapesInspected > 0)
                    OptimusLog.Write($"ColorReplace: NADA CASOU. procurada=\"{request.SourceKey}\" " +
                        $"presentes=[{result.KeysSeenText()}]" +
                        (ColorIdentity.LastError.Length > 0 ? " leitura=" + ColorIdentity.LastError : ""));

                Post(new
                {
                    type = "colorReplaced",
                    ok = true,
                    shapesInspected = result.ShapesInspected,
                    fillsChanged = result.FillsChanged,
                    outlinesChanged = result.OutlinesChanged,
                    notReplaceable = result.NotReplaceable,
                    // "Nothing was selected" and "that colour is not here" produce identical zeros.
                    // The screen cannot tell them apart without being told which one happened.
                    selectionEmpty = result.SelectionEmpty,
                    fillOnly = request.FillOnly,
                });
                PostLog($"✔ {result.TotalChanged} preenchimento(s)/contorno(s) trocado(s)" +
                    (result.NotReplaceable > 0 ? $" ({result.NotReplaceable} não substituível(is) — gradiente/padrão)." : "."),
                    result.TotalChanged > 0 ? "ok" : "warn");

                // The colour just replaced no longer matches the old key in the live document — the
                // cached audit would keep showing stale usage counts, so it is invalidated rather than
                // left to mislead the next screen.
                _lastColorAudit = null;
                Post(new { type = "colorTable", ok = false, staleAfterReplace = true });
            }
            catch (OptimizeException ex)
            {
                PostLog(ex.Message, "warn");
                Post(new { type = "colorReplaced", ok = false });
            }
            catch (Exception ex)
            {
                OptimusLog.Write("RunColorReplace FAILED: " + ex);
                PostLog("✘ Falha ao substituir cor: " + ex.Message, "err");
                Post(new { type = "colorReplaced", ok = false });
            }
            finally
            {
                _running = false;
                PostBusy(false);
                PostStatus();
            }
        }

        /// <summary>
        /// Applies a colour taken from a registered palette. The palette supplies the DESTINATION
        /// only — which colour is being replaced, and whether the scope is the selection or the whole
        /// page, keep working exactly as they already did on this screen (§9.2).
        /// </summary>
        private void RunPaletteApply(string json)
        {
            string paletteName, colorKey;
            var request = new ColorReplaceRequest { FillOnly = true };

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                paletteName = Str(root, "palette");
                colorKey = Str(root, "key");
                request.SourceKey = Str(root, "sourceKey");
                request.SelectionOnly = Bool(root, "selectionOnly", false);
            }

            ColorPalette? palette = _palettes.Find(paletteName);
            PaletteColor? color = palette?.Colors.Find(c => c.Key == colorKey);
            if (color == null)
            {
                PostPaletteError(PaletteChange.NotFound, colorKey);
                return;
            }

            if (!DescribeTarget(color, request))
            {
                // A spot ink with no readable value has nothing to write. Saying so beats writing
                // black, which is what "just use the hex" would have produced.
                Post(new { type = "colorReplaced", ok = false, unusableTarget = true });
                return;
            }

            ExecuteColorReplace(request);
        }

        /// <summary>Turns a registered palette colour into the target of a replacement. False when the
        /// entry carries no value this can write (a spot name with no hex behind it).</summary>
        private static bool DescribeTarget(PaletteColor color, ColorReplaceRequest request)
        {
            if (color.Model == ColorModel.Cmyk && color.Components.Length > 0)
            {
                var parts = new List<int>();
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(color.Components, "[0-9]+"))
                    parts.Add(int.Parse(m.Value));

                if (parts.Count >= 4)
                {
                    request.TargetModel = ColorModel.Cmyk;
                    request.TargetCmyk = new[] { parts[0], parts[1], parts[2], parts[3] };
                    return true;
                }
            }

            string hex = color.Hex.TrimStart('#');
            if (hex.Length != 6) return false;

            request.TargetModel = ColorModel.Rgb;
            request.TargetRgb = new[]
            {
                Convert.ToInt32(hex.Substring(0, 2), 16),
                Convert.ToInt32(hex.Substring(2, 2), 16),
                Convert.ToInt32(hex.Substring(4, 2), 16),
            };
            return true;
        }

        // ── importar paleta de um SVG ────────────────────────────────────────────────────

        /// <summary>The colours read from the last SVG the operator picked. Held here, not in the
        /// page: the page asks for a NAME, and the colours it would echo back are a second copy that
        /// could drift from what was actually read.</summary>
        private List<string> _svgColors = new List<string>();
        private string _svgFile = "";

        private void SvgPick()
        {
            try
            {
                // WinForms' OpenFileDialog already IS the modern shell dialog on .NET Framework
                // (unlike FolderBrowserDialog, which is why FolderPicker had to exist) — so this
                // needs no interop of its own.
                using var dlg = new System.Windows.Forms.OpenFileDialog
                {
                    Title = L("opt.pal.svg.pick"),
                    Filter = L("opt.pal.svg.filter") + "|*.svg;*.svgz|*.*|*.*",
                    Multiselect = false,
                    CheckFileExists = true,
                };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                string text = File.ReadAllText(dlg.FileName);
                SvgColorScan scan = SvgColorExtractor.Scan(text);

                _svgColors = scan.Hex;
                _svgFile = Path.GetFileName(dlg.FileName);

                OptimusLog.Write($"SVG: \"{_svgFile}\" lido={scan.Parsed} " +
                    $"mencoes={scan.Mentions} cores={scan.Hex.Count} estado={scan.Status}");

                Post(new
                {
                    type = "svgColors",
                    ok = scan.Status == PaletteChange.Ok,
                    code = scan.Status.ToString(),
                    file = _svgFile,
                    mentions = scan.Mentions,
                    // Sent for the preview grid only; the import itself reads the list above, so the
                    // page never becomes a second source of truth for what the file contained.
                    colors = scan.Hex,
                });
            }
            catch (Exception ex)
            {
                OptimusLog.Write("SvgPick FAILED: " + ex.Message);
                Post(new { type = "svgColors", ok = false, code = PaletteChange.UnreadableFile.ToString(), file = "" });
            }
        }

        private void SvgImport(string json)
        {
            string name = ReadStr(json, "name");
            if (name.Length == 0) { PostPaletteError(PaletteChange.InvalidName, ""); return; }
            if (_svgColors.Count == 0) { PostPaletteError(PaletteChange.NoColors, _svgFile); return; }

            var colors = new List<PaletteColor>();
            foreach (string hex in _svgColors)
            {
                // No name is invented from the value (§15). "vermelho" for #E30613 would be a guess
                // presented as a fact, and the operator is the one who knows what that colour IS.
                colors.Add(new PaletteColor { Name = "", Model = ColorModel.Rgb, Hex = hex });
            }

            ColorPalette? palette = _palettes.ImportPalette(name, colors, out PaletteChange status);
            if (palette == null) { PostPaletteError(status, name); return; }

            OptimusLog.Write($"SVG importado: \"{name}\" com {palette.Colors.Count} cor(es) de \"{_svgFile}\".");
            _svgColors = new List<string>();
            _svgFile = "";

            Post(new { type = "svgImported", ok = true, name, count = palette.Colors.Count });
            PostPalettes();
            PostColorTable();
        }

        // ── font manager (F10) ───────────────────────────────────────────────────────────

        /// <summary>
        /// Every registered font, checked against this machine's installed fonts and, when installed,
        /// its pt-BR accent coverage — reusing the same <see cref="FontProbe"/> the document audit
        /// already relies on (F7.1). Registering a font does not require it to be installed; the
        /// verdict just says so honestly.
        /// </summary>
        private void PostFontManager()
        {
            var probe = new FontProbe();
            var rows = new List<object>();
            foreach (RegisteredFont font in _fontRegistry.Fonts)
            {
                GlyphCoverage coverage = probe.Check(font.Name);
                rows.Add(new
                {
                    name = font.Name,
                    note = font.Note,
                    installed = probe.IsInstalled(font.Name),
                    verdict = coverage.Verdict.ToString(),
                    summary = coverage.Summary(),
                    usable = coverage.IsUsable,
                    substitutedBy = coverage.SubstitutedBy,
                });
            }
            Post(new { type = "fontManager", ok = true, fonts = rows });
        }

        private void FontAdd(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            string name = Str(root, "name");
            if (name.Length == 0) return;
            _fontRegistry.Add(name, Str(root, "note"));
            PostFontManager();
        }

        private void FontRemove(string json)
        {
            _fontRegistry.Remove(ReadStr(json, "name"));
            PostFontManager();
        }

        /// <summary>Pastes a whole list of names in one shot — the "semi-automatic" cadastro.</summary>
        private void FontImport(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            int added = _fontRegistry.ImportNames(Str(doc.RootElement, "text"));
            PostLog($"✔ {added} fonte(s) nova(s) cadastrada(s).", "ok");
            PostFontManager();
        }

        // ── pasta de fontes (modelo do Corel Font Manager) ───────────────────────────────
        // O Corel Font Manager usa fontes de uma PASTA sem instalar nada no Windows. Por isso
        // apontar a pasta e o gesto certo aqui — e nao colar uma lista de nomes, que nao diz
        // nada sobre onde a fonte esta nem se ela escreve portugues.

        /// <summary>Opens a folder picker on the STA thread and remembers the choice.</summary>
        /// <summary>
        /// Sends the actual bytes of a font to the page so it can draw a specimen.
        ///
        /// <para>
        /// "Acentos do português OK" asks the operator to take our word for it. Drawing "ção" in the
        /// real typeface lets them see it — and when a glyph is missing they see the notdef box,
        /// which is exactly what CorelDRAW will put on the print. The cmap check stays: it is what
        /// scales to a whole document. The specimen is what makes the verdict believable.
        /// </para>
        /// <para>
        /// The bytes go as base64 in one message. That is why the locator refuses anything large:
        /// base64 inflates by a third, and no specimen is worth freezing the docker.
        /// </para>
        /// </summary>
        private void PostFontPreview(string json)
        {
            string family = ReadStr(json, "family") ?? "";
            try
            {
                // The shop's own font folder is searched first: a Corel Font Manager font is never
                // installed in Windows, so the registry would not know it exists.
                var locator = new FontFileLocator(new[] { FontFolderStore.Read() });
                FontFileBytes file = locator.Load(family);

                if (!file.Ok)
                {
                    Post(new { type = "fontPreview", ok = false, family, error = file.Error });
                    return;
                }

                Post(new
                {
                    type = "fontPreview",
                    ok = true,
                    family,
                    mime = file.Mime,
                    data = Convert.ToBase64String(file.Data),
                });
            }
            catch (Exception ex)
            {
                OptimusLog.Write("FontPreview FAILED (" + family + "): " + ex);
                Post(new { type = "fontPreview", ok = false, family, error = ex.Message });
            }
        }

        private void FontFolderPick()
        {
            try
            {
                string current = FontFolderStore.Read();

                // The modern picker: breadcrumb path, search, favourites, and it accepts a pasted
                // path — which is how anyone reaches a folder on the shop's network share.
                string? chosen = FolderPicker.Pick(L("opt.pf.fonts.pickdesc"), current);

                // The old SHBrowseForFolder stays as a fallback. On a machine where the modern
                // dialog cannot be created, an ugly dialog beats no dialog: picking a folder is the
                // only way to configure this at all.
                if (chosen == null)
                {
                    using var dlg = new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description = L("opt.pf.fonts.pickdesc"),
                        ShowNewFolderButton = false,
                    };
                    if (current.Length > 0 && System.IO.Directory.Exists(current)) dlg.SelectedPath = current;
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    chosen = dlg.SelectedPath;
                    OptimusLog.Write("Fontes: seletor moderno indisponível, usei o antigo.");
                }

                FontFolderStore.Write(chosen);
                OptimusLog.Write("Fontes: pasta escolhida: " + chosen
                    + "  (seletor moderno: " + FolderPicker.IsAvailable() + ")");
            }
            catch (Exception ex) { OptimusLog.Write("FontFolderPick FAILED: " + ex.Message); }
            PostFontFolder();
        }

        /// <summary>Scans the watched folder and reports each font's pt-BR verdict.</summary>
        private void PostFontFolder()
        {
            string folder = FontFolderStore.Read();
            if (folder.Length == 0)
            {
                Post(new { type = "fontFolder", ok = true, folder = "", fonts = new object[0], filesSeen = 0 });
                return;
            }

            FontFolderResult r = new FontFolderProbe().Scan(folder);
            OptimusLog.Write($"Fontes: pasta {folder} - {r.Fonts.Count} fonte(s) de {r.FilesSeen} arquivo(s). {r.Error}");

            Post(new
            {
                type = "fontFolder",
                ok = r.Ok,
                folder = r.Folder,
                filesSeen = r.FilesSeen,
                error = r.Error,
                fonts = r.Fonts.ConvertAll(ff => new
                {
                    name = ff.Family,
                    file = ff.File,
                    alsoInstalled = ff.AlsoInstalled,
                    verdict = ff.Coverage.Verdict.ToString(),
                    summary = ff.Coverage.Summary(),
                    usable = ff.Coverage.IsUsable,
                }),
            });
        }

        // ── voice command (F11) ──────────────────────────────────────────────────────────
        // Fully offline: whisper.cpp runs locally (LocalVoiceTranscriber), the microphone is
        // captured locally (VoiceRecorder). No API key, no network call, ever. An earlier design
        // proxied through OpenAI's cloud Whisper API — Davi rejected it outright the moment he saw
        // an API-key field (2026-08-14: "eu quero que funcione offline... não é para usar API").

        /// <summary>
        /// Último estado de voz já escrito no log. Começa nulo para que a primeira resposta —
        /// disponível ou não — seja sempre registrada.
        /// </summary>
        private bool? _voiceLoggedState;

        /// <summary>
        /// Comandos que a página dispara sozinha para se manter em dia. São baratos, frequentes e
        /// nunca são o que se procura quando se abre o log — então não vão para ele.
        /// </summary>
        /// <remarks>
        /// "estimar" NÃO entra aqui de propósito: ele é disparado pelo operador e o payload registra
        /// exatamente quais ajustes estavam valendo — é a linha que explica por que uma otimização
        /// deu no que deu. Silenciar por volume tem que parar onde começa a informação.
        /// </remarks>
        private static bool IsChatty(string cmd) =>
            cmd == "status" || cmd == "voiceStatus" || cmd == "colorFromSelection";

        private void PostVoiceStatus()
        {
            bool available = _voiceTranscriber.ModelAvailable;

            // Escrito quando o RESULTADO MUDA, não a cada pergunta. O caminho do modelo é fixo
            // durante a sessão, então repeti-lo é ruído puro: uma sessão de vinte minutos gerou
            // centenas de linhas idênticas e enterrou a única linha de diagnóstico que importava.
            // A ausência continua sendo registrada com o caminho conferido — foi o que faltou no
            // único relato de "modelo de voz não encontrado" e é o motivo desta linha existir.
            if (_voiceLoggedState != available)
            {
                _voiceLoggedState = available;
                OptimusLog.Write($"Voz: modelo {(available ? "encontrado" : "AUSENTE")} em \"{_voiceTranscriber.ModelPath}\"");
            }
            Post(new { type = "voiceStatus", modelAvailable = available });
        }

        private void VoiceStart()
        {
            try
            {
                int deviceIndex = VoiceRecorder.ResolveDeviceIndex(VoiceDeviceStore.Read());
                _voiceRecorder.Start(deviceIndex);
                Post(new { type = "voiceRecording", ok = true, recording = true });
                _voicePlayer.Play("ready");
            }
            catch (Exception ex)
            {
                OptimusLog.Write("VoiceStart FAILED: " + ex.Message);
                Post(new { type = "voiceResult", ok = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Lists every recording device Windows currently sees, plus which one is saved as the
        /// operator's choice — so the settings screen can render a picker instead of leaving the
        /// microphone stuck on whatever Windows calls "default" (measured: a shop with a USB headset
        /// AND a built-in laptop mic has no other way to pick between them).
        /// </summary>
        private void PostVoiceDevices()
        {
            string saved = VoiceDeviceStore.Read();
            Post(new
            {
                type = "voiceDevices",
                devices = VoiceRecorder.ListDevices().ConvertAll(d => new { index = d.Index, name = d.Name }),
                selected = saved,
            });
        }

        private void VoiceDeviceSet(string json)
        {
            string name = ReadStr(json, "name");
            VoiceDeviceStore.Write(name);
            OptimusLog.Write("Voz: microfone escolhido = \"" + (name.Length > 0 ? name : "(padrão do sistema)") + "\"");
            PostVoiceDevices();
        }

        /// <summary>Maps a matched command to the fixed WAV key that confirms it — see
        /// <c>assets/voice/sounds/pt-BR/</c> and <see cref="VoicePlayer"/>.</summary>
        private static string VoiceSoundKey(VoiceIntent intent)
        {
            switch (intent)
            {
                case VoiceIntent.Analyze: return "analyze";
                case VoiceIntent.Audit: return "audit";
                case VoiceIntent.Optimize: return "optimize";
                case VoiceIntent.SetLanguagePt: return "lang_pt";
                case VoiceIntent.SetLanguageEs: return "lang_es";
                case VoiceIntent.SetLanguageEn: return "lang_en";
                default: return "unknown";
            }
        }

        /// <summary>
        /// Stops the recording, transcribes it LOCALLY, matches it to a command, and runs the SAME
        /// method the corresponding button already runs — voice is a second way to trigger existing
        /// actions, not a parallel implementation of them.
        ///
        /// <para>
        /// <c>async void</c> deliberately: this is a WebView2 event handler, and blocking the STA
        /// thread while whisper.cpp runs (a second or two on CPU for a short command, all local — no
        /// network round trip to wait on) would freeze CorelDRAW's whole window during a live demo.
        /// Every path out is wrapped in try/catch so nothing escapes to the unhandled-exception
        /// handler.
        /// </para>
        /// </summary>
        private async void RunVoiceStop()
        {
            _running = true;
            PostBusy(true, L("opt.busy.optimizing"));
            Post(new { type = "voiceResult", stage = "transcribing" });
            try
            {
                byte[] wav = await _voiceRecorder.StopAsync().ConfigureAwait(true);
                if (wav.Length == 0)
                {
                    Post(new { type = "voiceResult", ok = false, error = "Nenhum áudio capturado." });
                    return;
                }

                string whisperLang = _i18n.Current == Language.En ? "en" : _i18n.Current == Language.Es ? "es" : "pt";

                // Task.Run, deliberately: loading whisper.cpp's native model (~57 MB, first use only)
                // and running inference are CPU-heavy native calls. Awaiting TranscribeAsync directly
                // runs that synchronous prefix on THIS thread — CorelDRAW's own STA/UI thread, the
                // same one WebView2's COM objects live on. Isolating it onto a threadpool thread keeps
                // that thread free for the whole duration, which matters here: an unrelated WebView2
                // finalizer crash (O17) was observed on this machine, and a blocked STA thread during
                // heavy native work is exactly the kind of condition that makes COM/GC interaction
                // bugs more likely to surface, not less.
                TranscriptionResult result = await System.Threading.Tasks.Task
                    .Run(() => _voiceTranscriber.TranscribeAsync(wav, whisperLang))
                    .ConfigureAwait(true);

                if (!result.Ok)
                {
                    OptimusLog.Write("Voz: transcrição FALHOU: " + result.Error);
                    PostLog("✘ Comando de voz: " + result.Error, "err");
                    Post(new { type = "voiceResult", ok = false, error = result.Error });
                    return;
                }

                VoiceIntent intent = VoiceCommandMatcher.Match(result.Text);
                OptimusLog.Write($"Voz: \"{result.Text}\" -> {intent}");
                Post(new { type = "voiceResult", ok = true, transcript = result.Text, intent = intent.ToString() });
                _voicePlayer.Play(VoiceSoundKey(intent));

                // Each of these methods manages its OWN busy/_running lifecycle around the actual
                // work; this method's `finally` below still runs after them (a plain `return` inside
                // a `try` does not skip `finally`), so the worst case is a harmless duplicate
                // busy(false)/status post — never a stuck busy state.
                switch (intent)
                {
                    case VoiceIntent.Analyze: RunInspect(new OptimizeOptions()); break;
                    case VoiceIntent.Audit: RunAudit(); break;
                    case VoiceIntent.Optimize: RunOptimize(new OptimizeOptions()); break;
                    case VoiceIntent.SetLanguagePt: SetLanguage("pt"); break;
                    case VoiceIntent.SetLanguageEs: SetLanguage("es"); break;
                    case VoiceIntent.SetLanguageEn: SetLanguage("en"); break;
                    default:
                        PostLog("? Comando de voz não reconhecido: \"" + result.Text + "\"", "warn");
                        break;
                }
            }
            catch (Exception ex)
            {
                OptimusLog.Write("RunVoiceStop FAILED: " + ex.Message);
                Post(new { type = "voiceResult", ok = false, error = ex.Message });
            }
            finally
            {
                _running = false;
                PostBusy(false);
                PostStatus();
            }
        }

        /// <summary>Applies the operator's chosen settings, behind a verified backup.</summary>
        private void RunApply(string messageJson)
        {
            OptimizationSettings settings = ParseSettings(messageJson);
            _running = true;
            PostBusy(true, L("opt.busy.optimizing"));
            PostLog("▶ Otimizando com as suas configurações…", "run");
            try
            {
                OptimizeResult r = new FileOptimizer(_app, new OptimizeOptions())
                    .Apply(settings, s => PostLog("• " + s, "info"));

                PostApplyResult(r);
                PostLog(r.FileGrew
                    ? "⚠ O arquivo ficou MAIOR. A cópia de segurança está preservada."
                    : $"✔ {r.FileReductionPct:0.#}% menor (estimado {r.EstimatedPercent:0.#}%).",
                    r.FileGrew ? "warn" : "ok");
            }
            catch (BackupFailedException ex)
            {
                // The safety net could not be established, so nothing was touched.
                OptimusLog.Write("Apply aborted (backup): " + ex.Message);
                PostLog("✘ " + ex.Message, "err");
                Post(new { type = "applied", ok = false });
            }
            catch (OptimizeException ex)
            {
                PostLog(ex.Message, "warn");
                Post(new { type = "applied", ok = false });
            }
            catch (Exception ex)
            {
                OptimusLog.Write("RunApply FAILED: " + ex);
                PostLog("✘ Falha ao otimizar: " + ex.Message, "err");
                Post(new { type = "applied", ok = false });
            }
            finally
            {
                _running = false;
                PostBusy(false);
                PostStatus();
            }
        }

        private void PostApplyResult(OptimizeResult r)
        {
            OptimusLog.Write($"Apply: before={r.FileSizeBefore} after={r.FileSizeAfter} " +
                $"saved={r.FileBytesSaved} ({r.FileReductionPct:0.#}%) estimated={r.EstimatedPercent:0.#}% " +
                $"grew={r.FileGrew} nodes {r.NodesBefore}->{r.NodesAfter} backup=\"{r.BackupPath}\"");
            OptimusLog.Write($"Apply: BITMAPS found={r.BitmapsFound} aboveTarget={r.BitmapsAboveTarget} " +
                $"resampled={r.BitmapsResampled} unresampleable(OLE/EPS)={r.UnresampleableRaster}");
            OptimusLog.Write($"Apply: FLUIDITY measured={r.RedrawMeasured} " +
                $"redraw {r.RedrawBeforeMs:0.#}ms -> {r.RedrawAfterMs:0.#}ms ({r.RedrawImprovedPct:0.#}%) " +
                $"effects={r.LiveEffects} flattened={r.EffectsFlattened} transparencies={r.Transparencies} " +
                $"effectCost={r.EffectCostScore}");

            Post(new
            {
                type = "applied",
                ok = true,
                grew = r.FileGrew,
                beforeBytes = r.FileSizeBefore,
                afterBytes = r.FileSizeAfter,
                savedBytes = r.FileBytesSaved,
                savedPercent = Math.Round(r.FileReductionPct, 1),
                estimatedPercent = Math.Round(r.EstimatedPercent, 1),
                nodesBefore = r.NodesBefore,
                nodesAfter = r.NodesAfter,
                nodePercent = Math.Round(r.NodeReductionPct, 1),

                // Fluidity, MEASURED with a clock. redrawMeasured=false means we make no claim.
                redrawMeasured = r.RedrawMeasured,
                redrawBeforeMs = Math.Round(r.RedrawBeforeMs, 1),
                redrawAfterMs = Math.Round(r.RedrawAfterMs, 1),
                redrawImprovedPct = Math.Round(r.RedrawImprovedPct, 1),
                effectsFlattened = r.EffectsFlattened,
                liveEffects = r.LiveEffects,

                bitmapsFound = r.BitmapsFound,
                bitmapsAboveTarget = r.BitmapsAboveTarget,
                bitmapsResampled = r.BitmapsResampled,
                unresampleableRaster = r.UnresampleableRaster,

                emptyLayers = r.EmptyLayersDeleted,
                offPage = r.OffPageDeleted,
                reopened = r.Reopened,
                backup = System.IO.Path.GetFileName(r.BackupPath),
                components = r.Delta == null ? null : System.Linq.Enumerable.ToList(
                    System.Linq.Enumerable.Select(r.Delta.Components, c => new
                    {
                        component = c.Component.ToString(),
                        before = c.BeforeBytes,
                        after = c.AfterBytes,
                        saved = c.SavedBytes,
                    })),
            });
        }

        /// <summary>Runs the optimization synchronously on the CoreWebView2 thread (= CorelDRAW's
        /// STA UI thread, so COM is safe). It blocks the window for the few seconds it takes; the
        /// UI shows a busy overlay first.</summary>
        private void RunOptimize(OptimizeOptions opt)
        {
            _running = true;
            PostBusy(true, L("opt.busy.optimizing"));
            PostLog("▶ Otimizando o arquivo… (pode levar alguns segundos)", "run");
            try
            {
                var optimizer = new FileOptimizer(_app, opt);
                OptimizeResult r = optimizer.Run(s => PostLog("• " + s, "info"));
                PostResult(r);
                PostLog("✔ Otimização concluída.", "ok");
            }
            catch (OptimizeException ex)
            {
                // Expected precondition (no doc / unsaved) — friendly, not an error dump.
                PostLog(ex.Message, "warn");
                Post(new { type = "result", ok = false });
            }
            catch (Exception ex)
            {
                OptimusLog.Write("RunOptimize FAILED: " + ex);
                PostLog("✘ Falha ao otimizar: " + ex.Message, "err");
                Post(new { type = "result", ok = false });
            }
            finally
            {
                _running = false;
                PostBusy(false);
                PostStatus();
            }
        }

        private void PostResult(OptimizeResult r)
        {
            Post(new
            {
                type = "result",
                ok = true,
                doc = r.DocName,
                shapes = r.ShapesVisited,
                inPowerClip = r.ShapesInPowerClip,
                raster = r.RasterShapes,
                effects = r.LiveEffects,
                curves = r.CurvesReduced,
                nodesBefore = r.NodesBefore,
                nodesAfter = r.NodesAfter,
                nodesRemoved = r.NodesRemoved,
                nodePct = Math.Round(r.NodeReductionPct, 1),
                emptyLayers = r.EmptyLayersDeleted,
                offPage = r.OffPageDeleted,
                reopened = r.Reopened,
                sizeBefore = r.FileSizeBefore,
                sizeAfter = r.FileSizeAfter,
                bytesSaved = r.FileBytesSaved,
                sizePct = Math.Round(r.FileReductionPct, 1),
            });
        }

        // ---- JS bridge ---------------------------------------------------------------------

        private void PostLog(string text, string level) => Post(new { type = "log", text, level });

        /// <summary>
        /// Drives the UI's busy state. <paramref name="label"/> names the operation in progress —
        /// analysing is READ-ONLY and must never be announced as "Otimizando…", which would tell
        /// the operator their file is being modified when it is not.
        /// </summary>
        private void PostBusy(bool on, string? label = null) =>
            Post(new { type = "busy", on, label = label ?? L("opt.busy.optimizing") });

        public void PostStatus()
        {
            string doc = Safe(() => (string)_app.ActiveDocument.Name, L("opt.status.nodoc"));
            int sel = Safe(() => (int)_app.ActiveSelection.Shapes.Count, 0);
            bool saved = Safe(() => !string.IsNullOrWhiteSpace((string)_app.ActiveDocument.FullFileName), false);
            Post(new { type = "status", doc, sel, saved, build = Build.Tag });
        }

        private void Post(object message)
        {
            // C#→JS via ExecuteScriptAsync(window.optimusReceive(...)) instead of
            // PostWebMessageAsJson: inside a CorelDRAW addon docker (foreign WPF host) the
            // 'message' channel is unreliable (foreign Dispatcher + listener-registration race —
            // JS→C# works, C#→JS silently drops). Calling a global JS function bypasses it.
            //
            // Serialization used to sit OUTSIDE any try/catch here. Measured on a real client
            // machine (2026-09): a sibling AiSten addin sharing the same CorelDRAW process had
            // loaded a different version of System.Text.Json into the AppDomain, and
            // JsonSerializer.Serialize threw a TypeLoadException that escaped this method
            // entirely — unwinding straight through whichever command called Post(), which meant
            // that command's own `finally { _running = false; }` never got a chance to run for
            // every command still ahead of it in that call chain. The docker was left "busy"
            // forever; every command after that read as DROPPED (busy) until CorelDRAW was
            // restarted. A message that cannot be serialized must cost this ONE post, never the
            // whole session.
            string json;
            try { json = JsonSerializer.Serialize(message); }
            catch (Exception ex) { OptimusLog.Write("Post: falha ao serializar mensagem: " + ex); return; }

            string script = "if(window.optimusReceive){window.optimusReceive(" + json + ");}";
            _runOnUi(() =>
            {
                try { _ = _core.ExecuteScriptAsync(script); }
                catch (Exception ex) { OptimusLog.Write("Post FAILED (" + json + "): " + ex.Message); }
            });
        }

        // ---- json helpers ------------------------------------------------------------------

        private static string Str(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String ? (e.GetString() ?? "") : "";

        private static double Num(JsonElement root, string name, double fallback)
        {
            if (!root.TryGetProperty(name, out JsonElement e)) return fallback;
            string? s = e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString();
            return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        private static bool Bool(JsonElement root, string name, bool fallback)
        {
            if (!root.TryGetProperty(name, out JsonElement e)) return fallback;
            if (e.ValueKind == JsonValueKind.True) return true;
            if (e.ValueKind == JsonValueKind.False) return false;
            if (e.ValueKind == JsonValueKind.String) return e.GetString() == "true";
            return fallback;
        }

        private static T Safe<T>(Func<T> get, T fallback) { try { return get(); } catch { return fallback; } }

        /// <summary>Reads one string field out of a raw message without the caller having to parse it.</summary>
        private static string ReadStr(string messageJson, string name)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(messageJson);
                return Str(doc.RootElement, name);
            }
            catch (Exception) { return ""; }
        }

        /// <summary>Reads a fixed-length integer array (e.g. <c>[r,g,b]</c>), zero-filled when absent.</summary>
        private static int[] IntArray(JsonElement root, string name, int length)
        {
            var values = new int[length];
            if (!root.TryGetProperty(name, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array) return values;
            int i = 0;
            foreach (JsonElement e in arr.EnumerateArray())
            {
                if (i >= length) break;
                values[i++] = e.TryGetInt32(out int v) ? v : 0;
            }
            return values;
        }

        // ---- UI asset plumbing -------------------------------------------------------------

        /// <summary>Folder this assembly was loaded from — holds WebView2Loader.dll + loose deps.
        /// Added to the native DLL search path before WebView2 init.</summary>
        public static string InstallDir()
        {
            try
            {
                string loc = typeof(OptimusBridge).Assembly.Location;
                if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc)!;
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Optimus");
        }

        /// <summary>Extracts the embedded UI (single self-contained index.html) to temp.</summary>
        public static string ExtractUi()
        {
            string dir = Path.Combine(Path.GetTempPath(), "Optimus_ui");
            Directory.CreateDirectory(dir);
            Assembly asm = typeof(OptimusBridge).Assembly;
            using (Stream s = asm.GetManifestResourceStream("ui.index.html")
                ?? throw new InvalidOperationException("Recurso de UI ausente: ui.index.html"))
            using (FileStream fs = File.Create(Path.Combine(dir, "index.html")))
                s.CopyTo(fs);
            return dir;
        }

        /// <summary>Writable per-user WebView2 user-data folder. CRITICAL: under Program Files
        /// (read-only) a default folder makes WebView2 init fail silently → blank docker.</summary>
        public static string UserDataDir()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Optimus", "WebView2");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
