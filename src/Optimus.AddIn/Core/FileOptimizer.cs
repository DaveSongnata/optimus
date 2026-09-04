using System;
using System.Collections.Generic;
using System.IO;
using Optimus.Core.Diagnostics;
using Optimus.Core.Model;
using Optimus.Core.Optimization;
using Optimus.Core.Performance;
using Optimus.Interop;
using Optimus.AddIn.Ui;

namespace Optimus.AddIn.Core
{
    /// <summary>
    /// Legacy knob set, kept for the inspection path. The OPTIMIZATION path now takes
    /// <see cref="OptimizationSettings"/> — the operator's four sliders — because the product applies
    /// the trade-off the operator chose rather than a fixed recipe.
    /// </summary>
    public sealed class OptimizeOptions
    {
        /// <summary>
        /// Max curve deviation for <c>NodeRange.AutoReduce</c>, in MILLIMETRES. The document is
        /// switched to mm by <see cref="CorelDocumentState"/> first, so this is a real physical
        /// tolerance. Presets: Leve 0.01, Médio 0.03, Forte 0.08.
        /// </summary>
        public double ToleranceMm = 0.03;

        /// <summary>Delete empty layers (safe: an empty layer holds nothing).</summary>
        public bool RemoveEmptyLayers = true;

        /// <summary>Delete objects fully outside the page (opt-in — defaults OFF).</summary>
        public bool RemoveOffPage = false;

        /// <summary>Close + reopen after saving so CorelDRAW reserializes with no retained undo
        /// state (the "optimized file got BIGGER" fix). Recommended ON.</summary>
        public bool CloseAndReopen = true;

        /// <summary>Operate on the current selection only, instead of the whole page.</summary>
        public bool SelectionOnly = false;

        /// <summary>
        /// Count nodes during analysis. Measured at ~2.9 ms per curve on a real 16.900-shape file
        /// (three COM calls each, one of which may materialize the whole node collection), so a
        /// structural-only scan is offered for large documents.
        /// </summary>
        public bool CountNodes = true;
    }

    /// <summary>Read-only inspection outcome, always naming the document it describes.</summary>
    public sealed class InspectResult
    {
        public string DocName = "";
        public string DocPath = "";
        public ShapeInventory Inventory = ShapeInventory.From(new ShapeRecord[0]);

        /// <summary>Per-category COM cost of the walk, for the log (diagnoses slowness).</summary>
        public string Stats = "";

        /// <summary>False when the node count was skipped for speed (reported as 0, not as fact).</summary>
        public bool NodesCounted = true;

        /// <summary>Shapes below the page's top level (inside groups or PowerClips). Reported as
        /// "nested" because the bulk reader counts but cannot say which of the two.</summary>
        public int Nested;

        /// <summary>Shapes no type filter matched. Must be 0; anything else means the reader is
        /// blind to part of the document and the breakdown cannot be trusted.</summary>
        public int Unclassified;

        /// <summary>Exact on-disk byte composition of the saved file (empty if never saved).</summary>
        public CdrComposition Composition = CdrComposition.Unavailable();

        /// <summary>The honest reduction ceiling for THIS file.</summary>
        public ReductionCeiling? Ceiling;

        /// <summary>Why the composition could not be read, when it could not.</summary>
        public string ContainerNote = "";

        /// <summary>Font names the file references (read from its own font table, no COM).</summary>
        public List<string> FontNames = new List<string>();

        /// <summary>Embedded ICC profile bytes — read exactly from the container entries.</summary>
        public long IccBytes;

        /// <summary>Paths of the embedded ICC profiles, so the report can name them.</summary>
        public List<string> IccProfileNames = new List<string>();

        /// <summary>True when the embedded profile is referenced by objects in that space, i.e.
        /// removing it is a colour-management trade-off rather than free cleanup.</summary>
        public bool IccUsed;

        /// <summary>
        /// The object payload broken down, including the REPETITION CENSUS.
        ///
        /// <para>
        /// This is what tells the operator that a drawing stores the same art many times over — measured
        /// at 16.914 objects for only 4.789 distinct shapes on a real file, worth 58,2% of it losslessly
        /// (O15). It is read from the saved file, so it needs no COM at all.
        /// </para>
        /// </summary>
        public PayloadBreakdown? Payload;

        /// <summary>
        /// Repetition as seen through COM — the check that decides whether the 58,2% the file census
        /// promises can actually be realised by instancing. Null when the probe did not run.
        /// </summary>
        public RepetitionReport? ComRepetition;

        /// <summary>
        /// Each shape's identifier paired with its geometry fingerprint, read from the file. This is what
        /// lets the exact disk-side grouping be applied to live shapes.
        /// </summary>
        public ShapeIdCensus? ShapeIds;

