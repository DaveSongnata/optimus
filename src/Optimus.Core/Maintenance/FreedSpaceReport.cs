using System;
using System.Collections.Generic;

namespace Optimus.Core.Maintenance
{
    /// <summary>Bytes attributed to one cleanup category — an ESTIMATE, and labelled as such.</summary>
    public sealed class CategoryResult
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";

        /// <summary>Sum of the sizes of files actually deleted.</summary>
        public long BytesRemoved { get; set; }

        public int FilesRemoved { get; set; }

        /// <summary>Files skipped because another program had them open.</summary>
        public int FilesLocked { get; set; }
    }

    /// <summary>
    /// The honest space report.
    ///
    /// <para>
    /// ONE RULE, and it is the product's reputation: <b>the headline is the disk's real free-space
    /// delta</b>; per-category numbers are labelled estimates and their sum is NEVER presented as the
    /// result. Byte-summing deleted files overstates the gain — hardlinks (WinSxS is a hardlink farm),
    /// cluster slack and NTFS compression all make logical size bigger than the space actually
    /// recovered. Every cleaner that shows a headline bigger than the disk gained is lying, and
    /// "these tools exaggerate" is exactly this market's reputation problem.
    /// </para>
    /// <para>
    /// It also refuses to invent a positive result: if another process wrote more than we freed, the
    /// headline is 0 with an explanation, never a negative "saving" and never the category sum.
    /// </para>
    /// </summary>
    public sealed class FreedSpaceReport
    {
        private readonly List<CategoryResult> _categories = new List<CategoryResult>();

        /// <summary>Free space before, from the drive itself.</summary>
        public long FreeBeforeBytes { get; set; }

        /// <summary>Free space after, from the drive itself.</summary>
        public long FreeAfterBytes { get; set; }

        /// <summary>
        /// Measured idle drift of the drive's free space, sampled before starting. It is the
        /// uncertainty of the headline: printing "6,84 GB" when the noise floor is ±200 MB is false
        /// precision.
        /// </summary>
        public long NoiseFloorBytes { get; set; }

        public IReadOnlyList<CategoryResult> Categories => _categories;

        public void Add(CategoryResult category)
        {
            if (category != null) _categories.Add(category);
        }

        /// <summary>THE headline: what the operator will see in Explorer. Never negative.</summary>
        public long RealFreedBytes => Math.Max(0, FreeAfterBytes - FreeBeforeBytes);

        /// <summary>True when the disk lost space during the run because something else was writing.</summary>
        public bool AnotherProcessWrote => FreeAfterBytes < FreeBeforeBytes;

        /// <summary>Sum of the category estimates. Diagnostic only — never the headline.</summary>
        public long EstimatedBytes
        {
            get { long n = 0; foreach (CategoryResult c in _categories) n += c.BytesRemoved; return n; }
        }

        public int TotalFilesRemoved
        {
            get { int n = 0; foreach (CategoryResult c in _categories) n += c.FilesRemoved; return n; }
        }

        public int TotalFilesLocked
        {
            get { int n = 0; foreach (CategoryResult c in _categories) n += c.FilesLocked; return n; }
        }

        /// <summary>
        /// True when the measurement is too noisy to quote precisely — the run freed less than the
        /// drive's own idle drift.
        /// </summary>
        public bool BelowNoiseFloor => NoiseFloorBytes > 0 && RealFreedBytes < NoiseFloorBytes;

        /// <summary>The headline sentence, in the operator's language.</summary>
        public string Headline()
        {
            if (AnotherProcessWrote)
                return "Espaço liberado: 0 — outro programa gravou dados durante a limpeza, "
                     + "então o disco não mostrou ganho.";

            if (RealFreedBytes == 0)
                return "Espaço liberado: 0 — não havia nada a remover nas opções escolhidas.";

            string amount = Format(RealFreedBytes);
            return BelowNoiseFloor
                ? $"Espaço liberado: cerca de {amount} (valor pequeno demais para medir com precisão)."
                : $"Espaço liberado: {amount}.";
        }

        /// <summary>
        /// The caveat that must accompany the breakdown. It exists so the operator is never told the
        /// category sum is the result.
        /// </summary>
        public string BreakdownCaveat() =>
            EstimatedBytes > RealFreedBytes && RealFreedBytes > 0
                ? "Os valores por categoria são estimativas e somam mais que o ganho real do disco "
                + "(arquivos compartilhados e arredondamento de blocos) — o número que vale é o de cima."
                : "Os valores por categoria são estimativas; o número que vale é o ganho real do disco, acima.";

        public static string Format(long bytes)
        {
            if (bytes <= 0) return "0";
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.#") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("0.##") + " GB";
        }
    }
}
