using System.Diagnostics;

namespace Optimus.Interop
{
    /// <summary>
    /// Per-category cost accounting for a document walk.
    ///
    /// <para>
    /// Exists because the first real-world run took ~5 minutes for 16.900 shapes (~18 ms/shape),
    /// which is orders of magnitude slower than in-process COM should be. Rather than guess which
    /// call is expensive, we measure each category and let the numbers decide. The two suspects are
    /// (a) probing <c>Shape.PowerClip</c> on every shape, where an absent PowerClip may raise a COM
    /// exception — exceptions as control flow are brutally slow — and (b) <c>Curve.Nodes.Count</c>,
    /// which may materialize the whole node collection just to read a count.
    /// </para>
    /// </summary>
    public sealed class WalkStats
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public int TypeReads;
        public long TypeTicks;

        public int NodeCounts;
        public long NodeTicks;

        public int PowerClipProbes;
        public int PowerClipHits;
        public int PowerClipThrows;
        public long PowerClipTicks;

        public int ChildProbes;
        public int ChildHits;
        public int ChildThrows;
        public long ChildTicks;

        public int ItemFetches;
        public long ItemTicks;

        /// <summary>Managed thread the walk ran on, and its COM apartment. If this is NOT the
        /// thread CorelDRAW loaded the addon on, every COM call is crossing apartments — which
        /// costs ~1 ms each and would explain a multi-minute walk on its own.</summary>
        public int ThreadId;
        public string Apartment = "?";

        /// <summary>
        /// Ticks, not milliseconds. The first instrumented run measured 71 s across categories out
        /// of a 196 s walk — the missing 64% was quantization: <c>ElapsedMilliseconds</c> floors
        /// every sub-millisecond call to 0, and nearly every COM property read is sub-millisecond.
        /// Ticks keep sub-ms cost visible.
        /// </summary>
        public long Ticks => _clock.ElapsedTicks;

        public long TotalMs => _clock.ElapsedMilliseconds;

        private static long ToMs(long ticks) => ticks * 1000 / Stopwatch.Frequency;

        /// <summary>Single-line summary for docker.log — this is what tells us where the time went.
        /// Per-call microseconds are included because the per-call cost is the diagnosis: ~1000 µs
        /// per COM property read means apartment/process marshalling, ~10 µs means in-process.</summary>
        public override string ToString()
        {
            string Cat(string name, int n, long ticks) =>
                n == 0 ? $"{name}=0" : $"{name}={n}/{ToMs(ticks)}ms({ticks * 1000000 / Stopwatch.Frequency / n}µs ea)";

            long accounted = ItemTicks + TypeTicks + NodeTicks + PowerClipTicks + ChildTicks;
            return $"thread={ThreadId}/{Apartment} | total={TotalMs}ms | accounted={ToMs(accounted)}ms | " +
                   $"{Cat("item", ItemFetches, ItemTicks)} | {Cat("type", TypeReads, TypeTicks)} | " +
                   $"{Cat("nodes", NodeCounts, NodeTicks)} | " +
                   $"{Cat("powerClip", PowerClipProbes, PowerClipTicks)}(hit {PowerClipHits}, throw {PowerClipThrows}) | " +
                   $"{Cat("children", ChildProbes, ChildTicks)}(hit {ChildHits}, throw {ChildThrows})";
        }
    }
}