        /// <summary>Fluidity inputs and score (lower bound — transparencies land in Phase 4).</summary>
        public InteractionInputs Interaction = new InteractionInputs();
        public double InteractionScore;
        public string InteractionBand = "";
        public string InteractionDominant = "";
    }

    /// <summary>What happened, for the operator report.</summary>
    public sealed class OptimizeResult
    {
        public string DocName = "";
        public int ShapesVisited;
        public int ShapesInPowerClip;
        public int RasterShapes;
        public int LiveEffects;
        public int CurvesReduced;
        public long NodesBefore;
        public long NodesAfter;
        public int EmptyLayersDeleted;
        public int OffPageDeleted;
        public bool Reopened;
        public long FileSizeBefore;
        public long FileSizeAfter;

        public long NodesRemoved => Math.Max(0, NodesBefore - NodesAfter);
        public double NodeReductionPct => NodesBefore > 0 ? (NodesBefore - NodesAfter) * 100.0 / NodesBefore : 0;
        public long FileBytesSaved => Math.Max(0, FileSizeBefore - FileSizeAfter);
        public double FileReductionPct => FileSizeBefore > 0 ? (FileSizeBefore - FileSizeAfter) * 100.0 / FileSizeBefore : 0;

        /// <summary>Path of the verified backup taken before any mutation.</summary>
        public string BackupPath = "";

        /// <summary>True when the file came out BIGGER — reported, never hidden as a 0% saving.</summary>
        public bool FileGrew => FileSizeAfter > FileSizeBefore && FileSizeBefore > 0;

        /// <summary>Per-component before/after, measured on disk.</summary>
        public CompositionDelta? Delta;

        /// <summary>What the operator was promised before applying, for comparison with reality.</summary>
        public double EstimatedPercent;

        // ── fluidity, MEASURED (never claimed) ───────────────────────────────────────
        /// <summary>Median redraw time before, in ms. 0 when it could not be measured.</summary>
        public double RedrawBeforeMs;

        /// <summary>Median redraw time after, in ms. 0 when it could not be measured.</summary>
        public double RedrawAfterMs;

        /// <summary>Positive = faster. Negative = SLOWER, which is reported as such.</summary>
        public double RedrawImprovedPct =>
            RedrawBeforeMs > 0 && RedrawAfterMs > 0
                ? (RedrawBeforeMs - RedrawAfterMs) * 100.0 / RedrawBeforeMs
                : 0;

        /// <summary>True when both timings exist, i.e. the fluidity claim is backed by a clock.</summary>
        public bool RedrawMeasured => RedrawBeforeMs > 0 && RedrawAfterMs > 0;

        public int Transparencies;
        public int EffectsFlattened;

        // ── bitmaps ──────────────────────────────────────────────────────────────────
        public int BitmapsFound;

        /// <summary>Images carrying more resolution than the chosen target needs.</summary>
        public int BitmapsAboveTarget;

        public int BitmapsResampled;

        /// <summary>OLE/EPS shapes with raster inside — reported, never modified.</summary>
        public int UnresampleableRaster;

        /// <summary>Ordinal cost of the live effects found (ranking, not milliseconds).</summary>
        public int EffectCostScore;

        // ── repeated art turned into symbols (O15) ───────────────────────────────────
        /// <summary>Symbol definitions created from repeated art.</summary>
        public int SymbolsCreated;

        /// <summary>Copies replaced by an instance — the lossless saving.</summary>
        public int ShapesInstanced;
    }

    /// <summary>
    /// Optimizes the active CorelDRAW document. All CorelDRAW access is late-bound
    /// (<c>dynamic</c>) so one net48 assembly drives 2024/25/26.
    ///
    /// <para>
    /// Phase 1 rebuilt this on top of <see cref="Optimus.Interop"/>: the unit handling moved to
    /// <see cref="CorelDocumentState"/> (millimetres = 3, not 4) and the traversal moved to
    /// <see cref="ShapeWalker"/>, which descends into groups, effect groups AND PowerClips. The
    /// previous version guessed the unit and never entered a PowerClip, which is why it measured
    /// 0% reduction on real client files.
    /// </para>
    ///
    /// <para>Confirmed API surface (typelib dump): <c>NodeRange.AutoReduce(PrecisionMargin)</c>,
    /// <c>Document.BeginCommandGroup</c>/<c>EndCommandGroup</c>, <c>Document.ClearUndoList()</c>,
    /// <c>Document.Save</c>/<c>Close</c>, <c>Application.OpenDocument</c>.</para>
    /// </summary>
    public sealed class FileOptimizer
    {
        private readonly dynamic _app;
        private readonly OptimizeOptions _opt;

        public FileOptimizer(object application, OptimizeOptions options)
        {
            _app = application;
            _opt = options;
        }

