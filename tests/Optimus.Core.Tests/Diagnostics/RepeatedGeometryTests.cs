using System;
using System.Collections.Generic;
using System.IO;
using Optimus.Core.Diagnostics;
using Xunit;

namespace Optimus.Core.Tests.Diagnostics;

/// <summary>
/// Guards the repeated-geometry census — the measurement that found the biggest lever in the product
/// (O15) and, at the same time, found that our own node simplification was destroying it.
/// </summary>
public class RepeatedGeometryTests
{
    private static string? SampleDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "docs", "otimizacoes_arquivos");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static PayloadBreakdown? Analyze(params string[] relative)
    {
        string? dir = SampleDir();
        if (dir == null) return null;

        string path = Path.Combine(dir, Path.Combine(relative));
        if (!File.Exists(path)) return null;

        return new CdrContainerReader().Read(path, analyzePayload: true).Payload;
    }

    /// <summary>Without this the tests below would pass by finding no files at all.</summary>
    [Fact]
    public void The_sample_files_are_reachable_so_the_other_tests_are_not_vacuous()
    {
        Assert.NotNull(Analyze("kaneki_no_ponto.cdr"));
    }

    /// <summary>
    /// THE finding: this drawing is repeated art. 16.914 objects, 4.789 distinct coordinate blocks.
    /// If this ever stops holding, the census broke — not the file.
    /// </summary>
    [Fact]
    public void The_drawing_stores_far_fewer_distinct_shapes_than_it_has_objects()
    {
        PayloadBreakdown? p = Analyze("kaneki_no_ponto.cdr");
        if (p == null) return;

        Assert.True(p.ObjectRecords > 16_000, "contagem de objetos mudou: " + p.ObjectRecords);
        Assert.True(p.DistinctGeometries < p.ObjectRecords / 3,
            $"{p.DistinctGeometries} geometrias distintas para {p.ObjectRecords} objetos — "
          + "o censo de repetição parou de detectar");
        Assert.True(p.RepeatedSharePercent > 65, "repetição medida: " + p.RepeatedSharePercent + "%");
    }

    /// <summary>
    /// The saving is worth more than half the file, and it is LOSSLESS — the coordinates are
    /// byte-identical, so nothing about the art changes.
    /// </summary>
    [Fact]
    public void Storing_each_distinct_shape_once_would_save_more_than_half_the_file()
    {
        string? dir = SampleDir();
        if (dir == null) return;

        string path = Path.Combine(dir, "kaneki_no_ponto.cdr");
        if (!File.Exists(path)) return;

        PayloadBreakdown p = new CdrContainerReader().Read(path, analyzePayload: true).Payload!;
        long fileBytes = new FileInfo(path).Length;

        Assert.True(p.RepetitionSavingShareOfFile(fileBytes) > 50,
            "economia por repetição caiu para "
          + p.RepetitionSavingShareOfFile(fileBytes) + "% do arquivo");
    }

    /// <summary>
    /// THE regression that matters most.
    ///
    /// <para>
    /// Simplifying each shape on its own turns byte-identical copies into near-identical ones, and the
    /// repetition lever evaporates. Measured: our own run took the file from 4.789 distinct blocks to
    /// 5.400, and from 58,2% recoverable to 23,7%. This test pins that the OPTIMIZED file is measurably
    /// worse on this axis than the original — so that when the pipeline is reordered (repetition first,
    /// simplification last), the fix is provable rather than believed.
    /// </para>
    /// </summary>
    [Fact]
    public void Simplifying_before_deduplicating_destroys_the_repetition_lever()
    {
        string? dir = SampleDir();
        if (dir == null) return;

        string original = Path.Combine(dir, "kaneki_no_ponto.cdr");
        string optimized = Path.Combine(dir, "ja_otimizado", "kaneki_no_ponto.cdr");
        if (!File.Exists(original) || !File.Exists(optimized)) return;

        var reader = new CdrContainerReader();
        PayloadBreakdown before = reader.Read(original, analyzePayload: true).Payload!;
        PayloadBreakdown after = reader.Read(optimized, analyzePayload: true).Payload!;

        double shareBefore = before.RepetitionSavingShareOfFile(new FileInfo(original).Length);
        double shareAfter = after.RepetitionSavingShareOfFile(new FileInfo(optimized).Length);

        // Fewer objects, yet MORE distinct geometries: the copies drifted apart.
        Assert.True(after.DistinctGeometries > before.DistinctGeometries,
            $"esperado que a simplificação aumentasse as geometrias distintas "
          + $"({before.DistinctGeometries} → {after.DistinctGeometries})");

        Assert.True(shareAfter < shareBefore - 20,
            $"a alavanca de repetição caiu de {shareBefore}% para {shareAfter}% — "
          + "se isso deixou de valer, a ordem do pipeline mudou e O15 precisa ser revisto");
    }

    /// <summary>
    /// The census must not be fooled by empty or tiny blocks: if the "repeats" were degenerate, the
    /// average distinct block would be BIGGER than the average block overall, not smaller.
    /// </summary>
    [Fact]
    public void The_repeated_blocks_are_substantial_not_degenerate()
    {
        PayloadBreakdown? p = Analyze("kaneki_no_ponto.cdr");
        if (p == null) return;

        double averageAll = p.GeometryLogical / (double)p.ObjectRecords;
        double averageDistinct = p.DistinctGeometryLogical / (double)p.DistinctGeometries;

        Assert.True(averageDistinct > 100,
            "bloco distinto médio de apenas " + averageDistinct + " bytes — repetições podem ser vazias");
        Assert.True(averageDistinct < averageAll * 1.5,
            "blocos distintos muito maiores que a média: o censo pode estar contando lixo");
    }

    /// <summary>Both real drawings show the same signature — it is the art, not one broken export.</summary>
    [Fact]
    public void Both_real_drawings_show_the_same_repetition_signature()
    {
        foreach (string name in new[] { "kaneki.cdr", "kaneki_no_ponto.cdr" })
        {
            PayloadBreakdown? p = Analyze(name);
            if (p == null) continue;

            Assert.True(p.RepeatedSharePercent > 65, name + ": " + p.RepeatedSharePercent + "%");
        }
    }

    /// <summary>A file with no repetition must report no saving, not a fabricated one.</summary>
    [Fact]
    public void A_payload_with_nothing_repeated_reports_no_saving()
    {
        var p = new PayloadBreakdown
        {
            ObjectRecords = 10,
            DistinctGeometries = 10,
            GeometryCompressed = 5_000,
            DistinctGeometryCompressed = 5_000,
        };

        Assert.Equal(0, p.RepeatedShapes);
        Assert.Equal(0, p.RepetitionSavingBytes);
        Assert.Equal(0, p.RepetitionSavingShareOfFile(100_000));
    }
}
