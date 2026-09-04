namespace Optimus.Core.Diagnostics
{
    /// <summary>
    /// Tells a Corel font embedding apart from any other <c>embed/*</c> payload.
    ///
    /// <para>
    /// The distinction matters because embedded fonts reach 93% of a real file and have a specific,
    /// available remedy — re-saving without font embedding — whereas an OLE payload does not.
    /// </para>
    /// <para>
    /// Header verified across ten blobs in four real files:
    /// <c>u32 = 1</c>, then <c>u32 = 0x00000803</c> (constant in every blob observed), then a kind
    /// byte, then a UTF-16LE version string ("Version 7.01") that joins to
    /// <c>font/fontTable.dat</c>.
    /// </para>
    /// <para>
    /// LABELLED LIMIT: the payload itself is undocumented and contains no <c>sfnt</c> table
    /// directory — it is not a TTF/OTF and cannot be subset or re-encoded by us. The probe therefore
    /// identifies the blob; it does not interpret it.
    /// </para>
    /// </summary>
    public static class EmbeddedFontProbe
    {
        /// <summary>The constant second word seen in every observed font embedding.</summary>
        private const uint FontEmbeddingMarker = 0x00000803;

        public static bool LooksLikeFontEmbedding(byte[] head)
        {
            if (head == null || head.Length < 9) return false;

            uint first = ReadUInt32LE(head, 0);
            uint marker = ReadUInt32LE(head, 4);
            if (first != 1 || marker != FontEmbeddingMarker) return false;

            // A UTF-16LE string should follow the kind byte: ASCII char then a zero high byte.
            if (head.Length < 11) return true;   // header matched; too short to check further
            return head[10] == 0x00 && head[9] >= 0x20 && head[9] <= 0x7E;
        }

        private static uint ReadUInt32LE(byte[] b, int offset) =>
            (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));
    }
}
