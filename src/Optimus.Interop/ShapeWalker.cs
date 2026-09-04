using System;
using System.Collections.Generic;
using System.Diagnostics;
using Optimus.Core.Model;

namespace Optimus.Interop
{
    /// <summary>
    /// Per-shape walk of a CorelDRAW shape collection, producing a <see cref="ShapeInventory"/>.
    /// STRICTLY READ-ONLY: it must leave the document byte-identical (Phase 1, R1.7).
    ///
    /// <para>
    /// ⚠ NOT the primary path for a page — use <see cref="PageInventoryReader"/>. This walk was
    /// MEASURED at <b>174.119 ms</b> on a 16.901-shape client file (1.128 µs per <c>Shapes[i]</c>,
    /// 355 µs per <c>.Type</c>), while <c>Page.FindShapes(,, True)</c> returned the same shapes in
    /// <b>171 ms</b>. It survives only for scopes FindShapes cannot address — notably the current
    /// selection — and as a cross-check oracle for the bulk reader.
    /// </para>
    /// <para>
    /// CORRECTION (measured, superseding an earlier assumption in this file):
    /// <c>FindShapes(Recursive:=True)</c> <b>does</b> descend into PowerClips. On the file above it
    /// returned 16.899 shapes from a page whose top level holds ~21, so the 16.880 living inside
    /// PowerClips were included. The belief that it did not is what justified this manual walk.
    /// </para>
    /// <para>
    /// What this walk still does that the bulk reader cannot: it knows WHERE each shape was found
    /// (inside a PowerClip or not) because it recurses <c>Shape.PowerClip.Shapes</c> itself, and it
    /// classifies every shape rather than only the filtered types.
    /// </para>
    /// </summary>
    public sealed class ShapeWalker
    {
        /// <summary>Depth cap: protects against a pathological/cyclic document structure.</summary>
        private const int MaxDepth = 64;

        /// <summary>Per-category COM cost of the last walk. Logged so slowness is diagnosed, not guessed.</summary>
        public WalkStats Stats { get; private set; } = new WalkStats();

        /// <summary>
        /// When false, node counts are skipped. <c>Curve.Nodes.Count</c> is the prime suspect for
        /// the ~5 min walk observed on a 16.900-shape document; turning it off gives a fast
        /// structural scan and, by comparison, proves whether it is the culprit.
        /// </summary>
        public bool CountNodes { get; set; } = true;

        /// <summary>The inventory plus the live COM shapes that carry reducible geometry.</summary>
        public sealed class WalkResult
        {
            public WalkResult(ShapeInventory inventory, List<object> reducibleShapes)
            {
                Inventory = inventory;
                ReducibleShapes = reducibleShapes;
            }

            public ShapeInventory Inventory { get; }

            /// <summary>
            /// Curve shapes found anywhere in the document — including inside groups, effect groups
            /// AND PowerClips. This list is what an optimizer must act on; v1.0 only ever saw the
            /// top level, which is why it changed nothing.
            /// </summary>
            public List<object> ReducibleShapes { get; }
        }

        /// <summary>Walks a page. Never throws for a malformed shape. Read-only.</summary>
        public WalkResult WalkPage(dynamic page)
        {
            CaptureThread();
            var records = new List<ShapeRecord>();
            var reducible = new List<object>();
            try { Collect(page.Shapes, records, reducible, inPowerClip: false, depth: 0); }
            catch (Exception) { /* a broken page yields what we managed to read */ }
            return new WalkResult(ShapeInventory.From(records), reducible);
        }

        /// <summary>
        /// Records which thread/apartment the walk ran on. If COM calls cost ~1 ms each, they are
        /// crossing an apartment boundary — in-process same-apartment calls are ~100x cheaper.
        /// This is the difference between "the document is big" and "we are calling it wrong".
        /// </summary>
        private void CaptureThread()
        {
            Stats.ThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            Stats.Apartment = System.Threading.Thread.CurrentThread.GetApartmentState().ToString();
        }

