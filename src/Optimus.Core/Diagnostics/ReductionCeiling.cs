using System;

namespace Optimus.Core.Diagnostics
{
    /// <summary>
    /// How much of a given .cdr can honestly be removed, split by what it costs the artwork.
    ///
    /// <para>
    /// This class exists because the client's target is "reduce 50%" and measurement of his own
    /// files showed that target is arithmetically impossible on some of them without degrading
    /// images: one file is 63% raster, so deleting ALL geometry still leaves ~37%. The product
    /// states the real ceiling instead of promising a number (O4/P1).
    /// </para>
    /// </summary>
    public sealed class ReductionCeiling
    {
        /// <summary>Above this share of raster, 50% is unreachable without resampling.</summary>
        private const double RasterDominanceThreshold = 40.0;

        /// <summary>
        /// FALLBACK share of document bytes held by geometry, used only when the payload was not
        /// analysed.
        ///
        /// <para>
        /// ⚠ This value is FILE-DEPENDENT and treating it as a constant was a real error. It came from
        /// a file dominated by an embedded ICC profile, where geometry was incidental. Measured on a
        /// geometry-dominated client file (`kaneki.cdr`), the object payload's compressed cost is
        /// <b>97.3% geometry</b> — about 73% of the whole file — so halving the nodes is worth tens of
        /// percent there, not two. Always prefer the measured share
        /// (<see cref="For(CdrComposition, double?)"/>).
        /// </para>
        /// </summary>
        private const double FallbackGeometryShareOfDocument = 4.6;

        /// <summary>Fraction of nodes a safe tolerance can realistically remove.</summary>
        private const double AchievableNodeRemoval = 0.5;

        /// <summary>
        /// Fraction of raster bytes recoverable by resampling to 300 DPI. Deliberately conservative:
        /// bitmaps are stored UNCOMPRESSED in the container (88.5 MB deflating to 2.86 MB in a real
        /// file), so real gains are usually far higher — but the report must not over-promise.
        /// </summary>
        private const double ConservativeRasterRecovery = 0.6;

        private ReductionCeiling(
            double lossless, double withoutRaster, double withResampling,
            double nodeReduction, bool rasterDominated)
        {
            LosslessPercent = Clamp(lossless);
            CeilingWithoutRasterPercent = Clamp(withoutRaster);
            CeilingWithResamplingPercent = Clamp(withResampling);
            NodeReductionPercent = Clamp(nodeReduction);
            RasterDominated = rasterDominated;
        }

        /// <summary>Removable with ZERO effect on the artwork: preview, redundant descriptors.</summary>
        public double LosslessPercent { get; }

        /// <summary>Lossless plus realistic node reduction — i.e. the best case that never touches
        /// an image. This is the number to compare against the 50% target.</summary>
        public double CeilingWithoutRasterPercent { get; }

        /// <summary>The above plus conservative bitmap resampling (LOSSY, opt-in).</summary>
        public double CeilingWithResamplingPercent { get; }

        /// <summary>What halving the node count is worth in file bytes. Small by nature.</summary>
        public double NodeReductionPercent { get; }

        /// <summary>True when raster payload dominates the file.</summary>
        public bool RasterDominated { get; }

        /// <summary>Share held by the embedded ICC profile — often the single biggest item.</summary>
        public double IccPercent { get; internal set; }

        /// <summary>Share held by embedded font payload.</summary>
        public double EmbeddedFontPercent { get; internal set; }

        /// <summary>
        /// The dominant component, so the UI recommends the lever that actually matters instead of a
        /// generic "optimize". On a file that is 97% ICC, advising node reduction would be absurd.
        /// </summary>
        public string DominantLever
        {
            get
            {
                if (IccPercent >= 30) return "icc";
                if (EmbeddedFontPercent >= 30) return "fonts";
                if (RasterDominated) return "raster";
                return "vector";
            }
        }

        /// <summary>True when the 50% target cannot be met without resampling images. The UI must
        /// say this out loud rather than silently miss the target.</summary>
        public bool FiftyPercentRequiresResampling =>
            CeilingWithoutRasterPercent < 50 && CeilingWithResamplingPercent >= CeilingWithoutRasterPercent
            && RasterDominated;

        /// <summary>
        /// <paramref name="measuredGeometryShareOfFile"/> is the percentage of the FILE that geometry
        /// really costs, from <see cref="ObjectPayloadAnalyzer"/>. Pass it whenever available: it is
        /// the difference between telling the operator "node reduction buys 2%" and the truth for a
        /// geometry-dominated file, which is tens of percent.
        /// </summary>
        public static ReductionCeiling For(CdrComposition composition, double? measuredGeometryShareOfFile = null)
        {
            if (composition == null || !composition.Available)
                return new ReductionCeiling(0, 0, 0, 0, false);

            double preview = composition.PercentOf(CdrComponent.Preview);
            double metadataSlack = composition.PercentOf(CdrComponent.Metadata) * 0.5; // some is required
            double rasterShare = composition.PercentOf(CdrComponent.Raster);
            double iccShare = composition.PercentOf(CdrComponent.IccProfile);
            double fontShare = composition.PercentOf(CdrComponent.EmbeddedFonts);

            // Preview and slack metadata are the only genuinely FREE removals.
            double lossless = preview + metadataSlack;

            // ICC and embedded fonts are the biggest levers by far (97.3% and 93% of real files),
            // but neither is free: the profile is normally referenced by objects in that space, and
            // dropping embedded fonts means the file no longer renders identically on a machine
            // without them. Both are therefore OPT-IN with a stated cost (P3), and are reported
            // separately from the lossless figure so the operator decides.
            double optIn = iccShare + fontShare;

            double geometryShare = measuredGeometryShareOfFile ?? FallbackGeometryShareOfDocument;
            double nodes = geometryShare * AchievableNodeRemoval;
            double withoutRaster = lossless + optIn + nodes;
            double withResampling = withoutRaster + rasterShare * ConservativeRasterRecovery;

            return new ReductionCeiling(
                lossless, withoutRaster, withResampling, nodes,
                rasterDominated: rasterShare >= RasterDominanceThreshold)
            {
                IccPercent = Clamp(iccShare),
                EmbeddedFontPercent = Clamp(fontShare),
            };
        }

        private static double Clamp(double v) => Math.Max(0, Math.Min(100, Math.Round(v, 1)));
    }
}