        /// <summary>
        /// Reads the document WITHOUT modifying it and returns what is actually inside — including
        /// everything hidden in PowerClips. This is the honest answer to "why is my file heavy?".
        /// </summary>
        public InspectResult Inspect()
        {
            dynamic doc = ActiveDocumentOrThrow();
            using (new CorelDocumentState(doc))
            {
                // Selection scope still needs the per-shape walk (there is no FindShapes on a
                // selection); the whole page uses the bulk reader, measured ~1000x faster.
                ShapeInventory inventory;
                string diag;
                int nested, unclassified = 0;
                IList<object> curvesForProbe = new List<object>();
                if (_opt.SelectionOnly)
                {
                    var walker = new ShapeWalker { CountNodes = _opt.CountNodes };
                    inventory = walker.WalkShapes(_app.ActiveSelection.Shapes).Inventory;
                    diag = "walk(selection): " + walker.Stats;
                    nested = inventory.ShapesInPowerClip;
                }
                else
                {
                    PageInventoryReader.Result r = new PageInventoryReader()
                        .Read(doc.ActivePage, countNodes: _opt.CountNodes, collectCurves: false);
                    inventory = r.Inventory;
                    diag = r.Diagnostics;
                    nested = r.Nested;
                    unclassified = r.Unclassified;
                }

                string path = Safe(() => (string)doc.FullFileName, "");

                var res = new InspectResult
                {
                    // Naming the document removes all ambiguity about WHICH file a report describes
                    // — "0 images" means nothing until you know which file was measured.
                    DocName = Safe(() => (string)doc.Name, "(documento)"),
                    DocPath = path,
                    Inventory = inventory,
                    Stats = diag,
                    NodesCounted = _opt.CountNodes,
                    Nested = nested,
                    Unclassified = unclassified,
                };

                // Coverage of the file's static id, logged as a plain fact. Measured at 0,1% on this
                // drawing — CorelDRAW allocates it lazily — which is why it cannot bridge the file census
                // to the live document, and why the probe that assumed it could was removed.
                if (res.ShapeIds != null)
                    OptimusLog.Write($"Inspect: IDS formas={res.ShapeIds.Shapes.Count} " +
                        $"comId={res.ShapeIds.ShapesWithId} ({res.ShapeIds.IdCoveragePercent}%) " +
                        $"serveDePonte={res.ShapeIds.IdsAreUnique}");

                // THE premise test, on ONE curve. Reading curve points through raw IDispatch is the only
                // remaining route to grouping repeated art, and it either works on this CorelDRAW build
                // or it does not. Asking once costs microseconds; building a feature on an unverified
                // assumption cost a whole release last time.
                ProbeCurveReading(doc);

                // NOTE: the COM repetition probe deliberately does NOT run here.
                //
                // Analysing must stay fast — it is the button the operator presses to understand a file,
                // and 17 s is already at the limit of patience. The repetition opportunity is already
                // measured EXACTLY by the file census (O15), straight off the container, with no COM at
                // all; running a second, slower COM census here bought nothing and cost minutes.
                // RepetitionFinder belongs to the CONVERSION path, where its cost buys a mutation and
                // where it must run with confirmAll: true anyway.

                // Byte composition comes from the SAVED file, read straight off the container — no
                // CorelDRAW involved, so it cannot alter what it measures. An unsaved document has
                // no path and therefore no measurable size; that is reported, not guessed.
                if (!string.IsNullOrWhiteSpace(path))
                {
                    // analyzePayload: true also runs the repetition census — the measurement that found
                    // the biggest lever in the product (O15). It costs one extra pass over data1.dat.
                    var container = new CdrContainerReader().Read(path, analyzePayload: true);
                    res.Composition = container.Composition;
                    res.Payload = container.Payload;
                    res.ShapeIds = container.ShapeIds;
                    res.Ceiling = ReductionCeiling.For(container.Composition);
                    res.ContainerNote = container.Note;
                    res.FontNames = container.FontNames;
                    res.IccBytes = container.IccBytesFound;
                    res.IccProfileNames = container.IccProfileNames;
                    // Whether the embedded profile is actually referenced decides whether removing it
                    // is a cleanup or a colour-management trade-off. Measured: it is almost always used.
                    res.IccUsed = container.ColorContext.HasProfile("Cmyk")
                        ? !container.ColorContext.IsProfileUnused("Cmyk")
                        : container.ColorContext.HasProfile("Rgb") && !container.ColorContext.IsProfileUnused("Rgb");
                }

                // Fluidity score. Transparencies are not counted yet (Phase 4 measures them), so
                // this is a LOWER BOUND on the real interaction cost.
                res.Interaction = new InteractionInputs
                {
                    Nodes = _opt.CountNodes ? inventory.TotalNodes : 0,
                    Objects = inventory.TotalShapes,
                    NestedObjects = nested,
                    LiveEffects = inventory.LiveEffects,
                };
                res.InteractionScore = InteractionWeight.Compute(res.Interaction);
                res.InteractionBand = InteractionWeight.Classify(res.InteractionScore).ToString();
                res.InteractionDominant = InteractionWeight.Dominant(res.Interaction).ToString();
                return res;
            }
        }

