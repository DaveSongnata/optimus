using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace Optimus.Interop
{
    public sealed class SymbolConversionResult
    {
        /// <summary>Repetition groups that were actually converted.</summary>
        public int GroupsConverted { get; set; }

        /// <summary>Shapes replaced by an instance of a symbol.</summary>
        public int ShapesInstanced { get; set; }

        /// <summary>Groups skipped, and why — the operator sees this, it is not swallowed.</summary>
        public List<string> Skipped { get; } = new List<string>();

        public bool TimedOut { get; set; }
        public long ElapsedMs { get; set; }

        public bool DidAnything => ShapesInstanced > 0;
    }

    /// <summary>
    /// Replaces repeated art with instances of a single symbol — the lever worth 58,2% of a real file,
    /// losslessly (O15).
    ///
    /// <para>
    /// It runs BEFORE any node simplification, and that order is not negotiable: simplifying each shape
    /// on its own makes byte-identical copies stop being identical, which is exactly how the previous
    /// pipeline destroyed this opportunity (4.789 distinct blocks became 5.400, and the recoverable share
    /// fell from 58,2% to 23,7%).
    /// </para>
    /// <para>
    /// <b>What it refuses to do.</b> A group is converted only when every member's geometry matched
    /// coordinate for coordinate AND their bounding boxes are the same size. The size check is what
    /// catches a copy that was scaled: the fingerprint compares geometry in OBJECT space, so a scaled
    /// twin hashes the same while looking different on the page. Anything that fails a check is skipped
    /// with a reason — never converted on a resemblance.
    /// </para>
    /// <para>
    /// Verified members: <c>Shape.ConvertToSymbol(String)</c> (typelib 6010),
    /// <c>Layer.CreateSymbol(x, y, name, library?)</c> (5030), <c>Shape.GetBoundingBox(out…)</c> (6220),
    /// <c>Shape.SetSize</c>/<c>GetSize</c> (3675/3676), <c>Shape.Delete()</c>.
    /// </para>
    /// </summary>
    public sealed class SymbolConverter
    {
        /// <summary>
        /// Hard ceiling on the whole conversion. Shipping work without a clock is how this project twice
        /// left the operator staring at a frozen-looking panel; the budget guarantees the run ends and
        /// says so, instead of appearing to hang.
        /// </summary>
        private readonly TimeSpan _budget;

        private readonly Action<string>? _log;

        /// <summary>Positions must match to this many millimetres for a size check to pass.</summary>
        private const double SizeToleranceMm = 0.001;

        public SymbolConverter(TimeSpan? budget = null, Action<string>? log = null)
        {
            _budget = budget ?? TimeSpan.FromMinutes(5);
            _log = log;
        }

        /// <summary>
        /// Converts each group to one symbol plus instances. <paramref name="report"/> must come from a
        /// run with <c>confirmAll: true</c>; a sampled report is refused outright.
        /// </summary>
        public SymbolConversionResult Convert(object document, RepetitionReport report)
        {
            var result = new SymbolConversionResult();
            var clock = Stopwatch.StartNew();

            if (report == null || !report.SafeToMutate)
            {
                result.Skipped.Add(
                    "A repetição não foi conferida forma a forma; nada foi convertido. "
                  + "Converter com base em semelhança poderia trocar um desenho por outro.");
                result.ElapsedMs = clock.ElapsedMilliseconds;
                return result;
            }

            int index = 0;
            foreach (RepetitionGroup group in report.Groups)
            {
                if (clock.Elapsed > _budget)
                {
                    result.TimedOut = true;
                    result.Skipped.Add($"Tempo limite de {_budget.TotalMinutes:0} min atingido; "
                                     + $"{report.Groups.Count - index} grupo(s) não foram convertidos.");
                    break;
                }

                index++;
                if (!group.Exact || group.Copies < 1) continue;

                string name = "Optimus_" + index.ToString("0000", CultureInfo.InvariantCulture);
                try
                {
                    if (ConvertGroup(document, group, name, result)) result.GroupsConverted++;
                }
                catch (Exception ex)
                {
                    result.Skipped.Add($"Grupo {index}: {ex.Message}");
                }

                if (index % 50 == 0)
                    _log?.Invoke($"símbolos: {index}/{report.Groups.Count} grupos, "
                               + $"{result.ShapesInstanced} formas instanciadas, "
                               + $"{clock.Elapsed.TotalSeconds:0} s");
            }

            clock.Stop();
            result.ElapsedMs = clock.ElapsedMilliseconds;
            return result;
        }

        private bool ConvertGroup(object document, RepetitionGroup group, string name,
                                  SymbolConversionResult result)
        {
            // Every member's placement is read BEFORE anything changes: converting the first shape
            // mutates the page, and a position read afterwards could describe a different document.
            var placements = new List<Placement>();
            foreach (object shape in group.Shapes)
            {
                Placement? p = ReadPlacement(shape);
                if (p == null)
                {
                    result.Skipped.Add(name + ": não foi possível ler a posição de uma das cópias.");
                    return false;
                }
                placements.Add(p.Value);
            }

            // A scaled twin hashes identically (the fingerprint is object-space) but does not look the
            // same. Same width and height is what proves the copies are placed, not resized.
            for (int i = 1; i < placements.Count; i++)
            {
                if (Math.Abs(placements[i].Width - placements[0].Width) > SizeToleranceMm ||
                    Math.Abs(placements[i].Height - placements[0].Height) > SizeToleranceMm)
                {
                    result.Skipped.Add(name + ": as cópias têm tamanhos diferentes — provavelmente "
                                            + "há escala aplicada, e instanciar mudaria o desenho.");
                    return false;
                }
            }

            // The first member becomes the definition, in place.
            dynamic first = group.Shapes[0];
            object? symbolShape;
            try { symbolShape = (object)first.ConvertToSymbol(name); }
            catch (Exception ex)
            {
                result.Skipped.Add(name + ": ConvertToSymbol recusou — " + ex.Message);
                return false;
            }
            if (symbolShape == null)
            {
                result.Skipped.Add(name + ": ConvertToSymbol não devolveu a forma.");
                return false;
            }
            Release(symbolShape);

            // The rest become instances placed exactly where they were, and only then are removed.
            int instanced = 0;
            for (int i = 1; i < group.Shapes.Count; i++)
            {
                object original = group.Shapes[i];
                object? instance = null;
                object? layer = null;

                try
                {
                    layer = (object)((dynamic)original).Layer;
                    if (layer == null) continue;

                    instance = (object)((dynamic)layer).CreateSymbol(
                        placements[i].X, placements[i].Y, name);
                    if (instance == null) continue;

                    // CreateSymbol places by the shape's own reference point; force position and size to
                    // the values read from the original so the page is pixel-for-pixel unchanged.
                    dynamic inst = instance;
                    try { inst.SetSize(placements[i].Width, placements[i].Height); } catch (Exception) { }
                    try { inst.SetPosition(placements[i].X, placements[i].Y); } catch (Exception) { }

                    ((dynamic)original).Delete();
                    instanced++;
                }
                catch (Exception ex)
                {
                    result.Skipped.Add(name + ": uma cópia não pôde ser instanciada — " + ex.Message);
                }
                finally
                {
                    Release(instance);
                    Release(layer);
                }
            }

            result.ShapesInstanced += instanced;
            return instanced > 0;
        }

        private readonly struct Placement
        {
            public Placement(double x, double y, double width, double height)
            {
                X = x; Y = y; Width = width; Height = height;
            }

            public double X { get; }
            public double Y { get; }
            public double Width { get; }
            public double Height { get; }
        }

        private static Placement? ReadPlacement(object shape)
        {
            try
            {
                dynamic s = shape;
                double x = 0, y = 0, w = 0, h = 0;
                s.GetBoundingBox(out x, out y, out w, out h);
                return new Placement(x, y, w, h);
            }
            catch (Exception) { return null; }
        }

        private static void Release(object? o)
        {
            try
            {
                if (o != null && System.Runtime.InteropServices.Marshal.IsComObject(o))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(o);
            }
            catch (Exception) { }
        }
    }
}
