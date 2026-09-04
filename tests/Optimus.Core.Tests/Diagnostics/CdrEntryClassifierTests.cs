using Optimus.Core.Diagnostics;
using Xunit;

namespace Optimus.Core.Tests.Diagnostics;

/// <summary>
/// Maps a ZIP entry path inside a modern .cdr onto the component it belongs to.
///
/// <para>
/// Grounded in the real structure of two client files (measured, not assumed): only
/// <c>content/root.dat</c> is a RIFF stream; <c>content/data/*.dat</c> are raw payload blobs
/// referenced by 16-byte pointers from it. That is why on-disk bytes can be attributed EXACTLY at
/// the entry level, with no estimation — and why the earlier plan to attribute by RIFF chunk
/// counting is unnecessary for the size report.
/// </para>
/// </summary>
public class CdrEntryClassifierTests
{
    [Theory]
    [InlineData("content/data/Bitmaps.dat")]
    public void Bitmaps_dat_is_raster(string path)
    {
        Assert.Equal(CdrComponent.Raster, CdrEntryClassifier.Classify(path));
    }

    /// <summary>
    /// The vector payload. A prior heuristic guessed "if Bitmaps.dat exists, dataN.dat is raster
    /// too" — measurement disproved it: in a file WITH Bitmaps.dat, data1.dat still holds the
    /// object data. Raster lives only in Bitmaps.dat.
    /// </summary>
    [Theory]
    [InlineData("content/data/data1.dat")]
    [InlineData("content/data/data2.dat")]
    [InlineData("content/data/page1.dat")]
    [InlineData("content/data/masterPage.dat")]
    [InlineData("content/root.dat")]
    public void Document_payload_is_vector(string path)
    {
        Assert.Equal(CdrComponent.Vector, CdrEntryClassifier.Classify(path));
    }

    [Theory]
    [InlineData("previews/thumbnail.png")]
    [InlineData("previews/page1.png")]
    public void Previews_are_preview(string path)
    {
        Assert.Equal(CdrComponent.Preview, CdrEntryClassifier.Classify(path));
    }

    /// <summary>
    /// <c>font/fontTable.dat</c> holds font NAMES, not outlines — 696 bytes listing
    /// "Man City Dragon 2324", "MS Gothic", "Arial", "Impact" in a real file. It is metadata-sized,
    /// so it must never be sold as a size lever.
    /// </summary>
    [Theory]
    [InlineData("font/fontTable.dat")]
    public void Font_table_is_fonts(string path)
    {
        Assert.Equal(CdrComponent.Fonts, CdrEntryClassifier.Classify(path));
    }

    [Theory]
    [InlineData("embed/embedding0")]
    [InlineData("embed/embedding1")]
    public void Embeddings_are_embedded(string path)
    {
        Assert.Equal(CdrComponent.Embedded, CdrEntryClassifier.Classify(path));
    }

    [Theory]
    [InlineData("META-INF/metadata.xml")]
    [InlineData("META-INF/container.xml")]
    [InlineData("META-INF/textinfo.xml")]
    [InlineData("META-INF/links.xml")]
    [InlineData("styles/document.cdss")]
    [InlineData("color/docPalette.xml")]
    [InlineData("color/color.xml")]
    [InlineData("mimetype")]
    [InlineData("content/dataFileList.dat")]
    public void Small_descriptors_are_metadata(string path)
    {
        Assert.Equal(CdrComponent.Metadata, CdrEntryClassifier.Classify(path));
    }

    [Fact]
    public void Unknown_paths_are_reported_as_other_never_dropped()
    {
        Assert.Equal(CdrComponent.Other, CdrEntryClassifier.Classify("something/new.bin"));
    }

    [Fact]
    public void Classification_is_case_insensitive_and_slash_agnostic()
    {
        Assert.Equal(CdrComponent.Raster, CdrEntryClassifier.Classify(@"content\data\BITMAPS.DAT"));
        Assert.Equal(CdrComponent.Preview, CdrEntryClassifier.Classify("PREVIEWS/Thumbnail.PNG"));
    }
}