        /// <summary>
        /// Applies the operator's chosen settings to the active document.
        ///
        /// <para>Order is a safety contract: measure → BACKUP (verified, aborts on failure) → mutate
        /// inside one undo transaction → clear undo → save with the chosen options → close/reopen to
        /// reserialize → measure again and report the REAL result against what was promised.</para>
        /// </summary>
        public OptimizeResult Apply(OptimizationSettings settings, Action<string>? progress = null)
        {
            void Say(string s) { OptimusLog.Write("apply: " + s); progress?.Invoke(s); }

            dynamic doc = ActiveDocumentOrThrow();
            var result = new OptimizeResult { DocName = Safe(() => (string)doc.Name, "(documento)") };

            string path = Safe(() => (string)doc.FullFileName, "");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new OptimizeException(
                    "Salve o arquivo (.cdr) uma vez antes de otimizar — a redução só aparece no arquivo salvo.");

            // 1. Measure before, and record what we are about to promise.
            var reader = new CdrContainerReader();
            CdrContainerReader.Result before = reader.Read(path, analyzePayload: true);
            result.FileSizeBefore = before.Composition.TotalBytes;
            double? geometryShare = before.Payload?.GeometryShareOfFile(before.Composition.TotalBytes);
            result.EstimatedPercent =
                SavingsEstimator.Estimate(before.Composition, settings, geometryShare).TotalPercent;

            var saver = new DocumentSaver(_app)
            {
                // Only ever KEEP a profile, never add one to a file that had none.
                SourceHadColorProfile = before.Composition.Of(CdrComponent.IccProfile) > 0,
            };
            OptimusLog.Write("apply: perfil de cor no original = "
                + before.Composition.Of(CdrComponent.IccProfile) + " bytes");

            // 1b. Fluidity baseline — by RENDERING the document, not by asking for a repaint.
            // Timing Window.Refresh() measured 0,1 ms before and 0 ms after on a real file: it only posts
            // a repaint request and returns. RenderTimer forces Corel to actually draw every object.
            var timer = new RenderTimer((object)_app, (Action<string>)(s => OptimusLog.Write("apply: " + s)));
            if (settings.MeasureRedraw)
            {
                RenderTiming baseline = timer.Measure();
                if (baseline.Measured)
                {
                    result.RedrawBeforeMs = baseline.MedianMs;
                    Say($"Tempo para desenhar antes: {baseline.MedianMs:0.#} ms");
                }
                else
                {
                    Say("Tempo para desenhar: não foi possível medir (" + baseline.Note + ").");
                }
            }

            // 2. Backup FIRST. An irreversible operation without a verified safety net is not offered.
            Say("Criando cópia de segurança…");
            result.BackupPath = saver.CreateVerifiedBackup(doc, path);
            Say("Cópia de segurança: " + Path.GetFileName(result.BackupPath));

            // 3. Mutations, all inside one undo transaction — and with the canvas frozen.
            //    BulkEditScope is what makes this finish in minutes instead of a quarter of an hour:
            //    without it CorelDRAW repaints after every single node edit.
            using (new CorelDocumentState(doc))
            using (var bulk = new BulkEditScope((object)_app, (Action<string>)(s => OptimusLog.Write("apply: " + s))))
            using (new CommandGroupScope(doc, "Optimus — Otimizar"))
            {
                OptimusLog.Write("apply: modo de edição em massa " + (bulk.Active ? "ATIVO" : "indisponível"));

                Say("Lendo objetos…");
                PageInventoryReader.Result page = new PageInventoryReader()
                    .Read(doc.ActivePage, countNodes: settings.SimplifyDrawing, collectCurves: settings.SimplifyDrawing);

                ShapeInventory inv = page.Inventory;
                result.ShapesVisited = inv.TotalShapes;
                result.ShapesInPowerClip = page.Nested;
                result.RasterShapes = inv.RasterShapes;
                result.LiveEffects = inv.LiveEffects;
                result.NodesBefore = inv.TotalNodes;

                // Repeated art FIRST. Simplifying beforehand makes byte-identical copies stop being
                // identical and burns this lever — measured: 4.789 distinct blocks became 5.400 and the
                // recoverable share fell from 58,2% to 23,7% (O15). Order is not negotiable.
                if (settings.InstanceRepeatedArt && page.Curves.Count > 0)
                {
                    Say($"Procurando arte repetida entre {page.Curves.Count} curva(s)…");

                    var finder = new RepetitionFinder(
                        (Action<string>)(s => OptimusLog.Write("apply: " + s)));

                    // confirmAll: true — this is about to CHANGE the drawing, so every candidate is
                    // compared coordinate by coordinate. A sampled report is refused by the converter.
                    var findClock = System.Diagnostics.Stopwatch.StartNew();
                    RepetitionReport repetition = finder.Find(page.Curves, confirmAll: true);
                    findClock.Stop();

                    OptimusLog.Write($"apply: repetição em {findClock.ElapsedMilliseconds} ms — " +
                        $"distintas={repetition.DistinctShapes} repetidas={repetition.RepeatedShapes} " +
                        $"grupos={repetition.Groups.Count} podeAlterar={repetition.SafeToMutate}");

                    if (!repetition.SafeToMutate)
                    {
                        Say("Arte repetida não foi convertida: " + repetition.Note);
                    }
                    else if (repetition.Groups.Count == 0)
                    {
                        Say("Nenhuma arte repetida encontrada.");
                    }
                    else
                    {
                        Say($"Instanciando {repetition.RepeatedShapes} cópia(s) em "
                          + $"{repetition.Groups.Count} símbolo(s)…");

                        var converter = new SymbolConverter(
                            TimeSpan.FromMinutes(10),
                            (Action<string>)(s => OptimusLog.Write("apply: " + s)));

                        SymbolConversionResult symbols = converter.Convert((object)doc, repetition);
                        result.SymbolsCreated = symbols.GroupsConverted;
                        result.ShapesInstanced = symbols.ShapesInstanced;

                        OptimusLog.Write($"apply: SIMBOLOS grupos={symbols.GroupsConverted} " +
                            $"formas={symbols.ShapesInstanced} em {symbols.ElapsedMs} ms " +
                            $"timeout={symbols.TimedOut} pulados={symbols.Skipped.Count}");
                        foreach (string skip in symbols.Skipped) OptimusLog.Write("apply:   pulado: " + skip);

                        Say($"{symbols.ShapesInstanced} cópia(s) viraram instâncias de "
                          + $"{symbols.GroupsConverted} símbolo(s).");

                        // The curve list now holds deleted shapes; re-read before touching nodes.
                        page = new PageInventoryReader()
                            .Read(doc.ActivePage, countNodes: settings.SimplifyDrawing,
                                  collectCurves: settings.SimplifyDrawing);
                    }
                }

                if (settings.SimplifyDrawing && settings.ToleranceMm.HasValue)
                {
                    double tolerance = settings.ToleranceMm.Value;
                    Say($"Simplificando {page.Curves.Count} curva(s) com tolerância de {tolerance} mm…");

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    foreach (object shape in page.Curves) ReduceShape(shape, tolerance, result);
                    sw.Stop();

                    // Node totals are read ONCE from the page afterwards instead of per curve. Asking
                    // each curve for Nodes.Count before AND after meant materialising the node
                    // collection twice per shape — 28.684 collection builds on this file, which is
                    // where the ten minutes were going (O9: the cost is per object touched).
                    result.NodesAfter = new PageInventoryReader()
                        .Read(doc.ActivePage, countNodes: true, collectCurves: false)
                        .Inventory.TotalNodes;

                    OptimusLog.Write($"apply: simplificação levou {sw.Elapsed.TotalSeconds:0.#} s " +
                        $"({page.Curves.Count} curvas, {sw.Elapsed.TotalMilliseconds / Math.Max(1, page.Curves.Count):0.#} ms/curva)");
                }
                else
                {
                    result.NodesAfter = result.NodesBefore;   // untouched, so before == after
                }

                // Live effects are regenerated every frame, so flattening them is often the biggest
                // fluidity win — and it costs editability, which is why it is opt-in.
                var effects = new EffectInventory();
                EffectInventory.Result found = effects.Scan(doc.ActivePage);
                result.LiveEffects = found.Effects.Count;
                result.Transparencies = found.Transparencies;
                result.EffectCostScore = found.TotalCost;

                if (settings.FlattenEffects && found.EffectShapes.Count > 0)
                {
                    Say($"Achatando {found.EffectShapes.Count} efeito(s) vivo(s)…");
                    result.EffectsFlattened = effects.Flatten(found.EffectShapes);
                }

                // Bitmaps: only images ABOVE the chosen target are touched, and never below it (O3).
                if (settings.TargetDpi.HasValue)
                {
                    int target = settings.TargetDpi.Value;
                    var inspector = new BitmapInspector();
                    BitmapInspector.Result raster = inspector.Scan(doc.ActivePage);
                    List<PlacedBitmap> candidates = raster.Candidates(target);
                    result.BitmapsFound = raster.Bitmaps.Count;
                    result.BitmapsAboveTarget = candidates.Count;
                    result.UnresampleableRaster = raster.UnresampleableRasterShapes;

                    if (candidates.Count > 0)
                    {
                        Say($"Reamostrando {candidates.Count} de {raster.Bitmaps.Count} imagem(ns) para {target} DPI…");
                        result.BitmapsResampled = inspector.Resample(candidates, target);
                    }
                    else if (raster.Bitmaps.Count > 0)
                    {
                        Say($"As {raster.Bitmaps.Count} imagem(ns) já estão em {target} DPI ou abaixo — nada a fazer.");
                    }
                }

                if (settings.RemoveOffPage)
                {
                    Say("Removendo objetos fora da página…");
                    result.OffPageDeleted = RemoveOffPageShapes(doc);
                }

                if (settings.RemoveEmptyLayers)
                {
                    Say("Removendo camadas vazias…");
                    result.EmptyLayersDeleted = RemoveEmptyLayers(doc);
                }
            }

            // 4. Drop retained undo state BEFORE saving — the "file got bigger" fix.
            Say("Limpando histórico de desfazer…");
            DocumentSaver.ClearUndo(doc);

            // 5. Save with the options the sliders produced.
            Say("Salvando…");
            saver.SaveOptimized(doc, path, settings);

            // 6. Reserialize clean.
            if (settings.Reserialize)
            {
                Say("Fechando e reabrindo para reserializar limpo…");
                result.Reopened = saver.CloseAndReopen(path) != null;
            }

            // 7. Measure the REAL outcome and compare against the promise.
            CdrContainerReader.Result after = reader.Read(path);
            result.FileSizeAfter = after.Composition.TotalBytes;
            result.Delta = CompositionDelta.Between(before.Composition, after.Composition);

            // 7b. Fluidity after — same zoom, same machine, same session. If it did not improve, the
            // report says so instead of showing only the weight score (P1).
            if (settings.MeasureRedraw && result.RedrawBeforeMs > 0)
            {
                RenderTiming end = timer.Measure();
                if (end.Measured)
                {
                    result.RedrawAfterMs = end.MedianMs;
                    Say($"Tempo para desenhar depois: {end.MedianMs:0.#} ms " +
                        $"({(result.RedrawImprovedPct >= 0 ? "-" : "+")}{Math.Abs(result.RedrawImprovedPct):0.#}%)");
                }
            }

            Say(result.FileGrew
                ? "Atenção: o arquivo ficou maior. A cópia de segurança está preservada."
                : $"Concluído: {result.FileReductionPct:0.#}% menor (estimado {result.EstimatedPercent:0.#}%).");
            return result;
        }

