using System;
using System.Collections.Generic;
using System.Text;

namespace Optimus.Core.Diagnostics
{
    /// <summary>One leaf chunk of <c>root.dat</c>, resolved through the X6 pointer indirection.</summary>
    public sealed class ChunkPointer
    {
        public string ChunkId { get; set; } = "";

        /// <summary>Index into the data-stream list, or -1 when the payload is inline.</summary>
        public int StreamNumber { get; set; } = -1;

        /// <summary>Real logical length of the payload.</summary>
        public int PayloadLength { get; set; }

        /// <summary>Byte offset inside the data stream.</summary>
        public int StreamOffset { get; set; }

        public bool Inline => StreamNumber < 0;
    }

    /// <summary>
    /// Walks the RIFF structure of <c>content/root.dat</c> and resolves the X6 pointer records.
    ///
    /// <para>
    /// In a modern .cdr, <c>root.dat</c> is pure structure: EVERY leaf chunk is exactly 16 bytes and
    /// holds a pointer into <c>content/data/*.dat</c>. Tallying chunk lengths there therefore tells
    /// you nothing about weight — you have to follow the indirection. Pointer layout, verified over
    /// 807.588 records in 44 real files:
    /// <c>u32 streamNumber | u32 payloadLength | u32 streamOffset | u32 (0)</c>, with
    /// <c>0xFFFFFFFF</c> in the first word meaning the payload is inline (max 8 bytes).
    /// </para>
    /// <para>
    /// TWO PARSING RULES that differ from textbook RIFF and will desync a reader that ignores them:
    /// CorelDRAW does NOT word-align chunks, and runs of zero bytes appear between chunks. So: skip
    /// zeros, read the 8-byte header, then advance by an ABSOLUTE seek to (start + length).
    /// </para>
    /// </summary>
    public sealed class RiffPointerReader
    {
        private const int MaxNesting = 64;
        private const uint InlineMarker = 0xFFFFFFFF;

        /// <summary>Pointer records are exactly this long; that is the trigger for the indirection.</summary>
        private const int PointerRecordSize = 16;

        /// <summary>Leaf chunks found, with their payload resolved.</summary>
        public List<ChunkPointer> Pointers { get; } = new List<ChunkPointer>();

        /// <summary>Chunks skipped because they looked malformed — a non-zero value means the parse
        /// desynced and the numbers must not be trusted.</summary>
        public int Malformed { get; private set; }

        public void Read(byte[] rootDat)
        {
            Pointers.Clear();
            Malformed = 0;
            if (rootDat == null || rootDat.Length < 12) return;
            Walk(rootDat, 0, rootDat.Length, 0);
        }

        private void Walk(byte[] b, int start, int end, int depth)
        {
            if (depth > MaxNesting) return;
            int p = start;

            while (p + 8 <= end)
            {
                // CorelDRAW pads with zero runs instead of the RIFF alignment byte.
                while (p < end && b[p] == 0) p++;
                if (p + 8 > end) break;

                string id = Encoding.ASCII.GetString(b, p, 4);
                uint length = ReadU32(b, p + 4);
                int dataStart = p + 8;

                if (length > (uint)(end - dataStart)) { Malformed++; break; }

                if (id == "RIFF" || id == "LIST")
                {
                    // A container's length INCLUDES its children plus the 4-byte form type. Counting
                    // container lengths as payload double-counts and can exceed the file size.
                    Walk(b, dataStart + 4, Math.Min(end, dataStart + (int)length), depth + 1);
                }
                else if (length == PointerRecordSize)
                {
                    uint streamNumber = ReadU32(b, dataStart);
                    uint payloadLength = ReadU32(b, dataStart + 4);
                    uint streamOffset = ReadU32(b, dataStart + 8);

                    Pointers.Add(new ChunkPointer
                    {
                        ChunkId = id,
                        StreamNumber = streamNumber == InlineMarker ? -1 : (int)streamNumber,
                        PayloadLength = (int)Math.Min(payloadLength, int.MaxValue),
                        StreamOffset = (int)Math.Min(streamOffset, int.MaxValue),
                    });
                }

                p = dataStart + (int)length;   // absolute advance, no alignment padding
            }
        }

        private static uint ReadU32(byte[] b, int offset) =>
            (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));
    }
}
