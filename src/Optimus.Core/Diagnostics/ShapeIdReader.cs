using System;
using System.Collections.Generic;

namespace Optimus.Core.Diagnostics
{
    /// <summary>One shape as the FILE describes it: its id and its geometry fingerprint.</summary>
    public sealed class FileShape
    {
        /// <summary>Value stored in the shape's <c>spid</c> record — candidate for <c>Shape.StaticID</c>.</summary>
        public uint Id { get; set; }

        /// <summary>Fingerprint of its coordinate block, or empty when it has none.</summary>
        public string GeometryKey { get; set; } = "";
    }

    public sealed class ShapeIdCensus
    {
        public List<FileShape> Shapes { get; } = new List<FileShape>();

        /// <summary>Ids seen more than once — if any, the id is NOT a usable key.</summary>
        public int DuplicateIds { get; set; }

        public int ShapesWithGeometry { get; set; }

        /// <summary>Distinct coordinate blocks among the shapes that have one.</summary>
        public int DistinctGeometries { get; set; }

        /// <summary>Shapes that actually carry an id.</summary>
        public int ShapesWithId { get; set; }

        /// <summary>Share of shapes that carry an id at all.</summary>
        public double IdCoveragePercent =>
            Shapes.Count <= 0 ? 0 : Math.Round(ShapesWithId * 100.0 / Shapes.Count, 1);

        /// <summary>
        /// True when the identifier can actually bridge the file census to live COM shapes.
        ///
        /// <para>
        /// Requires ids to be distinct AND present on essentially every shape. The second condition is
        /// the one that fails in practice: CorelDRAW allocates the static id <b>lazily</b>, only for
        /// shapes something has asked about. Measured on the file that motivates the whole feature:
        /// <b>16 ids for 16.914 shapes — 0,1% coverage</b>. A join over 0,1% of the document is not a
        /// bridge, and calling it one would be the same self-deception as reading UUID prefixes.
        /// </para>
        /// </summary>
        public bool IdsAreUnique =>
            Shapes.Count > 0 && DuplicateIds == 0 && IdCoveragePercent >= 95;

        public string Note { get; set; } = "";
    }

    /// <summary>
    /// Reads each shape's <c>spid</c> record from a <c>.cdr</c>, alongside its geometry fingerprint.
    ///
    /// <para>
    /// WHY THIS EXISTS. The file census proves exactly which shapes carry identical art (O15), but it
    /// works on disk records, while instancing them has to happen through COM. Late-bound
    /// <c>Curve.GetCurveInfo()</c> — the obvious bridge — is refused by CorelDRAW 2024 with
    /// <i>"The specified record cannot be mapped to a managed value class"</i>, because the CLR has no
    /// managed type for the <c>CurveElement</c> struct and no interop assembly is shipped (deliberately:
    /// one binary must serve 2024/25/26).
    /// </para>
    /// <para>
    /// So the bridge is an IDENTIFIER instead of coordinates. Every shape in <c>root.dat</c> carries a
    /// <c>spid</c> chunk — measured at 16.839 of them on a real 16.899-shape drawing — and the COM API
    /// exposes both <c>Shape.StaticID</c> and <c>Page.FindShape(…, StaticID, …)</c>. If the value stored
    /// in <c>spid</c> is that same id, the exact grouping computed from disk can be applied to live
    /// shapes without ever asking CorelDRAW for a coordinate.
    /// </para>
    /// <para>
    /// This class only MEASURES that premise. It says whether the ids are unique; it does not assume the
    /// mapping works.
    /// </para>
    /// </summary>
    public sealed class ShapeIdReader
    {
        /// <summary><c>loda</c> argType carrying shape coordinates — same constant the payload analyzer uses.</summary>
        private const int ArgGeometry = 0x1e;

        private const int MaxArgs = 64;