        /// <summary>
        /// Legacy fixed-recipe entry point, retained so the existing button keeps working. New work
        /// goes through <see cref="Apply"/>, which honours the operator's sliders.
        /// </summary>
        public OptimizeResult Run(Action<string>? progress = null)
        {
            void Say(string s) { OptimusLog.Write("optimize: " + s); progress?.Invoke(s); }

            dynamic doc = ActiveDocumentOrThrow();
            var result = new OptimizeResult { DocName = Safe(() => (string)doc.Name, "(documento)") };

            // A never-saved document has no path, so there is nothing to measure or reserialize.
            string path = Safe(() => (string)doc.FullFileName, "");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new OptimizeException(
                    "Salve o arquivo (.cdr) uma vez antes de otimizar — a redução de tamanho só aparece no arquivo salvo.");

            result.FileSizeBefore = SafeFileSize(path);

            // Millimetres for the whole operation; original unit restored even on error.
            using (new CorelDocumentState(doc))
            {
                bool grouped = TrySet(() => doc.BeginCommandGroup("Optimus — Otimizar"));
                try
                {
                    Say("Lendo objetos…");
                    ShapeInventory inv;
                    List<object> curves;
                    if (_opt.SelectionOnly)
                    {
                        ShapeWalker.WalkResult walk = new ShapeWalker().WalkShapes(_app.ActiveSelection.Shapes);
                        inv = walk.Inventory;
                        curves = walk.ReducibleShapes;
                    }
                    else
                    {
                        // Bulk reader: a handful of COM calls instead of one per shape (measured
                        // 171 ms vs 174 s on a 16.901-shape file). Node counts are needed here to
                        // report a real before/after, so countNodes is always on for optimization.
                        PageInventoryReader.Result r = new PageInventoryReader()
                            .Read(doc.ActivePage, countNodes: true, collectCurves: true);
                        inv = r.Inventory;
                        curves = r.Curves;
                        OptimusLog.Write("Optimize: " + r.Diagnostics);
                    }

                    result.ShapesVisited = inv.TotalShapes;
                    result.ShapesInPowerClip = inv.ShapesInPowerClip;
                    result.RasterShapes = inv.RasterShapes;
                    result.LiveEffects = inv.LiveEffects;
                    result.NodesBefore = inv.TotalNodes;

                    Say($"Reduzindo nós de {curves.Count} curva(s)…");
                    foreach (object shape in curves)
                        ReduceShape(shape, result);

                    if (_opt.RemoveOffPage)
                    {
                        Say("Removendo objetos fora da página…");
                        result.OffPageDeleted = RemoveOffPageShapes(doc);
                    }

                    if (_opt.RemoveEmptyLayers)
                    {
                        Say("Removendo camadas vazias…");
                        result.EmptyLayersDeleted = RemoveEmptyLayers(doc);
                    }
                }
                finally
                {
                    if (grouped) TrySet(() => doc.EndCommandGroup());
                }

                // Drop the retained undo history BEFORE saving — the confirmed fix for the
                // "optimized file ended up heavier" problem.
                Say("Limpando histórico de desfazer…");
                TrySet(() => doc.ClearUndoList());
            }

            Say("Salvando…");
            doc.Save();

            if (_opt.CloseAndReopen)
            {
                Say("Fechando e reabrindo para reserializar limpo…");
                result.Reopened = TryCloseReopen(path);
            }

            result.FileSizeAfter = SafeFileSize(path);
            Say("Concluído.");
            return result;
        }

