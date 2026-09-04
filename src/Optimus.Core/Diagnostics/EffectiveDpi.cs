using System;

namespace Optimus.Core.Diagnostics
{
    /// <summary>
    /// Effective resolution of a placed image: pixels ÷ the physical size it occupies on the page.
    ///
    /// <para>
    /// This — not the nominal DPI stored in the image — is what decides whether pixels are wasted. A
    /// 4000 px image placed in a 50 mm box carries ~2032 effective DPI; at a 300 DPI print target,
    /// ~98% of its pixel AREA can never be printed. Measurement of a real client file found 88.5 MB
    /// of uncompressed raster inside a 4.5 MB document, which is exactly this waste.
    /// </para>
    /// <para>
    /// Policy (O3): only images ABOVE the target are candidates, and the default target is 300 DPI —
    /// the print standard — so the default is invisible on paper. Anything at or below the target is
    /// never touched.
    /// </para>
    /// </summary>
    public static class EffectiveDpi
    {
        private const double MmPerInch = 25.4;

        /// <summary>
        /// Margin above the target before resampling is considered worthwhile. Without it, an image
        /// at 305 DPI would be resampled for a ~3% gain and a real quality cost.
        /// </summary>
        public const double ToleranceFactor = 1.10;

        /// <summary>Print-safe default target.</summary>
        public const int DefaultTargetDpi = 300;

        /// <summary>Effective DPI, or 0 for degenerate input (never infinity).</summary>
        public static double Compute(int pixels, double physicalMm)
        {
            if (pixels <= 0 || physicalMm <= 0) return 0;
            return pixels / (physicalMm / MmPerInch);
        }

        /// <summary>True when the image carries meaningfully more resolution than the target needs.</summary>
        public static bool NeedsResampling(double effectiveDpi, int targetDpi = DefaultTargetDpi)
        {
            if (effectiveDpi <= 0 || targetDpi <= 0) return false;
            return effectiveDpi > targetDpi * ToleranceFactor;
        }

        /// <summary>Pixel count that renders the given physical size exactly at the target DPI.</summary>
        public static int TargetPixels(double physicalMm, int targetDpi = DefaultTargetDpi)
        {
            if (physicalMm <= 0 || targetDpi <= 0) return 1;
            return Math.Max(1, (int)Math.Round(physicalMm / MmPerInch * targetDpi));
        }

        /// <summary>
        /// Bytes recoverable by resampling, scaled by AREA — halving the resolution quarters the
        /// pixel count. Returns 0 when no resampling is warranted, so an estimate is never shown for
        /// work that will not happen.
        /// </summary>
        public static double EstimatedByteSaving(long currentBytes, double effectiveDpi, int targetDpi = DefaultTargetDpi)
        {
            if (currentBytes <= 0) return 0;
            if (!NeedsResampling(effectiveDpi, targetDpi)) return 0;

            double scale = targetDpi / effectiveDpi;      // linear
            double remaining = scale * scale;             // area
            return currentBytes * (1 - remaining);
        }
    }
}