        /// <summary>
        /// Pairs each shape's id with its geometry. <paramref name="dataStreams"/> must be in the order
        /// declared by <c>content/dataFileList.dat</c>.
        /// </summary>
        public ShapeIdCensus Read(byte[] rootDat, IList<byte[]> dataStreams)
        {
            var census = new ShapeIdCensus();
            if (rootDat == null || dataStreams == null) return census;

            var reader = new RiffPointerReader();
            reader.Read(rootDat);

            // Pointers arrive in document order, and a shape's records are adjacent: the spid and the
            // loda of one object sit in the same LIST. Walking the flat sequence and pairing each spid
            // with the NEXT loda reproduces that grouping without needing the tree.
            var pending = new List<uint>();
            var ids = new List<uint>();
            var keys = new List<string>();

            uint? currentId = null;

            foreach (ChunkPointer ptr in reader.Pointers)
            {
                // "usdn" — Unique Static iDeNtifier — is the chunk that carries Shape.StaticID. It is
                // stored INLINE: the pointer record's third field holds the value itself rather than an
                // offset.
                //
                // NOT "spid". That was this reader's first mistake and it produced a convincing lie:
                // spid holds a 16-byte UUID in the data stream, so reading its first four bytes gives
                // values that never collide, and the census cheerfully reported "every id is unique".
                // Verified on a real file: fe 10 b3 7a 8d 1e ee 47 86 22 21 4c 14 46 95 78 — sixteen
                // distinct bytes, a UUIDv4. 128 bits cannot be the Int32 that StaticID returns.
                if (ptr.ChunkId == "usdn" && ptr.Inline)
                {
                    currentId = (uint)ptr.StreamOffset;
                    continue;
                }

                if (ptr.ChunkId != "loda" || ptr.Inline) continue;
                if (ptr.StreamNumber >= dataStreams.Count) continue;

                byte[] stream = dataStreams[ptr.StreamNumber];
                if (stream == null) continue;

                string key = GeometryKey(stream, ptr);

                ids.Add(currentId ?? 0);
                keys.Add(key);
                currentId = null;
            }

            var seenIds = new HashSet<uint>();
            var seenGeometry = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < ids.Count; i++)
            {
                census.Shapes.Add(new FileShape { Id = ids[i], GeometryKey = keys[i] });

                if (ids[i] != 0)
                {
                    census.ShapesWithId++;
                    if (!seenIds.Add(ids[i])) census.DuplicateIds++;
                }

                if (keys[i].Length > 0)
                {
                    census.ShapesWithGeometry++;
                    seenGeometry.Add(keys[i]);
                }
            }

            census.DistinctGeometries = seenGeometry.Count;
            _ = pending;

            if (census.Shapes.Count == 0)
                census.Note = "Nenhum objeto encontrado no arquivo.";
            else if (census.IdCoveragePercent < 95)
                census.Note = $"Só {census.ShapesWithId} de {census.Shapes.Count} formas "
                            + $"({census.IdCoveragePercent}%) têm identificador — o CorelDRAW só o cria "
                            + "sob demanda, então ele não serve de ponte para o documento vivo.";
            else if (census.DuplicateIds > 0)
                census.Note = census.DuplicateIds + " identificador(es) repetido(s) — não serve como chave.";

            return census;
        }

        private static uint ReadPayloadU32(ChunkPointer ptr, IList<byte[]> streams)
        {
            if (ptr.Inline || ptr.StreamNumber >= streams.Count) return 0;
            byte[] s = streams[ptr.StreamNumber];
            if (s == null || ptr.StreamOffset < 0 || ptr.StreamOffset + 4 > s.Length) return 0;
            return ReadU32(s, ptr.StreamOffset);
        }

        /// <summary>
        /// Fingerprint of the shape's coordinate argument. Same splitting rule as
        /// <see cref="ObjectPayloadAnalyzer"/> — the arg TYPE array is stored in REVERSE order.
        /// </summary>
        private static string GeometryKey(byte[] s, ChunkPointer ptr)
        {
            int b = ptr.StreamOffset;
            int len = ptr.PayloadLength;
            if (b < 0 || len < 20 || (long)b + len > s.Length) return "";

            int numArgs = (int)ReadU32(s, b + 4);
            int startArgs = (int)ReadU32(s, b + 8);
            int startTypes = (int)ReadU32(s, b + 12);
            if (numArgs <= 0 || numArgs > MaxArgs) return "";
            if ((long)b + startArgs + 4L * numArgs > s.Length) return "";
            if ((long)b + startTypes + 4L * numArgs > s.Length) return "";

            var offsets = new int[numArgs];
            var types = new int[numArgs];
            for (int i = 0; i < numArgs; i++) offsets[i] = (int)ReadU32(s, b + startArgs + 4 * i);
            for (int i = 0; i < numArgs; i++) types[numArgs - 1 - i] = (int)ReadU32(s, b + startTypes + 4 * i);

            for (int i = 0; i < numArgs; i++)
            {
                if (types[i] != ArgGeometry) continue;

                int from = b + offsets[i];
                int to = i + 1 < numArgs ? b + offsets[i + 1] : b + len;
                int n = to - from;
                if (n <= 0 || from < b || to > s.Length) return "";

                const ulong offsetBasis = 14695981039346656037;
                const ulong prime = 1099511628211;
                ulong hash = offsetBasis;
                for (int k = from; k < to; k++)
                {
                    hash ^= s[k];
                    hash *= prime;
                }
                return n.ToString() + ":" + hash.ToString("x16");
            }

            return "";
        }

        private static uint ReadU32(byte[] b, int offset) =>
            (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));
    }
}