        /// <summary>
        /// Reduces one curve's node count via <c>AutoReduce</c>, counting nodes before and after.
        /// The tolerance is in millimetres because the document unit was set by
        /// <see cref="CorelDocumentState"/>.
        /// </summary>
        private void ReduceShape(object shapeObj, OptimizeResult result) =>
            ReduceShape(shapeObj, _opt.ToleranceMm, result);

        /// <summary>
        /// Simplifies one curve.
        ///
        /// <para>
        /// Deliberately does NOT count nodes before and after. Doing so cost two extra materialisations
        /// of the whole node collection per shape — measured at 43 ms per curve on Davi's file, i.e.
        /// <b>10 minutes for 14.342 curves</b>. The totals are read once from the page afterwards.
        /// </para>
        /// <para>
        /// Every intermediate COM object is released explicitly. Per O9, an STA <c>Release</c> runs on the
        /// owning thread and blocks it, so leaving 14.342 × 3 RCWs for the garbage collector stalls
        /// CorelDRAW later, at a moment that looks unrelated.
        /// </para>
        /// </summary>
        private void ReduceShape(object shapeObj, double toleranceMm, OptimizeResult result)
        {
            object? curveObj = null, nodesObj = null, allObj = null;

            try
            {
                try { curveObj = (object)((dynamic)shapeObj).Curve; }
                catch { return; }
                if (curveObj == null) return;

                try { nodesObj = (object)((dynamic)curveObj).Nodes; }
                catch { return; }
                if (nodesObj == null) return;

                try { allObj = (object)((dynamic)nodesObj).All; }
                catch { return; }
                if (allObj == null) return;

                // Tolerance is in MILLIMETRES because CorelDocumentState put the document in mm.
                ((dynamic)allObj).AutoReduce(toleranceMm);
                result.CurvesReduced++;
            }
            catch (Exception ex)
            {
                OptimusLog.Write("AutoReduce skipped on a shape: " + ex.Message);
            }
            finally
            {
                ReleaseCom(allObj);
                ReleaseCom(nodesObj);
                ReleaseCom(curveObj);
            }
        }

