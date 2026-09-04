using System;

namespace Optimus.Core.Performance
{
    /// <summary>What a document contains, from the point of view of interaction cost.</summary>
    public sealed class InteractionInputs
    {
        public long Nodes;
        public int Objects;

        /// <summary>Objects below the page's top level (inside groups or PowerClips).</summary>
        public int NestedObjects;

        /// <summary>Blend, extrude, contour, bevel, drop-shadow, envelope, lens… — regenerated on
        /// every redraw, which is why they can dominate the cost of a drag.</summary>
        public int LiveEffects;

        public int Transparencies;
    }

    /// <summary>Which input dominates the score — it decides what advice to give.</summary>
    public enum InteractionFactor { None, Nodes, Objects, LiveEffects, Nesting, Transparencies }

    public enum InteractionBand { Light, Moderate, Heavy }

    /// <summary>
    /// A score for "how heavy is this document to work with", separate from how big it is (O8).
    ///
    /// <para>
    /// HONESTY BOUNDARY: this is a MODEL, not a measurement. It ranks documents and points at the
    /// dominant cause; it does not claim milliseconds. The actual timing is measured in Phase 4 by
    /// clocking <c>Window.Refresh</c> at a fixed zoom before and after, and the weights below are to
    /// be CALIBRATED against those measurements rather than left as judgement.
    /// </para>
    /// <para>
    /// Weights come from what CorelDRAW must redo per frame: a node is one coordinate to transform
    /// and re-stroke; an object adds its own bookkeeping; a live effect must be REGENERATED, which is
    /// why it is weighted orders of magnitude higher than a plain shape.
    /// </para>
    /// </summary>
    public static class InteractionWeight
    {
        // Cost of one node = 1 (the unit).
        private const double PerObject = 4.0;
        private const double PerNestedObject = 1.0;    // on top of PerObject
        private const double PerLiveEffect = 800.0;    // regenerated every redraw
        private const double PerTransparency = 60.0;   // forces compositing

        private const double ModerateThreshold = 40_000;
        private const double HeavyThreshold = 200_000;

        public static double Compute(InteractionInputs i)
        {
            if (i == null) return 0;
            return Math.Max(0, i.Nodes)
                 + Math.Max(0, i.Objects) * PerObject
                 + Math.Max(0, i.NestedObjects) * PerNestedObject
                 + Math.Max(0, i.LiveEffects) * PerLiveEffect
                 + Math.Max(0, i.Transparencies) * PerTransparency;
        }

        public static InteractionBand Classify(double score)
        {
            if (score >= HeavyThreshold) return InteractionBand.Heavy;
            if (score >= ModerateThreshold) return InteractionBand.Moderate;
            return InteractionBand.Light;
        }

        /// <summary>
        /// The single largest contributor. This is what the UI should tell the operator to attack —
        /// on a file whose weight is three contour groups, "reduce nodes" would be bad advice.
        /// </summary>
        public static InteractionFactor Dominant(InteractionInputs i)
        {
            if (i == null) return InteractionFactor.None;

            double nodes = Math.Max(0, i.Nodes);
            double objects = Math.Max(0, i.Objects) * PerObject;
            double effects = Math.Max(0, i.LiveEffects) * PerLiveEffect;
            double nesting = Math.Max(0, i.NestedObjects) * PerNestedObject;
            double transparency = Math.Max(0, i.Transparencies) * PerTransparency;

            double best = 0;
            InteractionFactor winner = InteractionFactor.None;
            void Consider(double value, InteractionFactor factor)
            {
                if (value > best) { best = value; winner = factor; }
            }

            Consider(nodes, InteractionFactor.Nodes);
            Consider(objects, InteractionFactor.Objects);
            Consider(effects, InteractionFactor.LiveEffects);
            Consider(nesting, InteractionFactor.Nesting);
            Consider(transparency, InteractionFactor.Transparencies);
            return winner;
        }
    }
}
