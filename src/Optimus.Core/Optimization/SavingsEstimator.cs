using System;
using System.Collections.Generic;
using Optimus.Core.Diagnostics;

namespace Optimus.Core.Optimization
{
    /// <summary>One estimated saving, in the operator's language.</summary>
    public sealed class SavingItem
    {
        /// <summary>Label shown to the operator, e.g. "Perfil de cor".</summary>
        public string Label { get; set; } = "";

        public long Bytes { get; set; }
        public double Percent { get; set; }

        /// <summary>True when this saving costs the artwork something.</summary>
        public bool Lossy { get; set; }

        /// <summary>
        /// False when the file simply does not contain this component, so the control must appear
        /// DISABLED with the reason. Promising a saving that cannot happen is the failure mode this
        /// flag exists to prevent (R3.5).
        /// </summary>
        public bool Applicable { get; set; } = true;

        /// <summary>Why it does not apply, when it does not.</summary>
        public string NotApplicableReason { get; set; } = "";
    }

    public sealed class SavingsEstimate
    {
        public List<SavingItem> Items { get; } = new List<SavingItem>();

        public long TotalBytes { get; set; }
        public double TotalPercent { get; set; }

        /// <summary>Estimated size after the selected settings.</summary>
        public long ProjectedBytes { get; set; }

        /// <summary>True when any selected saving is lossy.</summary>
        public bool AnyLossy { get; set; }
    }

    /// <summary>
    /// Turns the operator's slider positions into a per-component estimate, using the EXACT byte
    /// composition measured in Phase 2.
    ///
    /// <para>
    /// Two rules keep it honest. First, a component the file does not have yields
    /// <see cref="SavingItem.Applicable"/> = false with a reason, never a zero-that-looks-like-a-
    /// saving — the client's own `kaneki.cdr` has no embedded profile, so its colour slider must say
    /// so instead of implying a win. Second, the estimator is deliberately CONSERVATIVE: it is
    /// checked against the real measured result afterwards, and an estimate that exceeds reality
    /// makes the product a liar.
    /// </para>
    /// </summary>
    public static class SavingsEstimator
    {
        /// <summary>
        /// Share of a large embedded profile recovered by swapping it for a small standard one.
        /// A standard sRGB/SWOP profile is a few hundred KB against 1.4–2.7 MB press profiles.
        /// </summary>
        private const double EconomicalProfileRecovery = 0.75;

        /// <summary>Portion of metadata that is genuinely redundant.</summary>
        private const double MetadataSlack = 0.5;

        /// <summary>
        /// Conservative recovery from resampling, applied to the RASTER share. Real gains are usually
        /// far higher because bitmaps are stored uncompressed, but over-promising is worse than
        /// under-promising.
        /// </summary>
        private const double RasterRecovery = 0.6;

        /// <summary>Geometry share used only when the payload was not analysed. See the remarks on
        /// <see cref="GeometryItem"/> — this is file-dependent and must not be trusted as a constant.</summary>
        private const double FallbackGeometrySharePercent = 4.6;

        /// <summary>
        /// How much of a node reduction actually shows up as bytes on disk. <b>Removing 31% of the nodes
        /// does NOT remove 31% of the file.</b>
        ///
        /// <para>
        /// Measured on <c>kaneki.cdr</c> (2026-07-25, the first end-to-end run on Davi's machine):
        /// nodes 421.057 → 290.587 (−31%) produced 7.959.265 → 6.818.507 bytes (−14,3%), against an
        /// estimate of 24,8%. The container is a ZIP: coordinates are deflated, and removing points
        /// shrinks the input without shrinking the compressed output proportionally — simplified
        /// coordinates are also less repetitive, so the compressor does slightly worse per byte.
        /// </para>
        /// <para>
        /// The ratio measured there was 0,63. This constant is deliberately set BELOW it: the product's
        /// rule is that under-promising is a disappointment and over-promising is a lie (P1), and one
        /// file is one data point. Revise it when there are more measurements, never upward on a hunch.
        /// </para>
        /// </summary>
        private const double DeflateRecovery = 0.55;

        /// <summary>
        /// <paramref name="measuredGeometryShareOfFile"/> — geometry's REAL share of the file, from
        /// <see cref="ObjectPayloadAnalyzer"/>. Pass it whenever available: on a geometry-dominated
        /// drawing it is ~73%, and without it the estimate falls back to a share taken from a file
        /// where geometry was incidental, understating the drawing lever by an order of magnitude.
        /// </summary>
        public static SavingsEstimate Estimate(
            CdrComposition composition, OptimizationSettings settings, double? measuredGeometryShareOfFile = null)
        {
            var e = new SavingsEstimate();
            if (composition == null || !composition.Available || settings == null) return e;

            long total = composition.TotalBytes;

            e.Items.Add(ColorItem(composition, settings));
            e.Items.Add(PreviewItem(composition, settings));
            e.Items.Add(CompatibilityItem(composition, settings));
            e.Items.Add(ImageItem(composition, settings));
            e.Items.Add(GeometryItem(composition, settings, measuredGeometryShareOfFile));

            long sum = 0;
            foreach (SavingItem item in e.Items)
            {
                if (!item.Applicable) continue;
                sum += item.Bytes;
                if (item.Bytes > 0 && item.Lossy) e.AnyLossy = true;
                item.Percent = total > 0 ? Math.Round(item.Bytes * 100.0 / total, 1) : 0;
            }

            // Never claim to remove more than the file holds.
            e.TotalBytes = Math.Max(0, Math.Min(sum, total));
            e.TotalPercent = total > 0 ? Math.Round(e.TotalBytes * 100.0 / total, 1) : 0;
            e.ProjectedBytes = Math.Max(0, total - e.TotalBytes);
            return e;
        }