        /// <summary>
        /// Asks ONE curve for its points, and logs the answer.
        ///
        /// <para>
        /// This is the premise the whole repeated-art feature rests on, isolated into a test that costs
        /// microseconds. Two releases were built on unverified premises before this: one on
        /// <c>GetCurveInfo</c> marshalling (it does not), one on the file's static id being usable (it
        /// covers 0,1% of the shapes). Both had to be withdrawn. A premise gets its own probe now.
        /// </para>
        /// </summary>
        private void ProbeCurveReading(dynamic doc)
        {
            object? shape = null;
            try
            {
                dynamic shapes = doc.ActivePage.FindShapes(
                    Type.Missing, Optimus.Core.Corel.CorelConstants.CdrCurveShape, true);
                if (shapes == null || (int)shapes.Count < 1)
                {
                    OptimusLog.Write("Inspect: CURVAS-CRU nenhuma curva para testar");
                    return;
                }

                shape = (object)shapes[1];

                var clock = System.Diagnostics.Stopwatch.StartNew();
                var reader = new CurveInfoReader((Action<string>)(s => OptimusLog.Write("inspect: " + s)));
                List<CurvePoint>? points = reader.Read(shape);
                clock.Stop();

                if (points == null)
                {
                    OptimusLog.Write($"Inspect: CURVAS-CRU FALHOU em {clock.ElapsedMilliseconds} ms " +
                                     "— o agrupamento de arte repetida continua bloqueado");
                    return;
                }

                OptimusLog.Write($"Inspect: CURVAS-CRU OK em {clock.ElapsedMilliseconds} ms — " +
                                 $"{points.Count} ponto(s) lidos");

                for (int i = 0; i < Math.Min(3, points.Count); i++)
                    OptimusLog.Write($"Inspect: CURVAS-CRU  ponto[{i}] x={points[i].X:0.####} " +
                                     $"y={points[i].Y:0.####} elem={points[i].ElementType} " +
                                     $"no={points[i].NodeType}");
            }
            catch (Exception ex)
            {
                OptimusLog.Write("Inspect: CURVAS-CRU exceção: " + ex.Message);
            }
            finally { ReleaseCom(shape); }
        }

