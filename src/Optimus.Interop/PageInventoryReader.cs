using System;
using System.Collections.Generic;
using System.Diagnostics;
using Optimus.Core.Corel;
using Optimus.Core.Model;

namespace Optimus.Interop
{
    /// <summary>
    /// Reads a page's structural inventory in a HANDFUL of COM calls instead of one per shape.
    ///
    /// <para>
    /// MEASURED on a real 16.901-shape client file (kaneki.cdr):
    /// </para>
    /// <list type="bullet">
    /// <item>Per-shape traversal (<see cref="ShapeWalker"/>): <b>174.119 ms</b> — 1.128 µs per
    /// <c>Shapes[i]</c>, 355 µs per <c>.Type</c>.</item>
    /// <item><c>Page.FindShapes(,, Recursive:=True)</c>: <b>171 ms</b> for the same 16.899 shapes.
    /// A thousand times faster.</item>
    /// </list>
    /// <para>
    /// Two facts from that measurement drive this design. First, <c>FindShapes</c> with
    /// <c>Recursive:=True</c> DOES descend into PowerClips — it returned 16.899 shapes on a document
    /// whose top level holds only ~21, so the 16.880 inside PowerClips are included. (The opposite
    /// was assumed, and documented, until the numbers said otherwise.) Second, <c>FindShapes</c>
    /// takes a TYPE FILTER, so a per-type count is one COM call rather than 16.901 <c>.Type</c>
    /// reads.
    /// </para>
    /// <para>
    /// Node counting is the one thing that still costs per-curve COM calls (~2.6 ms each), so it is
    /// opt-in and reported separately.
    /// </para>
    /// </summary>
    public sealed class PageInventoryReader
    {
        /// <summary><c>cdrCurveShape</c>, kept for the one cross-check documented in <see cref="Read"/>.</summary>
        private const int CurveType = CorelConstants.CdrCurveShape;

        public sealed class Result
        {
            public ShapeInventory Inventory { get; set; } = ShapeInventory.From(new ShapeRecord[0]);

            /// <summary>Curve shapes to act on (populated only when <c>collectCurves</c> is set).</summary>
            public List<object> Curves { get; } = new List<object>();

            /// <summary>Whether node counts in the inventory are exact or were skipped (reported 0).</summary>
            public bool NodesCounted { get; set; }

            /// <summary>
            /// Shapes that are NOT at the page's top level, i.e. inside groups or PowerClips
            /// (recursive total minus top-level count). The bulk reader cannot tell those two apart
            /// — it only counts — so this is reported as "nested", never as "in PowerClip". Claiming
            /// the narrower fact would be a lie the data does not support.
            /// </summary>
            public int Nested { get; set; }

            /// <summary>Shapes no type filter matched. A high value means the reader is blind to
            /// part of the document and its per-type numbers must NOT be trusted.</summary>
            public int Unclassified { get; set; }

            public string Diagnostics { get; set; } = "";
        }

        /// <summary>
        /// Builds the inventory. <paramref name="countNodes"/> enables the exact node count, which
        /// requires iterating the curves (the only per-shape cost left). <paramref name="collectCurves"/>
        /// keeps the live COM curve objects so an optimizer can act on them.
        /// </summary>
        public Result Read(dynamic page, bool countNodes, bool collectCurves)
        {
            var sw = Stopwatch.StartNew();
            var result = new Result { NodesCounted = countNodes };
            var records = new List<ShapeRecord>();
            int comCalls = 0;

            // Top-level count vs recursive total tells us how much art is nested (PowerClips,
            // groups) — the number that exposed the v1.0 blind spot. Two COM calls.
            int topLevel = SafeCount(() => page.Shapes); comCalls++;
            int total = RangeCount(FindShapes(page, null), out object? _ignored); comCalls++;

            // Enumerate the ONE flat range that agrees with the document total, reading each shape's
            // own .Type. The type-FILTER route was abandoned: on a real file, summing
            // FindShapes(type) over ALL 27 enum values yielded 6.118 shapes while the unfiltered
            // FindShapes yielded 16.899 — the same API contradicting itself. Filters see a
            // consistent SUBSET (2.35x fewer curves than the per-shape .Type read finds), so their
            // counts cannot be trusted as the document's composition.
            var byRawType = new SortedDictionary<int, int>();
            int classified = EnumerateFlat(page, countNodes, collectCurves, records, result, byRawType, ref comCalls);
            int unclassified = Math.Max(0, total - classified);
            for (int i = 0; i < unclassified; i++) records.Add(new ShapeRecord(CorelShapeKind.Unknown));

            result.Inventory = ShapeInventory.From(records);
            result.Nested = Math.Max(0, total - topLevel);
            result.Unclassified = unclassified;
            sw.Stop();

            // Per-type counts are logged with the RAW cdrShapeType number alongside our name,
            // because "unclassified=10781" told us the filters were blind but not to WHICH types.
            var perKind = new List<string>();
            foreach (var kv in byRawType)
                perKind.Add($"{kv.Key}:{GeometryClassifier.FromCorelShapeType(kv.Key)}={kv.Value}");

            // ONE cross-check, kept deliberately: the count the type FILTER reports for curves,
            // against the count the per-shape .Type read found. On a real file these disagreed
            // 6.107 vs 14.344 — the filter is not a reliable view of composition. Logging both keeps
            // that fact visible in the field instead of buried in a commit message.
            int filterCurves = RangeCount(FindShapes(page, CurveType), out object? fRange); comCalls++;
            Release(fRange);
            byRawType.TryGetValue(CurveType, out int enumeratedCurves);

            result.Diagnostics =
                $"fast: {sw.ElapsedMilliseconds}ms, {comCalls} bulk COM calls | topLevel={topLevel} " +
                $"totalRecursive={total} nested={result.Nested} classified={classified} " +
                $"unclassified={unclassified} nodesCounted={countNodes} | " +
                $"curves: enumerated={enumeratedCurves} vs filter={filterCurves} | kinds: {string.Join(", ", perKind)}";
            return result;
        }

