using System;

namespace Optimus.Core.Diagnostics
{
    /// <summary>
    /// Finds embedded ICC colour profiles by their FORMAT SIGNATURE rather than by where we expect
    /// them to be.
    ///
    /// <para>
    /// An ICC profile carries the ASCII signature <c>'acsp'</c> at byte offset 36 of its header, and
    /// its total size as a big-endian uint32 at offset 0 (ICC.1 spec). Matching on that pair is
    /// self-validating: the declared size has to be plausible AND the signature has to sit exactly
    /// 36 bytes in, which rejects almost all coincidental matches.
    /// </para>
    /// <para>
    /// Why this matters: an ICC profile was reported elsewhere as up to ~80% of a small vector .cdr,
    /// which would make it the dominant size lever. Scanning two real client files found NONE — so
    /// the product must detect the profile rather than assume it, and must not sell a lever the
    /// customer's files do not have.
    /// </para>
    /// </summary>
    public static class IccProfileScanner
    {
        private const int SignatureOffset = 36;

        /// <summary>Smallest plausible profile; anything under this is a coincidence.</summary>
        private const uint MinProfileBytes = 128;

        /// <summary>Largest plausible profile — guards against a garbage length field.</summary>
        private const uint MaxProfileBytes = 32u * 1024 * 1024;

        /// <summary>Total bytes of the ICC profiles found in the buffer.</summary>
        public static long TotalProfileBytes(byte[] buffer)
        {
            long total = 0;
            foreach (uint size in FindProfileSizes(buffer)) total += size;
            return total;
        }

        /// <summary>Declared sizes of every plausible ICC profile in the buffer.</summary>
        public static System.Collections.Generic.List<uint> FindProfileSizes(byte[] buffer)
        {
            var found = new System.Collections.Generic.List<uint>();
            if (buffer == null || buffer.Length < SignatureOffset + 4) return found;

            int limit = buffer.Length - 4;
            for (int i = SignatureOffset; i <= limit; i++)
            {
                if (buffer[i] != (byte)'a' || buffer[i + 1] != (byte)'c'
                    || buffer[i + 2] != (byte)'s' || buffer[i + 3] != (byte)'p') continue;

                int header = i - SignatureOffset;
                uint size = ReadBigEndianUInt32(buffer, header);
                if (size < MinProfileBytes || size > MaxProfileBytes) continue;

                // The profile must actually fit in what we have; otherwise it is a false positive
                // (or a truncated read, in which case counting it would overstate the file).
                if (header + (long)size > buffer.Length) continue;

                found.Add(size);
                i = header + (int)size;   // skip past this profile
            }
            return found;
        }

        private static uint ReadBigEndianUInt32(byte[] b, int offset) =>
            ((uint)b[offset] << 24) | ((uint)b[offset + 1] << 16) | ((uint)b[offset + 2] << 8) | b[offset + 3];
    }
}
