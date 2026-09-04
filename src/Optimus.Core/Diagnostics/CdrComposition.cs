using System.Collections.Generic;
using System.Linq;

namespace Optimus.Core.Diagnostics
{
    /// <summary>What a component of a .cdr file is, for byte attribution.</summary>
    public enum CdrComponent
    {
        /// <summary>Raster payload (<c>content/data/Bitmaps.dat</c>). Stored UNCOMPRESSED inside the
        /// container — 88.5 MB of it deflated to 2.86 MB in a real file — so resampling is by far
        /// the biggest available size lever when it is present.</summary>
        Raster,

        /// <summary>Object/geometry payload: <c>root.dat</c> (RIFF skeleton) plus the
        /// <c>data*.dat</c> / <c>page*.dat</c> / <c>masterPage.dat</c> blobs it points into.</summary>
        Vector,

        /// <summary>Embedded preview PNGs (<c>previews/*</c>). Removable at save time with no effect
        /// on the artwork; measured at 1.6–1.9% of two real files.</summary>
        Preview,

        /// <summary>Font TABLE (<c>font/fontTable.dat</c>) — names, not outlines. 696 bytes in a real
        /// file, so never a size lever, but the authoritative used-font list for the audit.</summary>
        Fonts,

        /// <summary>
        /// Embedded ICC colour profile (<c>color/profiles/{cmyk,rgb,grayscale}/*</c>).
        /// <b>The single largest size lever that exists</b>: measured at 97.3% of a real client file
        /// (1.367.132 of 1.405.181 bytes, one <c>isocoated_v2_eci.icc</c>) while the entire drawing
        /// was 0.9%. It is a plain ZIP entry, NOT a RIFF chunk — which is why a signature scan of the
        /// document streams finds nothing and reports a false negative.
        /// </summary>
        IccProfile,

        /// <summary>
        /// Embedded font glyph payload (<c>embed/embedding*</c> matching the Corel font-embedding
        /// signature). Measured up to 93% of a file. Not subsettable by us — the only lever is
        /// re-saving without font embedding.
        /// </summary>
        EmbeddedFonts,

        /// <summary>Other embedded/linked objects (<c>embed/*</c>), e.g. OLE payloads.</summary>
        Embedded,

        /// <summary>Descriptors: manifest, styles, palettes, colour context, mimetype.</summary>
        Metadata,

        /// <summary>Anything unrecognised. Reported, never dropped, so the totals always reconcile.</summary>
        Other,
    }

    /// <summary>
    /// Exact on-disk byte attribution of a .cdr, per component.
    ///
    /// <para>
    /// The numbers are COMPRESSED (as-stored) sizes of ZIP entries, which is what the operator sees
    /// in Explorer — the only figure worth reporting as fact. No estimation is involved: measurement
    /// of two real client files showed that raster and vector payload live in SEPARATE container
    /// entries, so nothing has to be split proportionally.
    /// </para>
    /// </summary>
    public sealed class CdrComposition
    {
        private readonly Dictionary<CdrComponent, long> _bytes;

        private CdrComposition(long totalBytes, Dictionary<CdrComponent, long> bytes)
        {
            TotalBytes = totalBytes;
            _bytes = bytes;
        }

        /// <summary>The file's real size on disk.</summary>
        public long TotalBytes { get; }

        /// <summary>True when the container could not be read (e.g. a pre-X4 RIFF-only .cdr).</summary>
        public bool Available => TotalBytes > 0 && _bytes.Count > 0;

        public IReadOnlyDictionary<CdrComponent, long> Bytes => _bytes;

        public long Of(CdrComponent component) => _bytes.TryGetValue(component, out long v) ? v : 0;

        /// <summary>Share of the file, in percent, held by a component.</summary>
        public double PercentOf(CdrComponent component) =>
            TotalBytes <= 0 ? 0 : Of(component) * 100.0 / TotalBytes;

        /// <summary>Bytes accounted for. Compared against <see cref="TotalBytes"/> it exposes
        /// container overhead (ZIP headers) — the report must never claim more than the file has.</summary>
        public long AccountedBytes => _bytes.Values.Sum();

        public static CdrComposition Unavailable() =>
            new CdrComposition(0, new Dictionary<CdrComponent, long>());

        public static CdrComposition FromBytes(
            long totalBytes, IEnumerable<(CdrComponent Component, long Bytes)> parts)
        {
            var map = new Dictionary<CdrComponent, long>();
            foreach ((CdrComponent component, long bytes) in parts)
            {
                map.TryGetValue(component, out long current);
                map[component] = current + bytes;
            }
            return new CdrComposition(totalBytes, map);
        }
    }
}
