using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Optimus.Interop
{
    /// <summary>One render timing, in milliseconds.</summary>
    public sealed class RenderTiming
    {
        public double MedianMs { get; set; }
        public double MinMs { get; set; }
        public double MaxMs { get; set; }
        public int Samples { get; set; }

        /// <summary>False when nothing could be timed. Then NO claim about fluidity may be made.</summary>
        public bool Measured { get; set; }

        /// <summary>Why it could not be measured, for the log.</summary>
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// Times how long CorelDRAW takes to actually DRAW the document, by rendering it to a bitmap.
    ///
    /// <para>
    /// <b>This replaces timing <c>Window.Refresh()</c>, which measured nothing.</b> Measured on Davi's
    /// machine: 0,1 ms before and 0 ms after simplifying 130.000 nodes away — because <c>Refresh()</c>
    /// only POSTS a repaint request and returns; the drawing happens later, on Corel's own message loop.
    /// Any before/after built on it is noise dressed as evidence, and the product does not ship that.
    /// </para>
    /// <para>
    /// <c>Document.ExportBitmap</c> is synchronous — it cannot return before the file exists — and it
    /// pushes every object through Corel's own rasteriser, so it scales with exactly what makes a drawing
    /// heavy: node count, object count, effects, transparencies. It is NOT the on-screen redraw path, and
    /// the UI says so: it is a comparable measure of <i>how much work this drawing is to draw</i>, taken
    /// the same way before and after on the same machine.
    /// </para>
    /// <para>
    /// Every parameter is passed explicitly through <c>InvokeMember</c>. That is the same hazard that broke
    /// <c>SaveAsCopy</c>: a late-bound call omitting trailing optional parameters fails with "Could not
    /// convert argument 0".
    /// </para>
    /// </summary>
    public sealed class RenderTimer
    {
        // Verified in the typelib dump: cdrPNG = 802 (556), cdrCurrentPage = 1 (437),
        // cdrRGBColorImage = 4 (790), cdrNormalAntiAliasing = 1 (83), cdrCompressionNone = 0 (185).
        private const int CdrPng = 802;
        private const int CdrCurrentPage = 1;
        private const int CdrRgbColorImage = 4;
        private const int CdrNormalAntiAliasing = 1;
        private const int CdrCompressionNone = 0;

        /// <summary>
        /// Render size. Big enough that the geometry — not the fixed overhead — dominates the timing, and
        /// small enough that a heavy file does not take a minute per sample.
        /// </summary>
        private const int RenderPixels = 1200;

        private const int DefaultSamples = 3;

        private readonly dynamic _app;
        private readonly Action<string>? _log;

        public RenderTimer(object application, Action<string>? log = null)
        {
            _app = application;
            _log = log;
        }

        public RenderTiming Measure(int samples = DefaultSamples)
        {
            var timing = new RenderTiming();
            if (samples < 2) samples = 2;

            string dir = Path.Combine(Path.GetTempPath(), "Optimus_render");
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) { timing.Note = "sem pasta temporária: " + ex.Message; return timing; }

            object doc;
            try { doc = (object)_app.ActiveDocument; }
            catch (Exception ex) { timing.Note = "sem documento ativo: " + ex.Message; return timing; }
            if (doc == null) { timing.Note = "sem documento ativo"; return timing; }

            var kept = new List<double>();
            var sw = new Stopwatch();

            for (int i = 0; i < samples; i++)
            {
                string file = Path.Combine(dir, "probe" + i + ".png");
                try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { }

                sw.Restart();
                bool ok = TryExport(doc, file, out string error);
                sw.Stop();

                if (!ok)
                {
                    timing.Note = error;
                    _log?.Invoke("RenderTimer: " + error);
                    return timing;
                }

                // The file must actually exist: an export that silently produced nothing would time the
                // failure path and report it as a fast drawing.
                if (!File.Exists(file))
                {
                    timing.Note = "o CorelDRAW não gerou o arquivo de teste";
                    return timing;
                }

                // Discard the first: it pays for filter loading and cache warm-up, not for the drawing.
                if (i > 0) kept.Add(sw.Elapsed.TotalMilliseconds);

                try { File.Delete(file); } catch (Exception) { }
            }

            if (kept.Count == 0) { timing.Note = "nenhuma amostra válida"; return timing; }

            kept.Sort();
            timing.Measured = true;
            timing.Samples = kept.Count;
            timing.MinMs = Math.Round(kept[0], 1);
            timing.MaxMs = Math.Round(kept[kept.Count - 1], 1);
            timing.MedianMs = Math.Round(Median(kept), 1);
            return timing;
        }

        /// <summary>
        /// Argument shapes to try, longest first.
        ///
        /// <para>
        /// The 16-argument form failed on CorelDRAW 2024 with <c>DISP_E_TYPEMISMATCH</c> — passing
        /// <c>Type.Missing</c> for the trailing <c>StructPaletteOptions</c> and <c>Rect</c> parameters is
        /// not something that dispatch accepts. Rather than guess which one it dislikes (and CLAUDE.md
        /// forbids guessing a VGCore member), the timer tries progressively shorter forms and LOGS which
        /// one worked, so the next build can be exact.
        /// </para>
        /// </summary>
        private object[][] ArgumentForms(string file) => new[]
        {
            // Everything up to the two object-typed optionals, which are simply omitted.
            new object[]
            {
                file, CdrPng, CdrCurrentPage, CdrRgbColorImage,
                RenderPixels, RenderPixels, 96, 96,
                CdrNormalAntiAliasing, false, false, false, false, CdrCompressionNone,
            },

            // Size and resolution only — enough to keep the render comparable before/after.
            new object[]
            {
                file, CdrPng, CdrCurrentPage, CdrRgbColorImage,
                RenderPixels, RenderPixels, 96, 96,
            },

            // Range and image type only; Corel picks the size.
            new object[] { file, CdrPng, CdrCurrentPage, CdrRgbColorImage },

            // The minimum the signature demands.
            new object[] { file, CdrPng },
        };

        /// <summary>Index of the argument form that worked, so the retries happen only once.</summary>
        private int _workingForm = -1;

        private bool TryExport(object doc, string file, out string error)
        {
            error = "";
            object[][] forms = ArgumentForms(file);

            // Once a form is known to work, use only that one — retrying the failing shapes on every
            // sample would put the failure latency into the measurement.
            int first = _workingForm >= 0 ? _workingForm : 0;
            int last = _workingForm >= 0 ? _workingForm : forms.Length - 1;

            for (int i = first; i <= last; i++)
            {
                object? filter = null;
                try
                {
                    filter = doc.GetType().InvokeMember(
                        "ExportBitmap", BindingFlags.InvokeMethod, null, doc, forms[i]);

                    // ICorelExportFilter.Finish() (typelib dump 2046) is what writes the file.
                    if (filter != null)
                        filter.GetType().InvokeMember("Finish", BindingFlags.InvokeMethod, null, filter, null);

                    if (_workingForm < 0)
                    {
                        _workingForm = i;
                        _log?.Invoke("RenderTimer: ExportBitmap aceitou a forma de "
                                   + forms[i].Length + " argumentos.");
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    error = "ExportBitmap (" + forms[i].Length + " args) falhou: "
                          + (ex.InnerException?.Message ?? ex.Message);
                }
                finally
                {
                    try
                    {
                        if (filter != null && System.Runtime.InteropServices.Marshal.IsComObject(filter))
                            System.Runtime.InteropServices.Marshal.ReleaseComObject(filter);
                    }
                    catch (Exception) { }
                }
            }

            return false;
        }

        private static double Median(List<double> sorted)
        {
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }
}
