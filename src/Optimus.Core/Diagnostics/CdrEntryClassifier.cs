using System;

namespace Optimus.Core.Diagnostics
{
    /// <summary>
    /// Maps a container entry path onto the component it belongs to.
    ///
    /// <para>
    /// Every rule here was read off two real client files, not inferred. The structure of a modern
    /// (X6+) .cdr is a ZIP whose <c>content/dataFileList.dat</c> is the document's OWN manifest of
    /// its payload files — in one file it literally reads
    /// <c>Bitmaps.dat / data1.dat / masterPage.dat / page1.dat</c>. So raster payload is identified
    /// by the container itself, not by a guess.
    /// </para>
    /// <para>
    /// A heuristic that was TRIED AND DISPROVED: "if Bitmaps.dat exists, then dataN.dat is raster
    /// too". Measurement showed a file with Bitmaps.dat whose data1.dat still held 32% of the file
    /// as object data. Raster lives in Bitmaps.dat and nowhere else.
    /// </para>
    /// </summary>
    public static class CdrEntryClassifier
    {
        public static CdrComponent Classify(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath)) return CdrComponent.Other;

            string p = entryPath.Replace('\\', '/').ToLowerInvariant();
            string file = p.Substring(p.LastIndexOf('/') + 1);

            // Raster payload — named by the document's own manifest.
            if (file.StartsWith("bitmaps", StringComparison.Ordinal)) return CdrComponent.Raster;

            // ICC profile — MUST be tested before the generic `color/` rule below. Getting this
            // order wrong filed a 1.37 MB profile (97.3% of a real file) as "metadata", which made
            // the whole size report meaningless for exactly the files where it matters most.
            if (p.StartsWith("color/profiles/", StringComparison.Ordinal)) return CdrComponent.IccProfile;

            // Embedded preview images.
            if (p.StartsWith("previews/", StringComparison.Ordinal)) return CdrComponent.Preview;

            // Font TABLE (names only, ~700 bytes) — metadata-sized, not embedded outlines.
            if (p.StartsWith("font/", StringComparison.Ordinal)) return CdrComponent.Fonts;

            // Embedded objects. `embed/embedding*` normally carries FONT glyph payload (up to 93% of
            // a file); the caller refines this to EmbeddedFonts when the payload matches the Corel
            // font-embedding signature, since only fonts have a "re-save without embedding" lever.
            if (p.StartsWith("embed/", StringComparison.Ordinal)) return CdrComponent.Embedded;

            // Descriptors. dataFileList is the manifest itself, so it is metadata, not payload.
            if (p.StartsWith("meta-inf/", StringComparison.Ordinal)
                || p.StartsWith("styles/", StringComparison.Ordinal)
                || p.StartsWith("color/", StringComparison.Ordinal)
                || p == "mimetype"
                || file == "datafilelist.dat")
                return CdrComponent.Metadata;

            // Object/geometry payload: the RIFF skeleton plus the blobs it points into.
            if (p.StartsWith("content/", StringComparison.Ordinal) && file.EndsWith(".dat", StringComparison.Ordinal))
                return CdrComponent.Vector;

            return CdrComponent.Other;
        }
    }
}