        private static void ReleaseCom(object? o)
        {
            try
            {
                if (o != null && System.Runtime.InteropServices.Marshal.IsComObject(o))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(o);
            }
            catch (Exception) { }
        }

        private static long SafeNodeCount(dynamic curve)
        {
            try { return (long)curve.Nodes.Count; } catch { return 0; }
        }

        /// <summary>Deletes shapes lying fully outside the page. Collect first, then delete.</summary>
        private int RemoveOffPageShapes(dynamic doc)
        {
            double pw, ph;
            try { pw = (double)doc.ActivePage.SizeWidth; ph = (double)doc.ActivePage.SizeHeight; }
            catch { return 0; }

            var doomed = new List<object>();
            try
            {
                dynamic shapes = doc.ActivePage.Shapes;
                int count = (int)shapes.Count;
                for (int i = 1; i <= count; i++)
                {
                    dynamic shp;
                    try { shp = shapes[i]; } catch { continue; }
                    try
                    {
                        double left = (double)shp.LeftX, right = (double)shp.RightX;
                        double bottom = (double)shp.BottomY, top = (double)shp.TopY;
                        // Page spans (0,0)..(pw,ph) from the lower-left. Fully outside = entirely
                        // past one edge; a small epsilon avoids clipping page-edge art.
                        bool outside = right < -0.01 || left > pw + 0.01 || top < -0.01 || bottom > ph + 0.01;
                        if (outside) doomed.Add((object)shp);
                    }
                    catch { /* unreadable bbox → leave it be */ }
                }
            }
            catch { return 0; }

            int n = 0;
            foreach (object shp in doomed)
            {
                try { ((dynamic)shp).Delete(); n++; } catch { }
            }
            return n;
        }

        /// <summary>Deletes layers with no shapes across every page.</summary>
        private int RemoveEmptyLayers(dynamic doc)
        {
            int n = 0;
            try
            {
                int pageCount = (int)doc.Pages.Count;
                for (int p = 1; p <= pageCount; p++)
                {
                    dynamic page = doc.Pages[p];
                    var empties = new List<object>();
                    int layerCount = (int)page.Layers.Count;
                    for (int l = 1; l <= layerCount; l++)
                    {
                        dynamic layer = page.Layers[l];
                        try { if ((int)layer.Shapes.Count == 0) empties.Add((object)layer); }
                        catch { }
                    }
                    foreach (object layer in empties)
                    {
                        try { ((dynamic)layer).Delete(); n++; } catch { /* undeletable special layer */ }
                    }
                }
            }
            catch (Exception ex) { OptimusLog.Write("RemoveEmptyLayers skipped: " + ex.Message); }
            return n;
        }

        private bool TryCloseReopen(string path)
        {
            try
            {
                dynamic doc = _app.ActiveDocument;
                doc.Close();
                dynamic reopened = _app.OpenDocument(path);
                try { reopened.Save(); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                OptimusLog.Write("CloseReopen FAILED: " + ex.Message);
                return false;
            }
        }

        private dynamic ActiveDocumentOrThrow()
        {
            object? doc = null;
            try { doc = _app.ActiveDocument; } catch { }
            if (doc == null)
                throw new OptimizeException("Abra um documento no CorelDRAW antes de otimizar.");
            return doc;
        }

        private static long SafeFileSize(string path)
        {
            try { return new FileInfo(path).Length; } catch { return 0; }
        }

        private static T Safe<T>(Func<T> get, T fallback)
        {
            try { return get(); } catch { return fallback; }
        }

        private static bool TrySet(Action action)
        {
            try { action(); return true; } catch (Exception ex) { OptimusLog.Write("op skipped: " + ex.Message); return false; }
        }
    }

    /// <summary>An operator-friendly, non-bug precondition failure (no doc / unsaved doc).</summary>
    public sealed class OptimizeException : Exception
    {
        public OptimizeException(string message) : base(message) { }
    }
}
