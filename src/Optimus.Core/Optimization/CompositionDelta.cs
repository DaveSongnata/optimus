using System;
using System.Collections.Generic;
using Optimus.Core.Diagnostics;

namespace Optimus.Core.Optimization
{
    /// <summary>What one component gained or lost between two measurements of the same file.</summary>
    public sealed class ComponentDelta
    {
        public CdrComponent Component { get; set; }
        public long BeforeBytes { get; set; }
        public long AfterBytes { get; set; }

        /// <summary>Positive = shrank. Negative = GREW, which must be reported as growth.</summary>
        public long SavedBytes => BeforeBytes - AfterBytes;

        public double SavedPercent =>
            BeforeBytes <= 0 ? 0 : Math.Round(SavedBytes * 100.0 / BeforeBytes, 1);
    }

    /// <summary>
    /// Before/after comparison of a real file, per component.
    ///
    /// <para>
    /// This is the number the product is allowed to advertise: measured on disk, not estimated. It
    /// also exists to catch the v1.0 failure mode — the "optimized" file came out BIGGER — which a
    /// tool that only reports estimates would never notice.
    /// </para>
    /// </summary>
    public sealed class CompositionDelta
    {
        private CompositionDelta(long before, long after, List<ComponentDelta> components)
        {
            BeforeBytes = before;
            AfterBytes = after;
            Components = components;
        }

        public long BeforeBytes { get; }
        public long AfterBytes { get; }
        public IReadOnlyList<ComponentDelta> Components { get; }

        /// <summary>Bytes actually removed. Negative when the file grew.</summary>
        public long SavedBytes => BeforeBytes - AfterBytes;

        public double SavedPercent =>
            BeforeBytes <= 0 ? 0 : Math.Round(SavedBytes * 100.0 / BeforeBytes, 1);

        /// <summary>
        /// True when the file GREW. Optimus v1.0 shipped this exact outcome without noticing, so it is
        /// a first-class result rather than an edge case: the UI must say the file grew, and by how
        /// much, instead of showing a negative "saving".
        /// </summary>
        public bool FileGrew => AfterBytes > BeforeBytes;

        public static CompositionDelta Between(CdrComposition before, CdrComposition after)
        {
            var components = new List<ComponentDelta>();
            if (before == null || after == null)
                return new CompositionDelta(0, 0, components);

            var kinds = new HashSet<CdrComponent>();
            foreach (CdrComponent k in before.Bytes.Keys) kinds.Add(k);
            foreach (CdrComponent k in after.Bytes.Keys) kinds.Add(k);

            foreach (CdrComponent kind in kinds)
            {
                long b = before.Of(kind);
                long a = after.Of(kind);
                if (b == 0 && a == 0) continue;
                components.Add(new ComponentDelta { Component = kind, BeforeBytes = b, AfterBytes = a });
            }

            components.Sort((x, y) => y.SavedBytes.CompareTo(x.SavedBytes));
            return new CompositionDelta(before.TotalBytes, after.TotalBytes, components);
        }
    }
}