        private static SavingItem ColorItem(CdrComposition c, OptimizationSettings s)
        {
            long icc = c.Of(CdrComponent.IccProfile);
            var item = new SavingItem { Label = "Perfil de cor", Lossy = true };

            if (icc <= 0)
            {
                item.Applicable = false;
                item.NotApplicableReason = "Este arquivo não tem perfil de cor embutido — não há nada a economizar aqui.";
                return item;
            }

            switch (s.Color)
            {
                case ColorFidelity.NoProfile: item.Bytes = icc; break;
                case ColorFidelity.Economical: item.Bytes = (long)(icc * EconomicalProfileRecovery); break;
                default: item.Bytes = 0; item.Lossy = false; break;   // Maximum / High keep it
            }
            return item;
        }

        private static SavingItem PreviewItem(CdrComposition c, OptimizationSettings s)
        {
            long preview = c.Of(CdrComponent.Preview);
            // Free: the preview is only the thumbnail Explorer shows, and it is STORED
            // (incompressible) in real files.
            var item = new SavingItem { Label = "Miniatura embutida", Lossy = false };

            if (preview <= 0)
            {
                item.Applicable = false;
                item.NotApplicableReason = "Este arquivo não tem miniatura embutida.";
                return item;
            }

            item.Bytes = s.RemovePreview ? preview : 0;
            return item;
        }

        private static SavingItem CompatibilityItem(CdrComposition c, OptimizationSettings s)
        {
            long metadata = c.Of(CdrComponent.Metadata);
            var item = new SavingItem { Label = "Dados de compatibilidade", Lossy = false };

            if (metadata <= 0)
            {
                item.Applicable = false;
                item.NotApplicableReason = "Nada de compatibilidade a remover.";
                return item;
            }

            item.Bytes = s.RemoveCompatibilityData ? (long)(metadata * MetadataSlack) : 0;
            return item;
        }

        private static SavingItem ImageItem(CdrComposition c, OptimizationSettings s)
        {
            long raster = c.Of(CdrComponent.Raster);
            var item = new SavingItem { Label = "Imagens", Lossy = true };

            if (raster <= 0)
            {
                item.Applicable = false;
                item.NotApplicableReason = "Este arquivo não tem imagens — reduzir qualidade de imagem não muda nada.";
                return item;
            }

            item.Bytes = s.TargetDpi.HasValue ? (long)(raster * RasterRecovery) : 0;
            if (!s.TargetDpi.HasValue) item.Lossy = false;
            return item;
        }

        /// <summary>
        /// Node reduction. How much it is worth in SIZE depends entirely on how much of the file is
        /// geometry — measured at ~73% on a geometry-dominated drawing and near nothing on an
        /// ICC-dominated one. Its fluidity payoff is reported separately (O8).
        /// </summary>
        private static SavingItem GeometryItem(
            CdrComposition c, OptimizationSettings s, double? measuredGeometryShareOfFile)
        {
            long vector = c.Of(CdrComponent.Vector);
            var item = new SavingItem { Label = "Simplificação do desenho", Lossy = true };

            if (vector <= 0)
            {
                item.Applicable = false;
                item.NotApplicableReason = "Este arquivo não tem desenho vetorial a simplificar.";
                return item;
            }

            if (!s.SimplifyDrawing)
            {
                // The operator chose not to touch the curves — no saving and, crucially, no loss.
                item.Bytes = 0;
                item.Lossy = false;
                return item;
            }

            // Use the MEASURED geometry share when we have it; the fallback is the share of a file
            // where geometry was incidental, and using it blindly understated this lever ~15x.
            double geometryShare = (measuredGeometryShareOfFile ?? FallbackGeometrySharePercent) / 100.0;

            // How many NODES the tolerance removes. Measured on kaneki.cdr at Balanced: 421.057 → 290.587,
            // i.e. 31% — close enough to the 0.3 modelled here.
            double nodeReduction = s.Weight == DrawingWeight.MaxFluidity ? 0.5
                                 : s.Weight == DrawingWeight.Balanced ? 0.3 : 0.1;

            item.Bytes = (long)(c.TotalBytes * geometryShare * nodeReduction * DeflateRecovery);
            return item;
        }
    }
}
