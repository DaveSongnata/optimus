using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Optimus.Core.Diagnostics
{
    /// <summary>Where the object payload's bytes actually go, measured both logically and on disk.</summary>
    public sealed class PayloadBreakdown
    {
        /// <summary>Bézier/shape coordinates (<c>loda</c> argType <c>0x1e</c>).</summary>
        public long GeometryLogical { get; set; }
        public long GeometryCompressed { get; set; }

        /// <summary>
        /// Verbose ASCII-JSON style state that CorelDRAW rewrites for EVERY object
        /// (<c>loda</c> argType <c>0xc9</c>). ~748 bytes per object, with only a handful of distinct
        /// values — i.e. the same fill/outline description duplicated thousands of times.
        /// </summary>
        public long StyleJsonLogical { get; set; }
        public long StyleJsonCompressed { get; set; }

        /// <summary>Everything else in the object records.</summary>
        public long OtherLogical { get; set; }
        public long OtherCompressed { get; set; }

        public int ObjectRecords { get; set; }
        public int Malformed { get; set; }

        public long TotalLogical => GeometryLogical + StyleJsonLogical + OtherLogical;
        public long TotalCompressed => GeometryCompressed + StyleJsonCompressed + OtherCompressed;

        public double PercentOfCompressed(long part) =>
            TotalCompressed <= 0 ? 0 : Math.Round(part * 100.0 / TotalCompressed, 1);

        /// <summary>
        /// Style JSON per object. This is the number that would say whether reducing the OBJECT COUNT
        /// is a lever — every object pays this toll regardless of how simple it is.
        ///
        /// <para>
        /// MEASURED VERDICT: it is NOT a lever. On a real file this is ~743 logical bytes per object
        /// over 16.948 objects (12.0 MB — more than the geometry), yet it deflates to 125 KB, i.e.
        /// 2.1% of the payload's real cost, because it is a handful of distinct strings repeated
        /// thousands of times. The container already solves it.
        /// </para>
        /// </summary>
        public double StyleBytesPerObject =>
            ObjectRecords <= 0 ? 0 : Math.Round(StyleJsonLogical / (double)ObjectRecords, 0);

        /// <summary>
        /// Geometry's share of the FILE, given the file's total size. This is the number that decides
        /// how much node reduction is worth — and it is FILE-DEPENDENT: ~97% of the object payload
        /// (≈73% of the file) on a geometry-dominated drawing, versus a rounding error on an
        /// ICC-dominated one.
        /// </summary>
        public double GeometryShareOfFile(long fileBytes) =>
            fileBytes <= 0 ? 0 : Math.Round(GeometryCompressed * 100.0 / fileBytes, 1);

        // ── repeated geometry ────────────────────────────────────────────────────────

        /// <summary>Shapes whose coordinate block is byte-identical to another shape's.</summary>
        public int RepeatedShapes { get; set; }

        /// <summary>Distinct coordinate blocks — how many shapes the drawing really needs to store.</summary>
        public int DistinctGeometries { get; set; }

        /// <summary>
        /// Logical bytes that would survive if every repeated block were stored once.
        /// </summary>
        public long DistinctGeometryLogical { get; set; }

        /// <summary>
        /// The same, deflated — the honest measure of what de-duplicating would save on disk (O11).
        /// </summary>
        public long DistinctGeometryCompressed { get; set; }

        /// <summary>
        /// Bytes recoverable by storing each distinct coordinate block once.
        ///
        /// <para>
        /// This is measured, never assumed, and it is measured COMPRESSED because deflate may already
        /// have erased the duplication for free — which is exactly what happens to the per-object style
        /// JSON (O8). It does NOT happen to geometry when the copies sit megabytes apart: deflate's
        /// window is 32 KB, and a 28 MB stream puts repeated art far outside it.
        /// </para>
        /// </summary>
        public long RepetitionSavingBytes =>
            Math.Max(0, GeometryCompressed - DistinctGeometryCompressed);

        public double RepetitionSavingShareOfFile(long fileBytes) =>
            fileBytes <= 0 ? 0 : Math.Round(RepetitionSavingBytes * 100.0 / fileBytes, 1);

        /// <summary>Share of shapes that are a repeat of another shape's coordinates.</summary>
        public double RepeatedSharePercent =>
            ObjectRecords <= 0 ? 0 : Math.Round(RepeatedShapes * 100.0 / ObjectRecords, 1);
    }

    /// <summary>
    /// Explains WHY a vector-dominated .cdr is big, one level below the container.
    ///
    /// <para>
    /// The container view says "89% is object payload", which is true and useless — it does not say
    /// what to do. This splits that payload into geometry versus CorelDRAW's redundant per-object
    /// style description, and measures each one's REAL compressed cost by deflating them separately.
    /// That distinction decides the advice: if the duplication survives compression, reducing the
    /// number of objects is a real lever; if deflate already erases it, the only honest lever is
    /// geometry (which is worth ~4% of the file — O8) and the file is near its floor.
    /// </para>
    /// <para>
    /// Measuring instead of extrapolating matters here: logical shares are ~41% geometry / ~52% style
    /// JSON, but text of a few distinct values compresses far better than coordinates, so the on-disk
    /// split can be nothing like the logical one.
    /// </para>
    /// </summary>
    public sealed class ObjectPayloadAnalyzer
    {
        /// <summary><c>loda</c> argType carrying shape coordinates.</summary>
        private const int ArgGeometry = 0x1e;

        /// <summary><c>loda</c> argType carrying the duplicated ASCII-JSON style state.</summary>
        private const int ArgStyleJson = 0xc9;

        /// <summary>Sanity cap on an object's argument count; more means we desynced.</summary>
        private const int MaxArgs = 64;

        /// <summary>
        /// <paramref name="dataStreams"/> must be in the order declared by
        /// <c>content/dataFileList.dat</c> — NOT ZIP entry order, which differs in real files.
        /// </summary>
        public PayloadBreakdown Analyze(byte[] rootDat, IList<byte[]> dataStreams)
        {
            var result = new PayloadBreakdown();
            if (rootDat == null || dataStreams == null) return result;

            var reader = new RiffPointerReader();
            reader.Read(rootDat);
            result.Malformed = reader.Malformed;

            // Each shape's coordinate block, kept separately so identical art can be counted.
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            using (var geometry = new MemoryStream())
            using (var styleJson = new MemoryStream())
            using (var other = new MemoryStream())
            using (var distinct = new MemoryStream())
            {
                foreach (ChunkPointer ptr in reader.Pointers)
                {
                    if (ptr.ChunkId != "loda" || ptr.Inline) continue;
                    if (ptr.StreamNumber >= dataStreams.Count) { result.Malformed++; continue; }

                    byte[] stream = dataStreams[ptr.StreamNumber];
                    if (stream == null) { result.Malformed++; continue; }

                    long geometryBefore = geometry.Length;
                    if (!SplitArgs(stream, ptr, geometry, styleJson, other)) { result.Malformed++; continue; }
                    result.ObjectRecords++;

                    // Census of repeated art: hash exactly the bytes this shape contributed.
                    int written = (int)(geometry.Length - geometryBefore);
                    if (written <= 0) continue;

                    byte[] buffer = geometry.GetBuffer();
                    string key = Fingerprint(buffer, (int)geometryBefore, written);

                    if (seen.ContainsKey(key)) { seen[key]++; result.RepeatedShapes++; }
                    else
                    {
                        seen[key] = 1;
                        distinct.Write(buffer, (int)geometryBefore, written);
                    }
                }

                result.GeometryLogical = geometry.Length;
                result.StyleJsonLogical = styleJson.Length;
                result.OtherLogical = other.Length;

                result.GeometryCompressed = DeflatedSize(geometry);
                result.StyleJsonCompressed = DeflatedSize(styleJson);
                result.OtherCompressed = DeflatedSize(other);

                result.DistinctGeometries = seen.Count;
                result.DistinctGeometryLogical = distinct.Length;
                result.DistinctGeometryCompressed = DeflatedSize(distinct);
            }

            return result;
        }

        /// <summary>
        /// Content fingerprint of one shape's coordinate block.
        ///
        /// <para>
        /// FNV-1a over the raw bytes, with the length mixed in. It answers one question only — "is this
        /// shape's geometry byte-identical to one already seen?" — so a fast non-cryptographic hash is
        /// the right tool; a collision would overstate repetition, and the length guard plus the 64-bit
        /// width make that vanishingly unlikely at these object counts.
        /// </para>
        /// </summary>
        private static string Fingerprint(byte[] buffer, int offset, int count)
        {
            const ulong offsetBasis = 14695981039346656037;
            const ulong prime = 1099511628211;

            ulong hash = offsetBasis;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                hash ^= buffer[i];
                hash *= prime;
            }

            return count.ToString() + ":" + hash.ToString("x16");
        }

        /// <summary>
        /// Splits one <c>loda</c> record's arguments by type.
        ///
        /// <para>
        /// Header: <c>u32 chunkLength, numOfArgs, startOfArgs, startOfArgTypes, chunkType</c>. The
        /// TYPE array is stored in REVERSE order relative to the offset array — get that backwards
        /// and every argument is mislabelled while still parsing "successfully".
        /// </para>
        /// </summary>
        private static bool SplitArgs(
            byte[] s, ChunkPointer ptr, Stream geometry, Stream styleJson, Stream other)
        {
            int b = ptr.StreamOffset;
            int len = ptr.PayloadLength;
            if (b < 0 || len < 20 || (long)b + len > s.Length) return false;

            int numArgs = (int)ReadU32(s, b + 4);
            int startArgs = (int)ReadU32(s, b + 8);
            int startTypes = (int)ReadU32(s, b + 12);
            if (numArgs <= 0 || numArgs > MaxArgs) return false;
            if ((long)b + startArgs + 4L * numArgs > s.Length) return false;
            if ((long)b + startTypes + 4L * numArgs > s.Length) return false;

            var offsets = new int[numArgs];
            var types = new int[numArgs];
            for (int i = 0; i < numArgs; i++) offsets[i] = (int)ReadU32(s, b + startArgs + 4 * i);
            for (int i = 0; i < numArgs; i++) types[numArgs - 1 - i] = (int)ReadU32(s, b + startTypes + 4 * i);

            for (int i = 0; i < numArgs; i++)
            {
                int from = b + offsets[i];
                int to = i + 1 < numArgs ? b + offsets[i + 1] : b + len;
                int n = to - from;
                if (n <= 0 || from < b || to > s.Length) continue;

                Stream target = types[i] == ArgGeometry ? geometry
                              : types[i] == ArgStyleJson ? styleJson
                              : other;
                target.Write(s, from, n);
            }
            return true;
        }

        /// <summary>Deflated size of a buffer — the real cost it would carry inside the container.</summary>
        private static long DeflatedSize(MemoryStream data)
        {
            if (data.Length == 0) return 0;
            byte[] raw = data.ToArray();
            using (var sink = new MemoryStream())
            {
                using (var deflate = new DeflateStream(sink, CompressionLevel.Optimal, true))
                    deflate.Write(raw, 0, raw.Length);
                return sink.Length;
            }
        }

        private static uint ReadU32(byte[] b, int offset) =>
            (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));
    }
}