        /// <summary>
        /// Single pass over the flat recursive range, reading each shape's own <c>.Type</c>.
        ///
        /// <para>
        /// Every COM object touched is RELEASED explicitly. That is not tidiness — it is the fix for
        /// the largest cost in the old per-shape walk: 110 s of its 174 s appeared in NO per-call
        /// measurement, because it was RCW cleanup. An STA COM object's <c>Release</c> has to run on
        /// the owning thread (ours), so leaving ~46.000 wrappers to the garbage collector blocks the
        /// very thread doing the work, at a moment nobody is measuring.
        /// </para>
        /// </summary>
        private static int EnumerateFlat(
            dynamic page, bool countNodes, bool collectCurves,
            List<ShapeRecord> records, Result result,
            SortedDictionary<int, int> byRawType, ref int comCalls)
        {
            object? rangeObj = FindShapes(page, null); comCalls++;
            if (rangeObj == null) return 0;

            int count = RangeCount(rangeObj, out object? _);
            dynamic range = rangeObj;
            int seen = 0;

            for (int i = 1; i <= count; i++)
            {
                object? shape = null;
                try { shape = range[i]; } catch { continue; }
                if (shape == null) continue;

                bool retained = false;
                try
                {
                    int rawType = -1;
                    try { rawType = (int)((dynamic)shape).Type; } catch { }

                    byRawType.TryGetValue(rawType, out int n);
                    byRawType[rawType] = n + 1;

                    CorelShapeKind kind = GeometryClassifier.FromCorelShapeType(rawType);
                    int nodes = 0;
                    if (kind == CorelShapeKind.Curve && countNodes)
                        nodes = SafeNodeCount(shape);

                    records.Add(new ShapeRecord(kind, nodes));
                    seen++;

                    if (collectCurves && kind == CorelShapeKind.Curve && (!countNodes || nodes > 0))
                    {
                        result.Curves.Add(shape);
                        retained = true;   // the optimizer will act on it; must NOT be released here
                    }
                }
                finally
                {
                    if (!retained) Release(shape);
                }
            }

            Release(rangeObj);
            return seen;
        }

        /// <summary>Node count for one curve. Releases the intermediate Curve/Nodes wrappers.</summary>
        private static int SafeNodeCount(object shape)
        {
            object? curve = null, nodes = null;
            try
            {
                curve = ((dynamic)shape).Curve;
                if (curve == null) return 0;
                nodes = ((dynamic)curve).Nodes;
                if (nodes == null) return 0;
                return (int)((dynamic)nodes).Count;
            }
            catch { return 0; }
            finally { Release(nodes); Release(curve); }
        }

        /// <summary>Drops a COM wrapper immediately instead of leaving it to the finalizer.</summary>
        private static void Release(object? comObject)
        {
            if (comObject == null) return;
            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(comObject))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(comObject);
            }
            catch { /* already released / not a COM object */ }
        }

        /// <summary>
        /// <c>Page.FindShapes(Name?, Type?, Recursive?)</c> — verified in the typelib. Recursive is
        /// always true: it is what reaches into groups AND PowerClips in a single call.
        /// </summary>
        private static object? FindShapes(dynamic page, int? cdrType)
        {
            try
            {
                return cdrType.HasValue
                    ? page.FindShapes(Type.Missing, cdrType.Value, true)
                    : page.FindShapes(Type.Missing, Type.Missing, true);
            }
            catch { return null; }
        }

        private static int RangeCount(object? range, out object? live)
        {
            live = range;
            if (range == null) return 0;
            try { return (int)((dynamic)range).Count; }
            catch { return 0; }
        }

        private static int SafeCount(Func<object> get)
        {
            try { return (int)((dynamic)get()).Count; }
            catch { return 0; }
        }
    }
}
