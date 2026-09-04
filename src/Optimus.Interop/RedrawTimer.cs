using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Optimus.Interop
{
    /// <summary>A redraw timing, in milliseconds.</summary>
    public sealed class RedrawTiming
    {
        /// <summary>Median of the kept samples — the headline figure.</summary>
        public double MedianMs { get; set; }

        public double MinMs { get; set; }
        public double MaxMs { get; set; }
        public int Samples { get; set; }

        /// <summary>False when the redraw could not be timed (no window/view).</summary>
        public bool Measured { get; set; }

        /// <summary>Spread between fastest and slowest kept sample; large spread = noisy machine.</summary>
        public double SpreadMs => Math.Max(0, MaxMs - MinMs);
    }

    /// <summary>
    /// Times a full document redraw, so "more fluid" is a measured claim rather than an adjective.
    ///
    /// <para>
    /// This exists because the product refuses to sell fluidity the way "RAM cleaners" sell speed. The
    /// interaction-weight score ranks documents, but only a clock says whether the operator's drag got
    /// better. Method: fix the viewport with <c>ActiveView.ToFitPage()</c> so before/after are
    /// comparable, then time N × <c>Window.Refresh()</c>, DISCARD the first sample (caches, lazy
    /// allocation) and report the MEDIAN — a mean would be dragged around by one stall.
    /// </para>
    /// <para>
    /// Both members are verified in the typelib: <c>Window.Refresh()</c> (dump 7811) and
    /// <c>ActiveView.ToFitPage()</c> (2975). Timings are only comparable WITHIN one session on one
    /// machine at the same zoom — never across machines.
    /// </para>
    /// </summary>
    public sealed class RedrawTimer
    {
        /// <summary>Samples taken; the first is always discarded as warm-up.</summary>
        private const int DefaultSamples = 4;

        private readonly dynamic _app;

        public RedrawTimer(object application) => _app = application;

        /// <summary>
        /// Fixes the zoom to fit-page so a before/after pair measures the same work. Best-effort: a
        /// document without an active view simply keeps its current zoom.
        /// </summary>
        public bool NormalizeView()
        {
            try
            {
                _app.ActiveDocument.ActiveView.ToFitPage();
                return true;
            }
            catch { return false; }
        }

        public RedrawTiming Measure(int samples = DefaultSamples)
        {
            var timing = new RedrawTiming();
            if (samples < 2) samples = 2;

            dynamic window;
            try { window = _app.ActiveWindow; }
            catch { return timing; }
            if (window == null) return timing;

            var kept = new List<double>();
            var sw = new Stopwatch();

            for (int i = 0; i < samples; i++)
            {
                sw.Restart();
                try { window.Refresh(); }
                catch { return timing; }   // cannot time a redraw we cannot trigger
                sw.Stop();

                // Discard the first: it pays for caches and lazy allocation, not for the drawing.
                if (i > 0) kept.Add(sw.Elapsed.TotalMilliseconds);
            }

            if (kept.Count == 0) return timing;

            kept.Sort();
            timing.Measured = true;
            timing.Samples = kept.Count;
            timing.MinMs = Math.Round(kept[0], 1);
            timing.MaxMs = Math.Round(kept[kept.Count - 1], 1);
            timing.MedianMs = Math.Round(Median(kept), 1);
            return timing;
        }

        private static double Median(List<double> sorted)
        {
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }
}
