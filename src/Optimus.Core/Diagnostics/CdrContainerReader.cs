using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Optimus.Core.Diagnostics
{
    /// <summary>
    /// Reads a <c>.cdr</c>'s byte composition straight from the file, WITHOUT CorelDRAW.
    ///
    /// <para>
    /// A modern .cdr (X6 and later) is a ZIP container — verified on real client files by the
    /// <c>PK\x03\x04</c> magic. Every entry's COMPRESSED length is its true cost on disk, so the
    /// report is exact rather than estimated. Older eras exist (≤X3 is a bare RIFF stream, X4/X5 is
    /// a ZIP holding <c>content/riffData.cdr</c>); anything that is not a readable ZIP degrades to
    /// <see cref="CdrComposition.Unavailable"/> instead of guessing.
    /// </para>
    /// <para>
    /// Pure BCL, no third-party dependency, and it never opens the document in CorelDRAW — so it
    /// cannot modify what it measures (R2.5).
    /// </para>
    /// </summary>
    public sealed class CdrContainerReader
    {
        /// <summary>ZIP local file header magic — how we know the file is a modern container.</summary>
        private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 };

        /// <summary>
        /// Cap on how many decompressed bytes we will scan looking for an embedded ICC profile.
        /// Raster payload can decompress to ~88 MB, and scanning that to answer a side question is
        /// not worth the wait. The result is reported as "not found within the scanned range", never
        /// as "no profile exists".
        /// </summary>
        private const long IccScanBudgetBytes = 8L * 1024 * 1024;

        public sealed class Result
        {
            public CdrComposition Composition { get; set; } = CdrComposition.Unavailable();

            /// <summary>Per-entry detail, largest first — what the operator can be shown as proof.</summary>
            public List<EntryInfo> Entries { get; } = new List<EntryInfo>();

            /// <summary>Payload files the document declares in <c>content/dataFileList.dat</c>.</summary>
            public List<string> DeclaredDataFiles { get; } = new List<string>();

            /// <summary>Font names read from <c>font/fontTable.dat</c> (names only; no outlines).</summary>
            public List<string> FontNames { get; } = new List<string>();

            /// <summary>Embedded ICC profile bytes, read EXACTLY from the container entries.</summary>
            public long IccBytesFound { get; set; }

            /// <summary>Paths of the embedded profiles, so the report can name them.</summary>
            public List<string> IccProfileNames { get; } = new List<string>();

            /// <summary>ICC bytes found by signature scan inside document streams (legacy pre-X6).</summary>
            public long LegacyIccBytesFound { get; set; }

            /// <summary>Which spaces have a profile embedded and which actually hold objects.</summary>
            public ColorContext ColorContext { get; set; } = new ColorContext();

            /// <summary>
            /// Each shape's identifier paired with its geometry fingerprint — the bridge between the
            /// exact file census and the live COM shapes. Null unless the payload was analysed.
            /// </summary>
            public ShapeIdCensus? ShapeIds { get; set; }

            /// <summary>False when the legacy signature scan hit its budget.</summary>
            public bool IccScanComplete { get; set; } = true;

            /// <summary>Why the composition is unavailable, when it is.</summary>
            public string Note { get; set; } = "";

            /// <summary>Geometry vs duplicated style state inside the object payload (opt-in).</summary>
            public PayloadBreakdown? Payload { get; set; }
        }

        public sealed class EntryInfo
        {
            public string Path { get; set; } = "";
            public CdrComponent Component { get; set; }

            /// <summary>Bytes as stored — the real cost on disk.</summary>
            public long CompressedBytes { get; set; }

            /// <summary>Bytes when expanded. A very low ratio means uncompressed payload: raster is
            /// stored raw, which is exactly why resampling pays off so well.</summary>
            public long LogicalBytes { get; set; }

            /// <summary>Stored with no compression — cannot be squeezed further (preview PNGs are).</summary>
            public bool Stored { get; set; }
        }

        /// <summary>
        /// Reads the composition AND, when requested, drills into the object payload to explain what
        /// makes a vector-dominated file heavy. The drill-down decompresses the data streams, so it is
        /// opt-in rather than part of every read.
        /// </summary>
        public Result Read(string cdrPath, bool analyzePayload)
        {
            Result result = Read(cdrPath);
            if (!analyzePayload || !result.Composition.Available) return result;

            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(cdrPath))
                {
                    byte[]? root = null;
                    var byName = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string lower = entry.FullName.Replace('\\', '/').ToLowerInvariant();
                        if (lower == "content/root.dat") root = ReadBytes(entry, int.MaxValue);
                        else if (lower.StartsWith("content/data/", StringComparison.Ordinal))
                            byName[Path.GetFileName(entry.FullName)] = ReadBytes(entry, int.MaxValue);
                    }

                    if (root == null) return result;

                    // Stream index = order declared in dataFileList.dat, NOT ZIP order (they differ).
                    var ordered = new List<byte[]>();
                    foreach (string declared in result.DeclaredDataFiles)
                        ordered.Add(byName.TryGetValue(declared.Trim(), out byte[]? s) ? s : new byte[0]);

                    result.Payload = new ObjectPayloadAnalyzer().Analyze(root, ordered);

                    // Pair each shape's identifier with its geometry, so the exact grouping computed
                    // here can be applied to live COM shapes via Page.FindShape(StaticID:) — the bridge
                    // that replaces GetCurveInfo, which CorelDRAW 2024 refuses to marshal.
                    result.ShapeIds = new ShapeIdReader().Read(root, ordered);
                }
            }
            catch (Exception ex) { result.Note = (result.Note + " payload: " + ex.Message).Trim(); }

            return result;
        }

        public Result Read(string cdrPath)
        {
            var result = new Result();
            if (string.IsNullOrWhiteSpace(cdrPath) || !File.Exists(cdrPath))
            {
                result.Note = "Arquivo não encontrado.";
                return result;
            }

            long totalBytes;
            try { totalBytes = new FileInfo(cdrPath).Length; }
            catch (Exception ex) { result.Note = "Não foi possível ler o arquivo: " + ex.Message; return result; }

            if (!LooksLikeZip(cdrPath))
            {
                // Pre-X4 .cdr is a bare RIFF stream. Saying so is more useful than a wrong number.
                result.Note = "Formato antigo (não é container ZIP) — composição por componente indisponível.";
                return result;
            }

            var parts = new List<(CdrComponent, long)>();
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(cdrPath))
                {
                    long iccBudget = IccScanBudgetBytes;

                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        CdrComponent component = CdrEntryClassifier.Classify(entry.FullName);
                        string lower = entry.FullName.Replace('\\', '/').ToLowerInvariant();

                        // Refine `embed/*`: font glyph payload carries a recognisable Corel header, and
                        // only fonts have the "re-save without embedding" lever, so the distinction
                        // changes the advice we give.
                        if (component == CdrComponent.Embedded
                            && EmbeddedFontProbe.LooksLikeFontEmbedding(ReadBytes(entry, 64)))
                            component = CdrComponent.EmbeddedFonts;

                        parts.Add((component, entry.CompressedLength));
                        result.Entries.Add(new EntryInfo
                        {
                            Path = entry.FullName,
                            Component = component,
                            CompressedBytes = entry.CompressedLength,
                            LogicalBytes = entry.Length,
                            // A STORED entry cannot be squeezed further — the preview PNGs are stored,
                            // which makes them pure incompressible overhead worth naming.
                            Stored = entry.CompressedLength == entry.Length && entry.Length > 0,
                        });

                        // The ICC profile is a ZIP ENTRY, not a chunk inside the document streams —
                        // so its size is read exactly here. A signature scan of the streams finds
                        // nothing and would report a false "no profile".
                        if (component == CdrComponent.IccProfile)
                        {
                            result.IccBytesFound += entry.CompressedLength;
                            result.IccProfileNames.Add(entry.FullName);
                        }
                        else if (lower.EndsWith("datafilelist.dat", StringComparison.Ordinal))
                            result.DeclaredDataFiles.AddRange(ReadLines(entry));
                        else if (lower.StartsWith("font/", StringComparison.Ordinal))
                            result.FontNames.AddRange(FontTableReader.ReadNames(ReadBytes(entry, 64 * 1024)));
                        else if (lower == "color/color.xml")
                            result.ColorContext = ColorContextReader.Parse(ReadText(entry));
                        else if (ShouldScanForIcc(component, entry, ref iccBudget))
                        {
                            // Legacy path only: pre-X6 files carry the profile in an `iccd` RIFF
                            // chunk instead of a ZIP entry, so the signature scan still has a job.
                            result.LegacyIccBytesFound +=
                                IccProfileScanner.TotalProfileBytes(ReadBytes(entry, int.MaxValue));
                        }
                        else if (component == CdrComponent.Raster || entry.Length > IccScanBudgetBytes)
                            result.IccScanComplete = false;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Note = "Container ilegível: " + ex.Message;
                return result;
            }

            result.Composition = CdrComposition.FromBytes(totalBytes, parts);
            result.Entries.Sort((a, b) => b.CompressedBytes.CompareTo(a.CompressedBytes));
            return result;
        }

        /// <summary>Only small/structural entries are scanned for ICC, within a global budget.</summary>
        private static bool ShouldScanForIcc(CdrComponent component, ZipArchiveEntry entry, ref long budget)
        {
            if (component == CdrComponent.Raster || component == CdrComponent.Preview) return false;
            if (entry.Length <= 0 || entry.Length > budget) return false;
            budget -= entry.Length;
            return true;
        }

        private static bool LooksLikeZip(string path)
        {
            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    var head = new byte[4];
                    if (fs.Read(head, 0, 4) < 4) return false;
                    for (int i = 0; i < 4; i++) if (head[i] != ZipMagic[i]) return false;
                    return true;
                }
            }
            catch { return false; }
        }

        private static byte[] ReadBytes(ZipArchiveEntry entry, int max)
        {
            using (Stream s = entry.Open())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[81920];
                int read, total = 0;
                while ((read = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, read);
                    total += read;
                    if (total >= max) break;
                }
                return ms.ToArray();
            }
        }

        private static string ReadText(ZipArchiveEntry entry)
        {
            try
            {
                using (Stream s = entry.Open())
                using (var sr = new StreamReader(s))
                    return sr.ReadToEnd();
            }
            catch { return ""; }
        }

        private static IEnumerable<string> ReadLines(ZipArchiveEntry entry)
        {
            var lines = new List<string>();
            try
            {
                using (Stream s = entry.Open())
                using (var sr = new StreamReader(s))
                {
                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length > 0) lines.Add(line);
                    }
                }
            }
            catch { }
            return lines;
        }
    }
}
