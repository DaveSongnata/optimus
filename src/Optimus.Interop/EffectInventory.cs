using System;
using System.Collections.Generic;
using Optimus.Core.Corel;
using Optimus.Core.Performance;

namespace Optimus.Interop
{
    /// <summary>
    /// Finds the LIVE effects in a document and, on request, flattens them.
    ///
    /// <para>
    /// Live effects are regenerated on every redraw, so a handful of them can cost more of a drag than
    /// hundreds of thousands of nodes. Flattening keeps the APPEARANCE and destroys the EDITABILITY,
    /// which is why it is strictly opt-in and every kind carries its own plain-language cost
    /// (<see cref="EffectCost.FlattenCost"/>).
    /// </para>
    /// <para>
    /// Verified members: <c>Shape.Effects</c> (dump 4341/6168), <c>Shape.FlattenEffects()</c> (6094),
    /// <c>Shape.ClearEffect(cdrEffectType)</c> (6215), <c>Shape.Transparency</c> (6205).
    /// </para>
    /// </summary>
    public sealed class EffectInventory
    {
        /// <summary>Effects found, plus the transparency count (which also forces compositing).</summary>
        public sealed class Result
        {
            public List<EffectRecord> Effects { get; } = new List<EffectRecord>();

            /// <summary>Shapes carrying transparency — each one forces a compositing pass.</summary>
            public int Transparencies { get; set; }

            /// <summary>Live COM shapes that own an effect, so flattening can act on exactly those.</summary>
            public List<object> EffectShapes { get; } = new List<object>();

            public int TotalCost => EffectCost.TotalCost(Effects);
        }

        /// <summary>
        /// Read-only scan of the page. Effect-group shapes are identified by their shape TYPE (which
        /// the bulk page reader already gives us cheaply) and refined by asking the shape for its
        /// effect list when available.
        /// </summary>
        public Result Scan(dynamic page)
        {
            var result = new Result();
            object? range = FindAll(page);
            if (range == null) return result;

            int count;
            try { count = (int)((dynamic)range).Count; }
            catch { return result; }

            dynamic shapes = range;
            for (int i = 1; i <= count; i++)
            {
                object? shape = null;
                try { shape = shapes[i]; } catch { continue; }
                if (shape == null) continue;

                bool retained = false;
                try
                {
                    LiveEffectKind kind = KindFromShapeType(SafeType(shape));
                    if (kind != LiveEffectKind.Unknown)
                    {
                        result.Effects.Add(new EffectRecord
                        {
                            Kind = kind,
                            GeneratedShapes = CountChildren(shape),
                        });
                        result.EffectShapes.Add(shape);
                        retained = true;
                    }

                    if (HasTransparency(shape)) result.Transparencies++;
                }
                finally
                {
                    if (!retained) Release(shape);
                }
            }

            Release(range);
            return result;
        }

        /// <summary>
        /// Flattens the given effect shapes. Returns how many were flattened. Never throws for one bad
        /// shape — a single failure must not abandon the rest.
        /// </summary>
        public int Flatten(IEnumerable<object> effectShapes)
        {
            int flattened = 0;
            if (effectShapes == null) return 0;

            foreach (object shape in effectShapes)
            {
                try
                {
                    ((dynamic)shape).FlattenEffects();
                    flattened++;
                }
                catch { /* this shape refuses to flatten; the others still can */ }
            }
            return flattened;
        }

        /// <summary>Maps a <c>cdrShapeType</c> effect group onto our effect kind.</summary>
        private static LiveEffectKind KindFromShapeType(int cdrShapeType)
        {
            switch (cdrShapeType)
            {
                case CorelConstants.CdrBlendGroupShape: return LiveEffectKind.Blend;
                case CorelConstants.CdrExtrudeGroupShape: return LiveEffectKind.Extrude;
                case CorelConstants.CdrContourGroupShape: return LiveEffectKind.Contour;
                case CorelConstants.CdrDropShadowGroupShape: return LiveEffectKind.DropShadow;
                case CorelConstants.CdrBevelGroupShape: return LiveEffectKind.CustomEffect;
                case CorelConstants.CdrCustomEffectGroupShape: return LiveEffectKind.CustomEffect;
                default: return LiveEffectKind.Unknown;
            }
        }

        private static object? FindAll(dynamic page)
        {
            try { return page.FindShapes(Type.Missing, Type.Missing, true); }
            catch { return null; }
        }

        private static int SafeType(object shape)
        {
            try { return (int)((dynamic)shape).Type; }
            catch { return -1; }
        }

        /// <summary>Shapes an effect generates — real work on every frame, so it adds to the cost.</summary>
        private static int CountChildren(object shape)
        {
            try
            {
                object? children = ((dynamic)shape).Shapes;
                if (children == null) return 0;
                return (int)((dynamic)children).Count;
            }
            catch { return 0; }
        }

        private static bool HasTransparency(object shape)
        {
            try
            {
                object? t = ((dynamic)shape).Transparency;
                if (t == null) return false;
                // Type 0 means "no transparency" in the VGCore transparency enum.
                return (int)((dynamic)t).Type != 0;
            }
            catch { return false; }
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