        /// <summary>
        /// One-shot probe of the bulk alternative: <c>Page.FindShapes(,, Recursive)</c> returns the
        /// whole page as a flat range in a SINGLE COM call, instead of thousands of indexed
        /// <c>Shapes[i]</c> fetches (which may be O(n) each, making the walk quadratic).
        ///
        /// <para>Diagnostic only — it reports the count and the cost so we can decide, with data,
        /// whether to rebuild the traversal on top of it. It also tells us whether FindShapes
        /// descends into PowerClips, which no documentation confirms.</para>
        /// </summary>
        public string ProbeFindShapes(dynamic page)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                dynamic range = page.FindShapes(System.Type.Missing, System.Type.Missing, true);
                int n = range == null ? -1 : (int)range.Count;
                sw.Stop();
                return $"FindShapes(recursive)={n} shapes in {sw.ElapsedMilliseconds}ms";
            }
            catch (Exception ex)
            {
                sw.Stop();
                return $"FindShapes FAILED after {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}";
            }
        }

        /// <summary>Walks an arbitrary shape collection (e.g. the current selection).</summary>
        public WalkResult WalkShapes(dynamic shapes)
        {
            CaptureThread();
            var records = new List<ShapeRecord>();
            var reducible = new List<object>();
            try { Collect(shapes, records, reducible, inPowerClip: false, depth: 0); }
            catch (Exception) { }
            return new WalkResult(ShapeInventory.From(records), reducible);
        }

        private void Collect(dynamic shapes, List<ShapeRecord> records, List<object> reducible, bool inPowerClip, int depth)
        {
            if (depth > MaxDepth) return;

            int count;
            try { count = (int)shapes.Count; }
            catch { return; }

            // VGCore collections are base-1. A foreach over the raw COM collection does NOT work
            // (it is not IEnumerable<dynamic> and throws InvalidCastException).
            for (int i = 1; i <= count; i++)
            {
                object? shape = null;
                long t0 = Stats.Ticks;
                try { shape = (object)shapes[i]; }
                catch { continue; }
                Stats.ItemFetches++;
                Stats.ItemTicks += Stats.Ticks - t0;
                if (shape == null) continue;

                // A shape kept as "reducible" is handed to the caller, which will edit and release it;
                // everything else is released HERE. Without this the walk leaves one RCW per shape for
                // the finalizer, and an STA Release runs on the owning thread and blocks it: measured on
                // a real selection walk at 211.624 ms total against 79.031 ms accounted — 62% of the
                // time was cleanup of objects nobody had freed (O9).
                bool retained = false;
                try { retained = CollectShape(shape, records, reducible, inPowerClip, depth); }
                finally { if (!retained) Release(shape); }
            }
        }

        /// <summary>
        /// Releases a COM object explicitly. Never throws: a shape the host already freed must not abort
        /// a walk that is otherwise complete.
        /// </summary>
        private static void Release(object? o)
        {
            try
            {
                if (o != null && System.Runtime.InteropServices.Marshal.IsComObject(o))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(o);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Records one shape and descends into it. Returns TRUE when the shape was kept in
        /// <paramref name="reducible"/> — the caller must then NOT release it, because ownership passed
        /// to whoever will edit it.
        /// </summary>
        private bool CollectShape(object shapeObj, List<ShapeRecord> records, List<object> reducible, bool inPowerClip, int depth)
        {
            dynamic shape = shapeObj;
            CorelShapeKind kind = GeometryClassifier.FromCorelShapeType(SafeShapeType(shape));

            // Record the shape ONCE, whatever it is.
            int nodes = SafeNodeCount(shape, kind);
            records.Add(new ShapeRecord(kind, nodes, inPowerClip));

            bool retained = false;
            if (GeometryClassifier.HasReducibleGeometry(kind) && nodes > 0)
            {
                reducible.Add(shapeObj);
                retained = true;
            }

            // Descend into children when the shape can hold art. Two families:
            //  • IsContainer  — groups and effect groups (formally have .Shapes)
            //  • MayContainArt — symbols, custom/perfect shapes and anything unmapped. A symbol
            //    instance wrapping a bitmap would otherwise be reported as "0 images", exactly the
            //    kind of silent miss that made v1.0 useless.
            if (GeometryClassifier.IsContainer(kind) || GeometryClassifier.MayContainArt(kind))
            {
                object? children = TryGetChildren(shape);
                if (children != null)
                {
                    try { Collect(children, records, reducible, inPowerClip, depth + 1); }
                    finally { Release(children); }
                }
            }

            // A shape of ANY kind can host a PowerClip. Its contents are a separate collection that
            // the ordinary walk never reaches — this is the v1.0 blind spot.
            object? powerClipShapes = TryGetPowerClipShapes(shape);
            if (powerClipShapes != null)
            {
                try { Collect(powerClipShapes, records, reducible, inPowerClip: true, depth: depth + 1); }
                finally { Release(powerClipShapes); }
            }

            return retained;
        }

        private int SafeShapeType(dynamic shape)
        {
            long t0 = Stats.Ticks;
            try { return (int)shape.Type; }
            catch { return -1; }
            finally { Stats.TypeReads++; Stats.TypeTicks += Stats.Ticks - t0; }
        }

        /// <summary>Node count for curve shapes. Read-only; 0 for anything else.</summary>
        private int SafeNodeCount(dynamic shape, CorelShapeKind kind)
        {
            if (!GeometryClassifier.HasReducibleGeometry(kind)) return 0;
            if (!CountNodes) return 0;   // fast scan: skip the (suspected) expensive call

            long t0 = Stats.Ticks;
            try
            {
                dynamic curve = shape.Curve;
                if (curve == null) return 0;
                return (int)curve.Nodes.Count;
            }
            catch { return 0; }
            finally { Stats.NodeCounts++; Stats.NodeTicks += Stats.Ticks - t0; }
        }

        private object? TryGetChildren(dynamic shape)
        {
            long t0 = Stats.Ticks;
            Stats.ChildProbes++;
            try
            {
                object? s = shape.Shapes;
                if (s != null && Count(s) > 0) { Stats.ChildHits++; return s; }
            }
            catch { Stats.ChildThrows++; }
            finally { Stats.ChildTicks += Stats.Ticks - t0; }
            return null;
        }

        /// <summary>Base-1 COM collection count, late-bound.</summary>
        private static int Count(object collection)
        {
            dynamic c = collection;
            return (int)c.Count;
        }

        /// <summary>
        /// Returns the PowerClip's contents collection, or null when the shape has no PowerClip.
        /// <c>Shape.PowerClip</c> and <c>PowerClip.Shapes</c> are both verified in the typelib.
        /// </summary>
        private object? TryGetPowerClipShapes(dynamic shape)
        {
            long t0 = Stats.Ticks;
            Stats.PowerClipProbes++;
            try
            {
                object? pc = shape.PowerClip;
                if (pc == null) return null;
                object? s = ((dynamic)pc).Shapes;
                if (s != null && Count(s) > 0) { Stats.PowerClipHits++; return s; }
            }
            catch { Stats.PowerClipThrows++; }
            finally { Stats.PowerClipTicks += Stats.Ticks - t0; }
            return null;
        }
    }
}
