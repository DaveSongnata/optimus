using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Optimus.Core.Diagnostics;
using Xunit;

namespace Optimus.Core.Tests.Diagnostics;

/// <summary>
/// Integration tests against the REAL client files in <c>docs/Arquivos_teste/</c>.
///
/// <para>
/// These are the regression that keeps the size report honest. The numbers asserted here were
/// measured by opening the containers directly, so if a refactor changes what the product reports
/// about the customer's own files, these fail. When the sample files are absent (a clean clone),
/// the tests skip rather than fail — but they must never be deleted, because they are the only
/// place the product's claims are checked against reality.
/// </para>
/// </summary>
public class CdrContainerReaderTests
{
    private static string? SampleDir()
    {
        // tests/Optimus.Core.Tests/bin/<cfg>/<tfm>/ → repo root is five levels up.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "docs", "Arquivos_teste");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? Sample(string name)
    {
        string? dir = SampleDir();
        if (dir == null) return null;
        string path = Path.Combine(dir, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Guards against a VACUOUS GREEN. Every sample-based test below returns early when the file is
    /// missing, so a broken path resolver would make them all "pass" without asserting anything.
    /// This test fails loudly instead, and is the reason the suite can be trusted.
    /// </summary>
    [Fact]
    public void Sample_files_are_reachable_so_the_other_tests_are_not_vacuous()
    {
        string? dir = SampleDir();
        Assert.False(dir == null,
            "docs/Arquivos_teste não foi encontrado a partir de " + AppContext.BaseDirectory +
            " — os testes de integração estariam passando sem verificar nada.");
        Assert.NotNull(Sample("arquivo.cdr"));
        Assert.NotNull(Sample("arquivo2.cdr"));
    }

    /// <summary>
    /// `arquivo.cdr`: 63% raster. This is THE file that proves the 50% target needs resampling —
    /// deleting every curve still leaves ~37% of it on disk.
    /// </summary>
    [Fact]
    public void Real_bitmap_heavy_file_is_measured_as_raster_dominated()
    {
        string? path = Sample("arquivo.cdr");
        if (path == null) return; // sample not present in this checkout

        CdrContainerReader.Result r = new CdrContainerReader().Read(path);

        Assert.True(r.Composition.Available, r.Note);
        double raster = r.Composition.PercentOf(CdrComponent.Raster);
        Assert.InRange(raster, 55, 70);

        ReductionCeiling ceiling = ReductionCeiling.For(r.Composition);
        Assert.True(ceiling.RasterDominated);
        Assert.True(ceiling.CeilingWithoutRasterPercent < 50);

        // The document declares its own payload files; Bitmaps.dat must be among them.
        Assert.Contains(r.DeclaredDataFiles, f => f.IndexOf("Bitmaps", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>`arquivo2.cdr`: ~94% vector, no raster payload at all.</summary>
    [Fact]
    public void Real_vector_heavy_file_has_no_raster_payload()
    {
        string? path = Sample("arquivo2.cdr");
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path);

        Assert.True(r.Composition.Available, r.Note);
        Assert.Equal(0, r.Composition.Of(CdrComponent.Raster));
        Assert.InRange(r.Composition.PercentOf(CdrComponent.Vector), 85, 99);
        Assert.DoesNotContain(r.DeclaredDataFiles, f => f.IndexOf("Bitmaps", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>
    /// Raster is stored UNCOMPRESSED: 88.5 MB expanding from 2.86 MB stored. That ratio is the
    /// reason resampling pays so well, so it is worth pinning down.
    /// </summary>
    [Fact]
    public void Raster_payload_is_stored_uncompressed()
    {
        string? path = Sample("arquivo.cdr");
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path);
        CdrContainerReader.EntryInfo? bitmaps =
            r.Entries.FirstOrDefault(e => e.Component == CdrComponent.Raster);

        Assert.NotNull(bitmaps);
        double ratio = bitmaps!.CompressedBytes / (double)bitmaps.LogicalBytes;
        Assert.True(ratio < 0.10, $"expected raw pixel data (ratio << 1), got {ratio:P1}");
    }

    /// <summary>
    /// Neither client file embeds an ICC profile. A profile was reported elsewhere as ~80% of a
    /// small vector .cdr, which would make it the dominant lever — it is simply not present here,
    /// and the product must not promise a lever these files do not have.
    /// </summary>
    [Fact]
    public void Client_files_embed_no_icc_profile()
    {
        foreach (string name in new[] { "arquivo.cdr", "arquivo2.cdr" })
        {
            string? path = Sample(name);
            if (path == null) continue;

            CdrContainerReader.Result r = new CdrContainerReader().Read(path);
            Assert.Equal(0, r.IccBytesFound);
        }
    }

    /// <summary>The font table yields referenced font names without opening CorelDRAW.</summary>
    [Fact]
    public void Font_table_yields_referenced_font_names()
    {
        string? path = Sample("arquivo2.cdr");
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path);

        Assert.NotEmpty(r.FontNames);
        Assert.Contains(r.FontNames, n => n.IndexOf("Arial", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.Contains(r.FontNames, n => n.IndexOf("Impact", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>Accounted bytes must never exceed the file: the report cannot claim more than exists.</summary>
    [Fact]
    public void Accounted_bytes_never_exceed_the_file_size()
    {
        foreach (string name in new[] { "arquivo.cdr", "arquivo2.cdr" })
        {
            string? path = Sample(name);
            if (path == null) continue;

            CdrContainerReader.Result r = new CdrContainerReader().Read(path);
            Assert.True(r.Composition.AccountedBytes <= r.Composition.TotalBytes,
                $"{name}: accounted {r.Composition.AccountedBytes} > total {r.Composition.TotalBytes}");
        }
    }

    /// <summary>Reading never modifies the file (R2.5) — verified by size + last-write time.</summary>
    [Fact]
    public void Reading_does_not_modify_the_file()
    {
        string? path = Sample("arquivo2.cdr");
        if (path == null) return;

        var before = new FileInfo(path);
        long size = before.Length;
        DateTime written = before.LastWriteTimeUtc;

        new CdrContainerReader().Read(path);

        var after = new FileInfo(path);
        Assert.Equal(size, after.Length);
        Assert.Equal(written, after.LastWriteTimeUtc);
    }

    /// <summary>A pre-X4 (bare RIFF) .cdr degrades to "unavailable" with a note, never an exception
    /// and never a fabricated breakdown.</summary>
    [Fact]
    public void Legacy_riff_cdr_degrades_gracefully()
    {
        string temp = Path.Combine(Path.GetTempPath(), "optimus_legacy_test.cdr");
        File.WriteAllBytes(temp, Encoding.ASCII.GetBytes("RIFF____CDRDsomething"));
        try
        {
            CdrContainerReader.Result r = new CdrContainerReader().Read(temp);
            Assert.False(r.Composition.Available);
            Assert.NotEqual("", r.Note);
        }
        finally { File.Delete(temp); }
    }

    [Fact]
    public void Missing_file_is_reported_not_thrown()
    {
        CdrContainerReader.Result r = new CdrContainerReader()
            .Read(Path.Combine(Path.GetTempPath(), "does_not_exist_optimus.cdr"));

        Assert.False(r.Composition.Available);
        Assert.NotEqual("", r.Note);
    }

    /// <summary>A synthetic container proves classification independently of the sample files.</summary>
    [Fact]
    public void Synthetic_container_is_classified_by_component()
    {
        string temp = Path.Combine(Path.GetTempPath(), "optimus_synth_test.cdr");
        if (File.Exists(temp)) File.Delete(temp);
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                void Add(string name, int bytes)
                {
                    using Stream s = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                    s.Write(new byte[bytes], 0, bytes);
                }
                Add("content/data/Bitmaps.dat", 4000);
                Add("content/data/data1.dat", 2000);
                Add("previews/thumbnail.png", 500);
                Add("mimetype", 45);
            }

            CdrContainerReader.Result r = new CdrContainerReader().Read(temp);

            Assert.True(r.Composition.Available, r.Note);
            Assert.True(r.Composition.Of(CdrComponent.Raster) > 0);
            Assert.True(r.Composition.Of(CdrComponent.Vector) > 0);
            Assert.True(r.Composition.Of(CdrComponent.Preview) > 0);
            Assert.True(r.Composition.Of(CdrComponent.Metadata) > 0);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
