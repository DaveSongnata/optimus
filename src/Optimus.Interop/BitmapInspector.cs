using System;
using System.Collections.Generic;
using Optimus.Core.Corel;
using Optimus.Core.Diagnostics;

namespace Optimus.Interop
{
    /// <summary>One placed bitmap, with the resolution it EFFECTIVELY carries on the page.</summary>
    public sealed class PlacedBitmap
    {
        /// <summary>The live COM shape, so the resampler can act on exactly this one.</summary>
        public object Shape { get; set; } = null!;

        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }

        /// <summary>Placed size on the page, in millimetres.</summary>
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }

        /// <summary>Pixels ÷ placed size. The only figure that says whether pixels are wasted.</summary>
        public double EffectiveDpiX { get; set; }
        public double EffectiveDpiY { get; set; }

        /// <summary>Worst of the two axes — resampling is judged by the higher one.</summary>
        public double EffectiveDpi => Math.Max(EffectiveDpiX, EffectiveDpiY);

        public long PixelCount => (long)PixelWidth * PixelHeight;

        public bool NeedsResampling(int targetDpi) =>
            EffectiveDpi.NeedsResamplingAt(targetDpi);
    }

    /// <summary>Tiny extension so the call site reads like the rule it enforces.</summary>
    internal static class DpiExtensions
    {
        internal static bool NeedsResamplingAt(this double effectiveDpi, int targetDpi) =>
            EffectiveDpi.NeedsResampling(effectiveDpi, targetDpi);
    }

    /// <summary>
    /// Finds the bitmaps in a document and decides which ones carry more resolution than the job needs.
    ///
    /// <para>
    /// Why EFFECTIVE dpi and not the nominal value stored in the image: a 4000 px image placed in a
    /// 50 mm box carries ~2032 dpi, and at a 300 dpi print target ~98% of its pixel AREA can never be
    /// printed. That waste is exactly what put 88.5 MB of uncompressed raster inside a 4.5 MB client
    /// file.
    /// </para>
    /// <para>
    /// Verified members: <c>Page.FindShapes(, cdrBitmapShape, true)</c>, <c>Shape.Bitmap</c> (slot 20),
    /// <c>Bitmap.SizeWidth/SizeHeight</c> (pixels), <c>Shape.SizeWidth/SizeHeight</c> (document units
    /// — millimetres, because <see cref="CorelDocumentState"/> set them), and
    /// <c>Bitmap.Resample(Width?, Height?, AntiAlias?, ResolutionX?, ResolutionY?)</c> (dump 3289).
    /// </para>
    /// <para>
    /// ⚠ <c>Document.ResolveAllBitmapsLinks()</c> is NEVER called: it EMBEDS linked bitmaps and makes
    /// the file BIGGER. It is named here so nobody reaches for it by mistake.
    /// </para>
    /// </summary>
    public sealed class BitmapInspector
    {
        public sealed class Result
        {
            public List<PlacedBitmap> Bitmaps { get; } = new List<PlacedBitmap>();

            /// <summary>OLE and EPS shapes carry raster too but are NOT resampleable — reported only.</summary>
            public int UnresampleableRasterShapes { get; set; }

            public long TotalPixels
            {
                get { long n = 0; foreach (PlacedBitmap b in Bitmaps) n += b.PixelCount; return n; }
            }

            /// <summary>Bitmaps above the target, i.e. the candidates for resampling.</summary>
            public List<PlacedBitmap> Candidates(int targetDpi)
            {
                var list = new List<PlacedBitmap>();
                foreach (PlacedBitmap b in Bitmaps) if (b.NeedsResampling(targetDpi)) list.Add(b);
                return list;
            }
        }

        /// <summary>Read-only scan. The document must already be in millimetres.</summary>
        public Result Scan(dynamic page)
        {
            var result = new Result();

            object? range = Find(page, CorelConstants.CdrBitmapShape);
            if (range != null)
            {
                int count;
                try { count = (int)((dynamic)range).Count; } catch { count = 0; }

                dynamic shapes = range;
                for (int i = 1; i <= count; i++)
                {
                    object? shape = null;
                    bool retained = false;
                    try
                    {
                        shape = shapes[i];
                        if (shape == null) continue;
                        PlacedBitmap? placed = Measure(shape);
                        if (placed != null) { result.Bitmaps.Add(placed); retained = true; }
                    }
                    catch { }
                    finally { if (!retained) Release(shape); }
                }
                Release(range);
            }

            // OLE and EPS also carry raster but expose no Bitmap object — counted, never touched.
            result.UnresampleableRasterShapes =
                CountOf(page, CorelConstants.CdrOleObjectShape) + CountOf(page, CorelConstants.CdrEpsShape);
            return result;
        }

        /// <summary>
        /// Resamples the candidates to the target dpi. Returns how many were changed.
        ///
        /// <para>All four dimensions are passed EXPLICITLY: the documentation says nothing about a 0
        /// sentinel meaning "keep", so relying on one would be a guess with irreversible consequences
        /// for the customer's image.</para>
        /// </summary>
        public int Resample(IEnumerable<PlacedBitmap> candidates, int targetDpi)
        {
            if (candidates == null || targetDpi <= 0) return 0;

            int changed = 0;
            foreach (PlacedBitmap b in candidates)
            {
                if (b == null || b.Shape == null) continue;
                if (!b.NeedsResampling(targetDpi)) continue;   // never touch what is already fine

                int newWidth = EffectiveDpi.TargetPixels(b.WidthMm, targetDpi);
                int newHeight = EffectiveDpi.TargetPixels(b.HeightMm, targetDpi);
                if (newWidth >= b.PixelWidth && newHeight >= b.PixelHeight) continue;

                object? bitmap = null;
                try
                {
                    bitmap = ((dynamic)b.Shape).Bitmap;
                    if (bitmap == null) continue;
                    ((dynamic)bitmap).Resample(newWidth, newHeight, true, (double)targetDpi, (double)targetDpi);
                    changed++;
                }
                catch (Exception ex)
                {
                    // One stubborn image must not abort the rest of the job.
                    System.Diagnostics.Debug.WriteLine("Resample skipped: " + ex.Message);
                }
                finally { Release(bitmap); }
            }
            return changed;
        }

        private static PlacedBitmap? Measure(object shape)
        {
            object? bitmap = null;
            try
            {
                bitmap = ((dynamic)shape).Bitmap;
                if (bitmap == null) return null;

                int px = (int)((dynamic)bitmap).SizeWidth;
                int py = (int)((dynamic)bitmap).SizeHeight;
                double mmW = (double)((dynamic)shape).SizeWidth;
                double mmH = (double)((dynamic)shape).SizeHeight;

                if (px <= 0 || py <= 0 || mmW <= 0 || mmH <= 0) return null;

                return new PlacedBitmap
                {
                    Shape = shape,
                    PixelWidth = px,
                    PixelHeight = py,
                    WidthMm = mmW,
                    HeightMm = mmH,
                    EffectiveDpiX = EffectiveDpi.Compute(px, mmW),
                    EffectiveDpiY = EffectiveDpi.Compute(py, mmH),
                };
            }
            catch { return null; }
            finally { Release(bitmap); }
        }

        private static int CountOf(dynamic page, int cdrShapeType)
        {
            object? range = Find(page, cdrShapeType);
            if (range == null) return 0;
            try { return (int)((dynamic)range).Count; }
            catch { return 0; }
            finally { Release(range); }
        }

        private static object? Find(dynamic page, int cdrShapeType)
        {
            try { return page.FindShapes(Type.Missing, cdrShapeType, true); }
            catch { return null; }
        }

        private static void Release(object? comObject)
        {
            if (comObject == null) return;
            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
            }
            catch { }
        }
    }
}
