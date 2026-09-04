using System;
using System.IO;
using Optimus.Core.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Optimus.Core.Tests.Diagnostics;

/// <summary>
/// Answers the operator's real question about a vector-dominated file: "89% of it is object payload —
/// why does that need to be so big, and is there a way out?"
///
/// <para>
/// The container view cannot answer it. This drills one level down and measures, separately and after
/// real compression, how much of that payload is the drawing's geometry and how much is CorelDRAW
/// rewriting the same per-object style description thousands of times.
/// </para>
/// </summary>
public class ObjectPayloadAnalyzerTests
{
    private readonly ITestOutputHelper _out;
    public ObjectPayloadAnalyzerTests(ITestOutputHelper output) => _out = output;

    private static string? Sample(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "docs", relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // ── the pointer reader ────────────────────────────────────────────────────────

    /// <summary>Every leaf in root.dat is a 16-byte pointer; the reader must resolve them, not
    /// count their 16 bytes as payload.</summary>
    [Fact]
    public void Resolves_pointer_records_from_the_real_root_stream()
    {
        string? path = Sample(Path.Combine("otimizacoes_arquivos", "kaneki.cdr"));
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path, analyzePayload: true);

        Assert.NotNull(r.Payload);
        Assert.True(r.Payload!.ObjectRecords > 1000,
            $"expected thousands of object records, got {r.Payload.ObjectRecords}");
        // A desync would show up as a pile of malformed records.
        Assert.True(r.Payload.Malformed < r.Payload.ObjectRecords / 10,
            $"too many malformed records ({r.Payload.Malformed}) — the parse desynced");
    }

    [Fact]
    public void Empty_or_null_input_is_handled()
    {
        var reader = new RiffPointerReader();
        reader.Read(null!);
        Assert.Empty(reader.Pointers);
        reader.Read(new byte[4]);
        Assert.Empty(reader.Pointers);

        PayloadBreakdown empty = new ObjectPayloadAnalyzer().Analyze(null!, null!);
        Assert.Equal(0, empty.TotalLogical);
    }

    // ── the measurement that decides the advice ──────────────────────────────────

    /// <summary>
    /// THE experiment. Prints the geometry-vs-style split, logically and after real compression, for
    /// the operator's own working file. The assertion is deliberately loose — the point is the
    /// measurement, and the numbers land in the test output as the record of it.
    /// </summary>
    [Fact]
    public void Measures_geometry_versus_duplicated_style_on_the_real_working_file()
    {
        string? path = Sample(Path.Combine("otimizacoes_arquivos", "kaneki.cdr"));
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path, analyzePayload: true);
        PayloadBreakdown p = r.Payload!;

        _out.WriteLine($"kaneki.cdr — arquivo: {r.Composition.TotalBytes:N0} bytes");
        _out.WriteLine($"objetos analisados: {p.ObjectRecords:N0} (malformados: {p.Malformed})");
        _out.WriteLine("");
        _out.WriteLine($"geometria    logico={p.GeometryLogical:N0}  comprimido={p.GeometryCompressed:N0}  " +
                       $"({p.PercentOfCompressed(p.GeometryCompressed)}% do comprimido)");
        _out.WriteLine($"estilo JSON  logico={p.StyleJsonLogical:N0}  comprimido={p.StyleJsonCompressed:N0}  " +
                       $"({p.PercentOfCompressed(p.StyleJsonCompressed)}% do comprimido)");
        _out.WriteLine($"outros       logico={p.OtherLogical:N0}  comprimido={p.OtherCompressed:N0}  " +
                       $"({p.PercentOfCompressed(p.OtherCompressed)}% do comprimido)");
        _out.WriteLine("");
        _out.WriteLine($"estilo por objeto: {p.StyleBytesPerObject:N0} bytes logicos");

        Assert.True(p.TotalLogical > 0);
        Assert.True(p.TotalCompressed > 0);
    }

    /// <summary>
    /// The same drill-down on the file the operator prepared with only the art he wants to optimise.
    /// Comparing the two says whether removing the outer frames changed anything structural.
    /// </summary>
    [Fact]
    public void Measures_the_trimmed_working_file_too()
    {
        string? path = Sample(Path.Combine("otimizacoes_arquivos", "kaneki_no_ponto.cdr"));
        if (path == null) return;

        CdrContainerReader.Result r = new CdrContainerReader().Read(path, analyzePayload: true);
        PayloadBreakdown p = r.Payload!;

        _out.WriteLine($"kaneki_no_ponto.cdr — arquivo: {r.Composition.TotalBytes:N0} bytes");
        _out.WriteLine($"objetos: {p.ObjectRecords:N0}  malformados: {p.Malformed}");
        _out.WriteLine($"geometria  comprimido={p.GeometryCompressed:N0} ({p.PercentOfCompressed(p.GeometryCompressed)}%)");
        _out.WriteLine($"estiloJSON comprimido={p.StyleJsonCompressed:N0} ({p.PercentOfCompressed(p.StyleJsonCompressed)}%)");
        _out.WriteLine($"outros     comprimido={p.OtherCompressed:N0} ({p.PercentOfCompressed(p.OtherCompressed)}%)");
        _out.WriteLine($"estilo por objeto: {p.StyleBytesPerObject:N0} bytes");

        Assert.True(p.ObjectRecords > 0);
    }

    /// <summary>
    /// Every object pays the style toll regardless of how simple it is — that is what makes OBJECT
    /// COUNT a lever independent of node count. Pinned so the fact stays visible.
    /// </summary>
    [Fact]
    public void Style_cost_is_per_object_not_per_node()
    {
        string? path = Sample(Path.Combine("otimizacoes_arquivos", "kaneki.cdr"));
        if (path == null) return;

        PayloadBreakdown p = new CdrContainerReader().Read(path, analyzePayload: true).Payload!;
        if (p.StyleJsonLogical == 0) return;   // no style args in this file

        Assert.True(p.StyleBytesPerObject > 50,
            $"style state should cost hundreds of bytes per object, got {p.StyleBytesPerObject}");
    }
}
