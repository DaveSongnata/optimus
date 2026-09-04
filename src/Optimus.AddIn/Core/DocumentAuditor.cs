using System;
using System.Collections.Generic;
using Optimus.Core.Audit;
using Optimus.Interop;
using Optimus.Windows;
using Optimus.AddIn.Ui;

namespace Optimus.AddIn.Core
{
    /// <summary>Everything the colour/font/accent audit found.</summary>
    public sealed class AuditReport
    {
        public string DocName = "";
        public ColorAudit Colors = new ColorAudit();

        /// <summary>Fonts used on the page, with their pt-BR accent verdict.</summary>
        public List<GlyphCoverage> Fonts = new List<GlyphCoverage>();

        public int TextShapes;
        public int MixedFontStories;

        /// <summary>How many fonts CorelDRAW itself offers — includes Font Manager folders, not just
        /// what Windows installed. Zero means the list was unavailable and the Windows list was used.</summary>
        public int CorelFontCount;
        public long ElapsedMs;

        /// <summary>
        /// True only when every shape was actually walked, so the per-colour usage percentages are
        /// real. False on the fast document-palette route, where the colours are right but "how much
        /// of the drawing" is unknown — and the UI must then show no percentage rather than a zero
        /// that would read as "barely used".
        /// </summary>
        public bool UsageMeasured;

        /// <summary>Fonts that would print hollow boxes instead of accented letters.</summary>
        public List<GlyphCoverage> FailingFonts
        {
            get
            {
                var failing = new List<GlyphCoverage>();
                foreach (GlyphCoverage f in Fonts)
                    if (f.Verdict == FontVerdict.Failed || f.Verdict == FontVerdict.NotInstalled)
                        failing.Add(f);
                return failing;
            }
        }
    }

    /// <summary>
    /// Runs the document audit: which colours, which fonts, and whether those fonts can actually set
    /// Portuguese.
    ///
    /// <para>
    /// STRICTLY READ-ONLY. The colour pass in particular is built around
    /// <c>Color.GetCopy()</c> because <c>Fill.UniformColor</c> is a live reference — an audit that
    /// modifies the artwork it inspects would be worse than no audit.
    /// </para>
    /// </summary>
    public sealed class DocumentAuditor
    {
        private readonly dynamic _app;

        public DocumentAuditor(object application) => _app = application;

        /// <summary>
        /// Runs the audit. <paramref name="progress"/> names the current stage;
        /// <paramref name="stageProgress"/> reports (stageIndex, stageCount, done, total) so the UI can
        /// show a DETERMINATE bar — the colour walk alone measured 90 s on a real file, and an
        /// indeterminate spinner for that long is indistinguishable from a hang.
        /// </summary>
        /// <param name="measureUsage">
        /// When false (the default), colours come from CorelDRAW's own document palette — seconds
        /// instead of the ~90 s a full shape walk costs, at the price of not knowing how MUCH each
        /// colour is used. The operator asks for the exact usage measurement separately, when the
        /// question is actually "which colour dominates this drawing".
        /// </param>
        /// <param name="partialColors">
        /// Called with the ranking SO FAR while the usage walk runs, so the operator watches it form
        /// instead of staring at a bar. Only fires on the measuring route — the fast route has nothing
        /// to stream because it finishes at once.
        /// </param>
        public AuditReport Run(Action<string>? progress = null,
                               Action<int, int, int, int>? stageProgress = null,
                               bool measureUsage = false,
                               Action<ColorAudit>? partialColors = null)
        {
            const int StageCount = 3;
            void Say(string s) { OptimusLog.Write("audit: " + s); progress?.Invoke(s); }
            void Stage(int index, int done, int total)
            {
                try { stageProgress?.Invoke(index, StageCount, done, total); } catch (Exception) { }
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var report = new AuditReport();

            dynamic doc;
            try { doc = _app.ActiveDocument; }
            catch { throw new OptimizeException("Abra um documento no CorelDRAW antes de auditar."); }
            if (doc == null) throw new OptimizeException("Abra um documento no CorelDRAW antes de auditar.");

            try { report.DocName = (string)doc.Name; } catch { report.DocName = "(documento)"; }

            using (new CorelDocumentState(doc))
            {
                Say("Lendo cores…");
                Stage(0, 0, 1);

                // Fast route first: CorelDRAW scans the artwork in its OWN native code and hands back
                // the finished colour list. Only fall back to the 16.899-shape COM walk when that
                // route is unavailable, or when the operator explicitly asked to measure usage.
                bool usedFastPath = false;
                if (!measureUsage)
                {
                    FastColorCollector.Result fast =
                        new FastColorCollector().Collect(doc, (Action<string>)(s => Say(s)));
                    if (fast.Available)
                    {
                        report.Colors = fast.Audit;
                        report.UsageMeasured = false;
                        usedFastPath = true;
                        Say($"Paleta do documento: {fast.Audit.Unique.Count} cor(es).");
                    }
                    else
                    {
                        OptimusLog.Write("audit: paleta rápida indisponível (" + fast.Note + ") — usando varredura completa.");
                    }
                }

                if (!usedFastPath)
                {
                    var colors = new ColorCollector
                    {
                        Progress = (done, total, soFar) =>
                        {
                            Stage(0, done, total);
                            try { partialColors?.Invoke(soFar); } catch (Exception) { }
                        }
                    };
                    report.Colors = colors.Collect(doc.ActivePage);
                    report.UsageMeasured = true;
                }

                Say("Lendo fontes…");
                Stage(1, 0, 1);
                FontCollector.Result fonts = new FontCollector().Collect(doc.ActivePage);
                report.TextShapes = fonts.TextShapes;
                report.MixedFontStories = fonts.MixedFontStories;

                Say($"Verificando acentuação de {fonts.Fonts.Count} fonte(s)…");
                Stage(2, 0, Math.Max(1, fonts.Fonts.Count));

                // CorelDRAW's OWN font list is the authority on what it can set type in — it includes
                // fonts activated by Corel Font Manager straight from a folder, which Windows never
                // installed. Judging by the Windows list alone reported those as "não instalada":
                // a false alarm on a font that is present and working.
                List<string> corelFonts = FontCollector.InstalledPerCorel(_app);
                report.CorelFontCount = corelFonts.Count;
                report.Fonts = new FontProbe(corelFonts).CheckAll(fonts.Fonts);
                Stage(2, fonts.Fonts.Count, Math.Max(1, fonts.Fonts.Count));
            }

            sw.Stop();
            report.ElapsedMs = sw.ElapsedMilliseconds;
            OptimusLog.Write($"audit: DONE in {report.ElapsedMs} ms — " +
                $"colors={report.Colors.Unique.Count} (spot={report.Colors.SpotColors().Count}, " +
                $"mixed={report.Colors.MixesRgbAndCmyk}, notInspectable={report.Colors.NotInspectable}) " +
                $"fonts={report.Fonts.Count} failing={report.FailingFonts.Count} " +
                $"textShapes={report.TextShapes} mixedStories={report.MixedFontStories}");
            return report;
        }
    }
}
